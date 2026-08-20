using Avalonia;

namespace nanoMIDIPlayer;

static class Program {
    // STAThread wird nur auf windows gebraucht, schadet auf macOS aber nicht
    [STAThread]
    static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // von avalonia tooling erwartet
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
