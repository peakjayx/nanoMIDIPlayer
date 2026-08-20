using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using nanoMIDIPlayer.Core;
using nanoMIDIPlayer.Player;

namespace nanoMIDIPlayer.UI;

public partial class SettingsView : UserControl {
    readonly PlaybackEngine engine;
    bool init = true;

    public SettingsView(PlaybackEngine engine) {
        InitializeComponent();
        this.engine = engine;
        WireEvents();
        LoadValues();
        init = false;
    }

    void WireEvents() {
        foreach (var cb in new[] { Sustain, NoDoubles, Velocity, Keys88, LoopSong,
                                   ReleaseOnPause, CustomHold, Topmost, Timestamp })
            cb.IsCheckedChanged += Save;
        RandomFail.IsCheckedChanged += SaveRandomFail;

        OnValue(NoteLengthSlider, NoteLenSlider);
        OnValue(SpeedFailSlider, SpeedFailSliderChg);
        OnValue(TransFailSlider, TransFailSliderChg);
    }

    static void OnValue(Slider s, Action<double> cb) =>
        s.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) cb(s.Value); };

    void LoadValues() {
        var p = Config.Data.midiPlayer;
        Sustain.IsChecked = p.sustain;
        NoDoubles.IsChecked = p.noDoubles;
        Velocity.IsChecked = p.velocity;
        Keys88.IsChecked = p.keys88;
        LoopSong.IsChecked = p.loopSong;
        ReleaseOnPause.IsChecked = p.releaseOnPause;
        CustomHold.IsChecked = p.customHoldLength.enabled;
        NoteLengthSlider.Value = Math.Min(10, p.customHoldLength.noteLength * 10);
        NoteLengthEntry.Text = p.customHoldLength.noteLength.ToString(CultureInfo.InvariantCulture);
        RandomFail.IsChecked = p.randomFail.enabled;
        SpeedFailSlider.Value = p.randomFail.speed;
        SpeedFailEntry.Text = ((int)p.randomFail.speed).ToString();
        TransFailSlider.Value = p.randomFail.transpose;
        TransFailEntry.Text = ((int)p.randomFail.transpose).ToString();

        SendMode.ItemsSource = new[] { "scancode", "virtualkey", "unicode" };
        SendMode.SelectedItem = p.sendMode;
        if (SendMode.SelectedItem == null) SendMode.SelectedIndex = 0;
        UpdateSendModeHint();

        Topmost.IsChecked = Config.Data.appUI.topmost;
        Timestamp.IsChecked = Config.Data.appUI.timestamp;
    }

    // auf macOS gibt es keine scancode/virtualkey-trennung
    void UpdateSendModeHint() =>
        SendModeHint.Text = OperatingSystem.IsMacOS()
            ? "macOS: scancode und virtualkey sind identisch (CGEvent kennt nur "
              + "virtual keycodes). unicode tippt zeichen direkt."
            : "";

    static double PD(string? s, double def) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : def;

    // sammel-save fuer alle switches
    void Save(object? s = null, RoutedEventArgs? e = null) {
        if (init) return;
        var p = Config.Data.midiPlayer;
        p.sustain = Sustain.IsChecked == true;
        p.noDoubles = NoDoubles.IsChecked == true;
        p.velocity = Velocity.IsChecked == true;
        p.keys88 = Keys88.IsChecked == true;
        p.loopSong = LoopSong.IsChecked == true;
        p.releaseOnPause = ReleaseOnPause.IsChecked == true;
        p.customHoldLength.enabled = CustomHold.IsChecked == true;
        Config.Data.appUI.topmost = Topmost.IsChecked == true;
        Config.Data.appUI.timestamp = Timestamp.IsChecked == true;
        if (TopLevel.GetTopLevel(this) is Window w) w.Topmost = Config.Data.appUI.topmost;
        Config.Save();
        Hint();
    }

    void SaveRandomFail(object? s, RoutedEventArgs e) {
        if (init) return;
        Config.Data.midiPlayer.randomFail.enabled = RandomFail.IsChecked == true;
        Config.Save();
        Hint();
    }

    void Hint() { if (!init) SaveHint.Text = "gespeichert ✓"; }

    void NoteLenSlider(double raw) {
        if (init) return;
        double v = Math.Round(raw / 10.0, 2);
        NoteLengthEntry.Text = v.ToString(CultureInfo.InvariantCulture);
        Config.Data.midiPlayer.customHoldLength.noteLength = v;
        Config.Save();
    }
    void NoteLenEntry(object? s, KeyEventArgs e) {
        if (init) return;
        double v = PD(NoteLengthEntry.Text, 0.1);
        Config.Data.midiPlayer.customHoldLength.noteLength = v;
        NoteLengthSlider.Value = Math.Min(10, v * 10);
        Config.Save();
    }

    void SpeedFailSliderChg(double raw) {
        if (init) return;
        Config.Data.midiPlayer.randomFail.speed = Math.Round(raw);
        SpeedFailEntry.Text = ((int)raw).ToString();
        Config.Save();
    }
    void SpeedFailEntryChg(object? s, KeyEventArgs e) {
        if (init) return;
        double v = PD(SpeedFailEntry.Text, 5);
        Config.Data.midiPlayer.randomFail.speed = v;
        SpeedFailSlider.Value = Math.Min(100, v);
        Config.Save();
    }
    void TransFailSliderChg(double raw) {
        if (init) return;
        Config.Data.midiPlayer.randomFail.transpose = Math.Round(raw);
        TransFailEntry.Text = ((int)raw).ToString();
        Config.Save();
    }
    void TransFailEntryChg(object? s, KeyEventArgs e) {
        if (init) return;
        double v = PD(TransFailEntry.Text, 5);
        Config.Data.midiPlayer.randomFail.transpose = v;
        TransFailSlider.Value = Math.Min(100, v);
        Config.Save();
    }

    void SendModeChanged(object? s, SelectionChangedEventArgs e) {
        if (init || SendMode.SelectedItem is not string m) return;
        Config.Data.midiPlayer.sendMode = m;
        Config.Save();
    }

    void ClearMidiList(object? s, RoutedEventArgs e) {
        Config.Data.midiPlayer.midiList.Clear();
        Config.Data.midiPlayer.currentFile = "";
        Config.Save();
        Hint();
    }
}
