using Avalonia;
using System.IO;

namespace DialogueMachine;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex) when (args.Contains("--smoke-test"))
        {
            var i = Array.IndexOf(args, "--smoke-output");
            var output = i >= 0 && i + 1 < args.Length ? args[i + 1] : AppContext.BaseDirectory;
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "native-smoke-error.txt"), ex.ToString()); Environment.ExitCode = 1;
        }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
