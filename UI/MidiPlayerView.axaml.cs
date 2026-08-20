using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using nanoMIDIPlayer.Core;
using nanoMIDIPlayer.Player;

namespace nanoMIDIPlayer.UI;

public partial class MidiPlayerView : UserControl {
    readonly PlaybackEngine engine;
    readonly List<string> lines = new();
    bool init = true;
    bool draggingSeek = false;   // true waehrend der nutzer den spul-slider zieht
    bool updatingSeek = false;   // true waehrend wir den slider programmatisch setzen (kein Seek() ausloesen)

    static readonly (int off, string label)[] PitchMap = {
        (-24, "Lowest"), (-12, "Low"), (0, "Middle"), (12, "High"), (24, "Highest")
    };
    static readonly string[] KeyNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public MidiPlayerView(PlaybackEngine engine) {
        InitializeComponent();
        this.engine = engine;
        engine.OnLog += Log;
        engine.OnTime += t => Dispatcher.UIThread.Post(() => Timeline.Text = t);
        engine.OnStopped += () => Dispatcher.UIThread.Post(ResetPlayState);
        engine.OnProgress += (pos, dur) => Dispatcher.UIThread.Post(() => UpdateSeekProgress(pos, dur));

        WireEvents();
        LoadState();
        init = false;
        Log("Hello! :)");
    }

    // avalonia kennt keine WPF-trigger/ValueChanged-attribute im XAML:
    // slider + switches werden hier verdrahtet
    void WireEvents() {
        SustainSwitch.IsCheckedChanged += (_, _) => Toggle(p => p.sustain = SustainSwitch.IsChecked == true);
        NoDoublesSwitch.IsCheckedChanged += (_, _) => Toggle(p => p.noDoubles = NoDoublesSwitch.IsChecked == true);
        VelocitySwitch.IsCheckedChanged += (_, _) => Toggle(p => p.velocity = VelocitySwitch.IsChecked == true);
        Keys88Switch.IsCheckedChanged += (_, _) => Toggle(p => p.keys88 = Keys88Switch.IsChecked == true);
        SwapYZSwitch.IsCheckedChanged += (_, _) => Toggle(p => p.swapYZ = SwapYZSwitch.IsChecked == true);

        OnValue(FingerSlider, FingerChanged);
        OnValue(SpeedSlider, SpeedChanged);
        OnValue(SeekSlider, SeekSliderChanged);

        // seek erst beim loslassen ausloesen, nicht bei jedem zwischenwert
        // (sonst hunderte Seek()-aufrufe waehrend des ziehens)
        SeekSlider.PointerPressed += (_, _) => draggingSeek = true;
        SeekSlider.PointerReleased += (_, _) => {
            draggingSeek = false;
            if (engine.Duration > 0) engine.Seek(SeekSlider.Value);
        };
    }

