using DialogueMachine.Runtime;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace DialogueMachine.Core.LiveLink;

public sealed record LiveLinkOptions(string DiscoveryPath, string AppVersion)
{
    public int Port { get; init; } = LiveLinkServer.DefaultPort;
    public TimeSpan HelloTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan SilenceTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

// Localhost bridge for engine plugins. The exported file stays the source of truth.
// Events are raised on background threads; UI subscribers must marshal them.
public sealed class LiveLinkServer(LiveLinkOptions options) : IDisposable
{
    public const int Protocol = 1, DefaultPort = 47615, PortAttempts = 11, MaxClients = 8, MaxMessageBytes = 16 * 1024 * 1024;
    public const string UpdateUrl = "https://github.com/xLostPro/DialogueMachine/releases/latest";
    private const int OutboundQueue = 64;

    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private readonly List<Client> clients = [];
    private HttpListener? listener;
    private string projectName = "";
    private string? storyFrame;
    private int connections, nextClient;
    private bool disposed;

    public string Token { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    public int Port { get; private set; }
    public string? StorySha256 { get; private set; }
    public int ClientCount { get { lock (gate) return clients.Count; } }

    public event Action<int>? ClientsChanged;
    public event Action<string>? ClientLeft;
    public event Action<LiveRunnerState>? RunnerState;
    public event Action<LiveRunnerEvent>? RunnerEvent;
    public event Action<string>? RevealRequested;

    public void Start()
    {
        for (var port = options.Port; listener is null && port < options.Port + PortAttempts; port++)
        {
            var candidate = new HttpListener(); candidate.Prefixes.Add($"http://127.0.0.1:{port}/dialogue-machine/");
            try { candidate.Start(); listener = candidate; Port = port; }
            catch (Exception ex) when (ex is HttpListenerException or SocketException) { candidate.Close(); }
        }
        if (listener is null) throw new IOException($"Live link found no free port from {options.Port} to {options.Port + PortAttempts - 1}.");
        try { WriteDiscovery(); }
        catch { Dispose(); throw; }
        _ = Task.Run(AcceptLoop);
    }

    // Keeps the last good sha, because engines still hold that story while this one is broken.
    public void Publish(string project, RuntimeHandoff handoff, bool send = true)
    {
        var payload = JsonNode.Parse(RuntimeExport.Serialize(handoff))!.AsObject();
        payload["ProjectName"] = project; // Additive: tells engines which asset a push belongs to.
        lock (gate) { projectName = project; StorySha256 = handoff.StorySha256; SetStory(Frame("story.updated", payload), send); }
    }
    public void PublishInvalid(string project, IEnumerable<Diagnostic> diagnostics, bool send = true)
    {
        var list = new JsonArray(diagnostics.Select(d => (JsonNode)new JsonObject { ["stateId"] = d.StateId, ["severity"] = d.Severity, ["message"] = d.Message }).ToArray());
        lock (gate) { projectName = project; SetStory(Frame("story.invalid", new JsonObject { ["diagnostics"] = list }), send); }
    }
    public void ClearStory() { lock (gate) { projectName = ""; StorySha256 = null; storyFrame = null; } }
    private void SetStory(string frame, bool send)
    {
        storyFrame = frame;
        if (send) foreach (var client in clients) client.Send(frame);
    }

    public void PlayFrom(string sceneId, int seed, IReadOnlyDictionary<string, int> numeric, IReadOnlyDictionary<string, string> text)
    {
        var numbers = new JsonObject(); foreach (var (key, value) in numeric) numbers[key] = value;
        var strings = new JsonObject(); foreach (var (key, value) in text) strings[key] = value;
        var frame = Frame("play.from", new JsonObject { ["sceneId"] = sceneId, ["seed"] = seed, ["numeric"] = numbers, ["text"] = strings });
        lock (gate) foreach (var client in clients) client.Send(frame);
    }
    public void SetVariable(string clientId, string runnerId, string key, int? number, string? text)
    {
        var payload = new JsonObject { ["runnerId"] = runnerId, ["key"] = key };
        if (number is { } value) payload["number"] = value; else payload["text"] = text;
        lock (gate) clients.FirstOrDefault(c => c.Id == clientId)?.Send(Frame("runtime.setVariable", payload));
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        stop.Cancel();
        listener?.Close();
        DeleteDiscovery();
    }

    private void WriteDiscovery()
    {
        var path = Path.GetFullPath(options.DiscoveryPath); var temp = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = new JsonObject { ["port"] = Port, ["token"] = Token, ["pid"] = Environment.ProcessId, ["appVersion"] = options.AppVersion, ["protocol"] = Protocol }.ToJsonString();
        // The token is a secret: other users on this machine must not read it.
        var file = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) file.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var writer = new StreamWriter(temp, new UTF8Encoding(false), file)) writer.Write(content);
        File.Move(temp, path, true);
    }
    private void DeleteDiscovery()
    {
        try
        {
            // A second running copy may have replaced the file; leave that one's alone.
            if (!File.Exists(options.DiscoveryPath)) return;
            using (var document = JsonDocument.Parse(File.ReadAllText(options.DiscoveryPath)))
                if (Number(document.RootElement, "pid", -1) != Environment.ProcessId) return;
            File.Delete(options.DiscoveryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private async Task AcceptLoop()
    {
        while (true)
        {
            HttpListenerContext context;
            try { context = await listener!.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
            _ = Task.Run(() => Serve(context));
        }
    }

    private async Task Serve(HttpListenerContext context)
    {
        // Browsers always send Origin and engine clients don't, so this keeps websites out.
        if (context.Request.Headers["Origin"] is not null) { Reject(context, 403); return; }
        if (!context.Request.IsWebSocketRequest) { Reject(context, 400); return; }
        if (Interlocked.Increment(ref connections) > MaxClients) { Interlocked.Decrement(ref connections); Reject(context, 503); return; }
        try
        {
            var accepted = await context.AcceptWebSocketAsync(null, 64 * 1024, Timeout.InfiniteTimeSpan);
            using var socket = accepted.WebSocket;
            await Run(new Client(socket, Interlocked.Increment(ref nextClient).ToString()));
        }
        catch (Exception ex) when (ex is WebSocketException or HttpListenerException or IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException) { }
        finally { Interlocked.Decrement(ref connections); }
    }
    private static void Reject(HttpListenerContext context, int status)
    {
        try { context.Response.StatusCode = status; context.Response.Close(); }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { }
    }

    private async Task Run(Client client)
    {
        using var life = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        var writer = Write(client, life);
        var watchdog = Watch(client, life);
        try
        {
            var buffer = new byte[64 * 1024];
            while (await Receive(client, buffer, life.Token) is { } text && Handle(client, text)) { }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException) { }
        finally
        {
            // Let a final error frame reach the engine before the socket goes away.
            client.Outbound.Writer.TryComplete();
            await Task.WhenAny(writer, Task.Delay(1000));
            await life.CancelAsync();
            await Task.WhenAll(writer, watchdog);
            client.Socket.Abort();
            bool left; int count;
            lock (gate) { left = clients.Remove(client); count = clients.Count; }
            if (left) { ClientsChanged?.Invoke(count); ClientLeft?.Invoke(client.Id); }
        }
    }

    private static async Task Write(Client client, CancellationTokenSource life)
    {
        try
        {
            await foreach (var message in client.Outbound.Reader.ReadAllAsync(life.Token))
                await client.Socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, life.Token);
            await client.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", life.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException) { life.Cancel(); }
    }

    private async Task Watch(Client client, CancellationTokenSource life)
    {
        var started = Environment.TickCount64; var pinged = started;
        try
        {
            while (true)
            {
                await Task.Delay(100, life.Token);
                var now = Environment.TickCount64;
                if (!client.Ready)
                {
                    if (now - started <= options.HelloTimeout.TotalMilliseconds) continue;
                    Refuse(client, "unauthorized", "No hello arrived in time."); return;
                }
                if (now - Volatile.Read(ref client.LastHeard) > options.SilenceTimeout.TotalMilliseconds) { await life.CancelAsync(); return; }
                if (now - pinged >= options.PingInterval.TotalMilliseconds) { pinged = now; client.Send(Frame("ping")); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task<string?> Receive(Client client, byte[] buffer, CancellationToken token)
    {
        using var message = new MemoryStream();
        while (true)
        {
            var result = await client.Socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (message.Length + result.Count > MaxMessageBytes) { Refuse(client, "too-large", "Messages are limited to 16 MB."); return null; }
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;
            if (result.MessageType == WebSocketMessageType.Text) return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0); // The protocol only uses text frames.
        }
    }

    // Returns false when the connection should close.
    private bool Handle(Client client, string text)
    {
        Volatile.Write(ref client.LastHeard, Environment.TickCount64);
        JsonDocument document;
        try { document = JsonDocument.Parse(text); }
        catch (JsonException)
        {
            if (client.Ready) return true;
            Refuse(client, "unauthorized", "The first message must be hello."); return false;
        }
        using (document)
        {
            var root = document.RootElement;
            var type = Text(root, "type"); var id = Text(root, "id");
            var payload = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("payload", out var value) ? value : default;
            if (!client.Ready)
            {
                if (type == "hello") return Hello(client, payload, id);
                Refuse(client, "unauthorized", "The first message must be hello."); return false;
            }
            switch (type)
            {
                case "ping": client.Send(Frame("pong", null, id)); break;
                case "story.request": lock (gate) if (storyFrame is not null) client.Send(storyFrame); break;
                case "runtime.state": if (ParseState(client, payload) is { } state) RunnerState?.Invoke(state); break;
                case "runtime.event": if (Text(payload, "runnerId") is { } runner) RunnerEvent?.Invoke(new(client.Id + "/" + runner, Text(payload, "type") ?? "", Text(payload, "targetId") ?? "", Text(payload, "parameter"))); break;
                case "editor.reveal": if (Text(payload, "sceneId") is { } scene) RevealRequested?.Invoke(scene); break;
            }
            return true;
        }
    }

    private bool Hello(Client client, JsonElement payload, string? id)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Text(payload, "token") ?? ""), Encoding.UTF8.GetBytes(Token)))
        {
            Refuse(client, "unauthorized", "The live-link token is wrong. Read livelink.json again."); return false;
        }
        if (Number(payload, "protocol", -1) != Protocol || Number(payload, "schemaVersion", -1) < Handoff.SupportedSchemaVersion)
        {
            Refuse(client, "incompatible", $"This plugin doesn't support live-link protocol {Protocol} and runtime schema {Handoff.SupportedSchemaVersion}. Update the Dialogue Machine plugin."); return false;
        }
        client.Engine = Text(payload, "engine") is { Length: > 0 } engine ? engine : "engine";
        var outdated = Version.TryParse(Text(payload, "pluginVersion"), out var plugin) && Version.TryParse(options.AppVersion, out var app) && plugin < app;
        int count;
        lock (gate)
        {
            client.Send(Frame("welcome", new JsonObject
            {
                ["appVersion"] = options.AppVersion, ["protocol"] = Protocol, ["schemaVersion"] = Handoff.SupportedSchemaVersion,
                ["contract"] = RuntimeExport.Contract, ["projectName"] = projectName, ["updateUrl"] = outdated ? UpdateUrl : null
            }, id));
            if (storyFrame is not null) client.Send(storyFrame);
            client.Ready = true; clients.Add(client); count = clients.Count;
        }
        ClientsChanged?.Invoke(count);
        return true;
    }

    private static LiveRunnerState? ParseState(Client client, JsonElement payload)
    {
        if (Text(payload, "runnerId") is not { } runner || Text(payload, "status") is not { } status || status is not ("started" or "step" or "ended")) return null;
        var numeric = new Dictionary<string, int>(StringComparer.Ordinal); var text = new Dictionary<string, string>(StringComparer.Ordinal);
        if (payload.TryGetProperty("numeric", out var numbers) && numbers.ValueKind == JsonValueKind.Object)
            foreach (var entry in numbers.EnumerateObject()) if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var value)) numeric[entry.Name] = value;
        if (payload.TryGetProperty("text", out var strings) && strings.ValueKind == JsonValueKind.Object)
            foreach (var entry in strings.EnumerateObject()) if (entry.Value.ValueKind == JsonValueKind.String) text[entry.Name] = entry.Value.GetString()!;
        return new(client.Id, client.Engine, runner, Text(payload, "runnerName") ?? runner, Text(payload, "projectName") ?? "", Text(payload, "storySha256") ?? "", status, Text(payload, "sceneId"), Number(payload, "pageIndex", 0), Text(payload, "choiceId"), numeric, text);
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int Number(JsonElement element, string name, int fallback)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : fallback;
    private static string Frame(string type, JsonNode? payload = null, string? id = null)
    {
        var frame = new JsonObject { ["protocol"] = Protocol, ["type"] = type };
        if (id is not null) frame["id"] = id;
        frame["payload"] = payload ?? new JsonObject();
        return frame.ToJsonString();
    }
    private static void Refuse(Client client, string code, string message)
    {
        client.Send(Frame("error", new JsonObject { ["code"] = code, ["message"] = message }));
        client.Outbound.Writer.TryComplete();
    }

    private sealed class Client(WebSocket socket, string id)
    {
        public readonly WebSocket Socket = socket;
        public readonly string Id = id;
        // A stuck engine loses its oldest messages instead of holding up the editor.
        public readonly Channel<string> Outbound = Channel.CreateBounded<string>(new BoundedChannelOptions(OutboundQueue) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        public string Engine = "";
        public volatile bool Ready;
        public long LastHeard = Environment.TickCount64;
        public void Send(string frame) => Outbound.Writer.TryWrite(frame);
    }
}
