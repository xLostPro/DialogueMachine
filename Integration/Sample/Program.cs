using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
// Disposable sample sessions need no persistent user-profile keys.
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
var app = builder.Build();
app.MapStaticAssets();
app.UseAntiforgery();
app.MapRazorComponents<DialogueMachine.Sample.App>().AddInteractiveServerRenderMode();
app.Run();
