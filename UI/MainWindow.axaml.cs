using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using nanoMIDIPlayer.Core;
using nanoMIDIPlayer.Player;

namespace nanoMIDIPlayer.UI;

public partial class MainWindow : Window {
    readonly PlaybackEngine engine = new();
    readonly HotkeyManager hotkeys = new();
    readonly MidiPlayerView midiView;
    readonly SettingsView settingsView;
    readonly InfoView infoView;

    public MainWindow() {
        InitializeComponent();
        midiView = new MidiPlayerView(engine);
        settingsView = new SettingsView(engine);
        infoView = new InfoView();
        MainContent.Content = midiView;

        Topmost = Config.Data.appUI.topmost;
        Opened += OnOpened;
        Closing += (_, _) => { hotkeys.Dispose(); engine.Stop(); };

        // updater will neu starten -> fenster schliessen laesst den Closing-handler oben
        // sauber aufraeumen (keys loesen, engine stoppen), danach beendet sich die app
        Updater.OnRestartNeeded += () => Dispatcher.UIThread.Post(Close);
        Updater.CleanupOldExe();
    }

    void OnOpened(object? sender, EventArgs e) {
        var hk = Config.Data.hotkeys;
        hotkeys.Register(hk.play, () => midiView.Play());
        hotkeys.Register(hk.pause, () => midiView.PauseHotkey());
        hotkeys.Register(hk.stop, () => midiView.StopHotkey());
        hotkeys.Register(hk.speedup, () => midiView.SpeedHotkey(true));
        hotkeys.Register(hk.slowdown, () => midiView.SpeedHotkey(false));
        hotkeys.Start();
        midiView.ReportPlatform();

        // update-check im hintergrund, blockiert den start nicht
        _ = Updater.StartupCheckAsync();
    }

    static UserControl NotPorted(string name) {
        var tb = new TextBlock {
            Text = $"{name}\n\nnicht portiert (nur MIDI Player)",
            Foreground = Brushes.White,
            FontSize = 15,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return new UserControl {
            Background = new SolidColorBrush(Color.FromRgb(0x0a, 0x0a, 0x0a)),
            Content = tb
        };
    }

    void ShowMidi(object? s, RoutedEventArgs e) => MainContent.Content = midiView;
    void ShowSettings(object? s, RoutedEventArgs e) => MainContent.Content = settingsView;
    void ShowInfo(object? s, RoutedEventArgs e) => MainContent.Content = infoView;
    void ShowDrums(object? s, RoutedEventArgs e) => MainContent.Content = NotPorted("Drums Player");
    void ShowHub(object? s, RoutedEventArgs e) => MainContent.Content = NotPorted("MIDI Hub");
    void ShowQwerty(object? s, RoutedEventArgs e) => MainContent.Content = NotPorted("MIDI To QWERTY");
}
