using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using DialogueMachine.UI;

namespace DialogueMachine;

public sealed partial class App : Application
{
    public override void Initialize() { AvaloniaXamlLoader.Load(this); SetPalette("Dark"); }
    public void SetPalette(string theme)
    {
        RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var palette = theme switch
        {
            "Light" => new[] { "#F4F5FA", "#FFFFFF", "#EAECF3", "#D5D8E4", "#202538", "#596278", "#6C4FDB", "#64DCC2", "#E8E1FF" },
            "High contrast" => new[] { "#000000", "#000000", "#171717", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#4938BC", "#8CFFDB", "#373060" },
            _ => new[] { "#12141C", "#1A1D28", "#242836", "#34394B", "#EEF0FA", "#A6ADC2", "#7554D6", "#79DFC8", "#35304D" }
        };
        var keys = new[] { "Base", "Panel", "Raised", "Line", "Ink", "Muted", "Accent", "Mint", "Selection" };
        for (var i = 0; i < keys.Length; i++) Resources[keys[i]] = new SolidColorBrush(Color.Parse(palette[i]));
        Resources["MintInk"] = new SolidColorBrush(Color.Parse(theme == "Light" ? "#107866" : palette[7]));
        Resources["GraphGrid"] = theme == "High contrast" ? Brushes.Transparent : Resources["Line"];
        Resources["GraphSelection"] = new SolidColorBrush(Color.Parse(theme == "High contrast" ? "#FFFF00" : theme == "Light" ? "#6242C7" : "#B59AFF"));
        Resources["GraphIncoming"] = Resources["MintInk"];
        Resources["GraphLink"] = new SolidColorBrush(Color.Parse(theme == "High contrast" ? "#FFFFFF" : theme == "Light" ? "#929AAF" : "#667089"));
        Resources["GraphLive"] = new SolidColorBrush(Color.Parse(theme == "High contrast" ? "#FF9F1C" : theme == "Light" ? "#B85300" : "#FFB454"));
        Resources["GraphLiveTrail"] = new SolidColorBrush(Color.Parse(theme == "High contrast" ? "#B0FF9F1C" : theme == "Light" ? "#80B85300" : "#80FFB454"));
        var fluent = Styles.OfType<FluentTheme>().First();
        fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Color.Parse(palette[6]) };
        fluent.Palettes[ThemeVariant.Light] = new ColorPaletteResources { Accent = Color.Parse(palette[6]) };
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow(desktop.Args);
        base.OnFrameworkInitializationCompleted();
    }
}