    static void OnValue(Slider s, Action<double> cb) =>
        s.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) cb(s.Value); };

    void Toggle(Action<PlayerConfig> apply) {
        if (init) return;
        apply(Config.Data.midiPlayer);
        Config.Save();
    }

    void LoadState() {
        var p = Config.Data.midiPlayer;
        SustainSwitch.IsChecked = p.sustain;
        NoDoublesSwitch.IsChecked = p.noDoubles;
        VelocitySwitch.IsChecked = p.velocity;
        Keys88Switch.IsChecked = p.keys88;
        SwapYZSwitch.IsChecked = p.swapYZ;

        OutputDevice.ItemsSource = new[] { "No MIDI Output" };
        OutputDevice.SelectedIndex = 0;

        PlayHk.Content = Config.Data.hotkeys.play.ToUpperInvariant();
        PauseHk.Content = Config.Data.hotkeys.pause.ToUpperInvariant();
        StopHk.Content = Config.Data.hotkeys.stop.ToUpperInvariant();
        SpeedUpHk.Content = Config.Data.hotkeys.speedup.ToUpperInvariant();
        SlowHk.Content = Config.Data.hotkeys.slowdown.ToUpperInvariant();

        // file dropdown
        RefreshFileList();

        // pitch
        PitchDropdown.ItemsSource = PitchMap.Select(x => x.label).ToArray();
        var pl = PitchMap.FirstOrDefault(x => x.off == p.pitchOffset).label ?? "Middle";
        PitchDropdown.SelectedItem = pl;

        // key
        KeyDropdown.ItemsSource = KeyNames;
        KeyDropdown.SelectedIndex = p.transposeOffset is >= 0 and <= 11 ? p.transposeOffset : 0;

        // finger
        FingerSlider.Value = p.fingerLimit;
        FingerValue.Text = p.fingerLimit > 10 ? "∞" : p.fingerLimit.ToString();

        // speed
        SpeedSlider.Value = 100;
        SpeedEntry.Text = "100";
    }

    void RefreshFileList() {
        var p = Config.Data.midiPlayer;
        var list = p.midiList.Where(File.Exists).Select(Path.GetFileName).ToList();
        FileDropdown.ItemsSource = list;
        if (!string.IsNullOrEmpty(p.currentFile) && File.Exists(p.currentFile)) {
            var name = Path.GetFileName(p.currentFile);
            if (list.Contains(name)) FileDropdown.SelectedItem = name;
        }
    }

    void Log(string msg) {
        Dispatcher.UIThread.Post(() => {
            lines.Add(msg);
            if (lines.Count > 200) lines.RemoveAt(0);
            ConsoleText.Text = string.Join("\n", lines);
            ConsoleScroll.ScrollToEnd();
        });
    }

    // beim start einmal melden ob die plattform tasten senden darf
    public void ReportPlatform() {
        var d = engine.Diagnose();
        if (d != null) Log(d);
    }

    // --- file ---
    async void SelectFile(object? s, RoutedEventArgs e) {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "MIDI Datei waehlen",
            AllowMultiple = false,
            FileTypeFilter = new[] {
                new FilePickerFileType("MIDI") {
                    Patterns = new[] { "*.mid", "*.midi" },
                    AppleUniformTypeIdentifiers = new[] { "public.midi-audio" },
                    MimeTypes = new[] { "audio/midi" }
                }
            }
        });

        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) { Log("datei nicht lokal lesbar"); return; }

        var p = Config.Data.midiPlayer;
        if (!p.midiList.Contains(path)) p.midiList.Add(path);
        p.currentFile = path;
        Config.Save();
        RefreshFileList();
        FileDropdown.SelectedItem = Path.GetFileName(path);
        Log($"geladen: {Path.GetFileName(path)}");
    }

    void FileSelected(object? s, SelectionChangedEventArgs e) {
        if (init || FileDropdown.SelectedItem is not string name) return;
        var match = Config.Data.midiPlayer.midiList.FirstOrDefault(f => Path.GetFileName(f) == name);
        if (match != null) { Config.Data.midiPlayer.currentFile = match; Config.Save(); }
    }

    // --- pitch / key ---
    void PitchChanged(object? s, SelectionChangedEventArgs e) {
        if (init || PitchDropdown.SelectedItem is not string label) return;
        Config.Data.midiPlayer.pitchOffset = PitchMap.First(x => x.label == label).off;
        Config.Save();
    }
    void KeyChanged(object? s, SelectionChangedEventArgs e) {
        if (init) return;
        Config.Data.midiPlayer.transposeOffset = KeyDropdown.SelectedIndex;
        Config.Save();
    }

    // --- finger ---
    void FingerChanged(double raw) {
        int v = (int)Math.Round(raw);
        FingerValue.Text = v > 10 ? "∞" : v.ToString();
        if (init) return;
        Config.Data.midiPlayer.fingerLimit = v;
        Config.Save();
    }
    void ResetFinger(object? s, RoutedEventArgs e) => FingerSlider.Value = 11;

    // --- speed ---
    void SpeedChanged(double raw) {
        if (init) return;
        int v = (int)Math.Round(raw);
        SpeedEntry.Text = v.ToString();
        engine.SetSpeedPercent(v);
    }
    void SpeedEntryChanged(object? s, KeyEventArgs e) {
        if (int.TryParse(SpeedEntry.Text, out var v) && v is >= 1 and <= 500)
            SpeedSlider.Value = v;
    }
    void ResetSpeed(object? s, RoutedEventArgs e) { SpeedSlider.Value = 100; SpeedEntry.Text = "100"; }

    void SetSpeed(double pct) {
        pct = Math.Max(1, Math.Min(500, pct));
        SpeedSlider.Value = pct;
        SpeedEntry.Text = ((int)pct).ToString();
    }

    // --- seek ---
    static string FormatTime(double seconds) {
        if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
        return TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss");
    }

    // laeuft ca. 10x/sekunde per engine.OnProgress mit
    void UpdateSeekProgress(double pos, double dur) {
        bool loaded = dur > 0;
        SeekSlider.IsEnabled = loaded;
        SeekBackBtn.IsEnabled = loaded;
        SeekFwdBtn.IsEnabled = loaded;

        updatingSeek = true;
        SeekSlider.Maximum = loaded ? dur : 0;
        if (!draggingSeek) SeekSlider.Value = loaded ? pos : 0;
        updatingSeek = false;

        SeekTotalTime.Text = FormatTime(dur);
        if (!draggingSeek) SeekCurrentTime.Text = FormatTime(pos);
    }

    // reagiert auf jeden zwischenwert (auch waehrend des ziehens) - nur anzeige,
    // Seek() selbst passiert erst bei PointerReleased
    void SeekSliderChanged(double raw) {
        if (updatingSeek) return;
        SeekCurrentTime.Text = FormatTime(raw);
    }

    void SeekBack(object? s, RoutedEventArgs e) => engine.Seek(engine.Position - 10);
    void SeekForward(object? s, RoutedEventArgs e) => engine.Seek(engine.Position + 10);

    // --- transport ---
    public void Play() {
        var f = Config.Data.midiPlayer.currentFile;
        if (string.IsNullOrEmpty(f) || !File.Exists(f)) { Log("keine datei gewählt"); return; }
        engine.SetSpeedPercent(Dispatcher.UIThread.Invoke(() => SpeedSlider.Value));
        engine.Start(f);
        Dispatcher.UIThread.Post(() => {
            PlayButton.IsEnabled = false;
            StopButton.IsEnabled = true;
        });
    }
    void OnPlay(object? s, RoutedEventArgs e) => Play();
    void OnStop(object? s, RoutedEventArgs e) => engine.Stop();

    public void PauseHotkey() => engine.TogglePause();
    public void StopHotkey() => engine.Stop();
    public void SpeedHotkey(bool up) {
        const double step = 5;
        Dispatcher.UIThread.Post(() => SetSpeed(SpeedSlider.Value + (up ? step : -step)));
    }

    void ResetPlayState() {
        PlayButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        SeekSlider.IsEnabled = false;
        SeekBackBtn.IsEnabled = false;
        SeekFwdBtn.IsEnabled = false;
        updatingSeek = true;
        SeekSlider.Value = 0;
        updatingSeek = false;
        SeekCurrentTime.Text = "0:00:00";
        SeekTotalTime.Text = "0:00:00";
    }
}
