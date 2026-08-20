using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using nanoMIDIPlayer.Core;
using nanoMIDIPlayer.UI;

namespace nanoMIDIPlayer;

public partial class App : Application {
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted() {
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => WriteCrash(ex.ExceptionObject);

        Config.Load();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }

    static void WriteCrash(object? ex) {
        try {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nanoMIDI_crash.txt"),
                ex?.ToString() ?? "unbekannter fehler");
        } catch { }
    }
}
