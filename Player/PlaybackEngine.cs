using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using nanoMIDIPlayer.Core;

namespace nanoMIDIPlayer.Player;

// midi -> tastatur translator (port midiWindows.py)
public class PlaybackEngine {
    readonly KeyboardSender kb = new();
    public event Action<string>? OnLog;
    public event Action<string>? OnTime;
    public event Action? OnStopped;

    Thread? playThread, clockThread;
    volatile bool stop, paused;
    double playbackSpeed = 1.0;
    int heldNoteCount;
    bool sustainActive;
    readonly Random rng = new();
    readonly List<Timer> timers = new();
    readonly Dictionary<int, List<int>> activeTransposed = new();

    static readonly Regex ShiftSym = new("[!@$%^*(]", RegexOptions.Compiled);
    static readonly Dictionary<char, string> YzSwap = new() {
        { 'y', "z" }, { 'z', "y" }, { 'Y', "Z" }, { 'Z', "Y" }
    };

    public bool IsRunning => playThread is { IsAlive: true };
    public double Speed => playbackSpeed;

    // plattform-hinweis (z.b. fehlendes mac-recht), null wenn alles passt
    public string? Diagnose() => kb.Diagnose();

    void Log(string m) => OnLog?.Invoke(m);
    static PlayerConfig P => Config.Data.midiPlayer;

    string SwapYz(string key) {
        if (P.swapYZ && key.Length == 1 && YzSwap.TryGetValue(key[0], out var s)) return s;
        return key;
    }

    // --- playback control ---
    public void Start(string midiFile) {
        if (IsRunning) return;
        stop = false; paused = false;
        kb.Mode = P.sendMode;
        heldNoteCount = 0; sustainActive = false;

        MidiFile mid;
        try { mid = new MidiFile(midiFile); }
        catch (Exception e) { Log($"load fehler: {e.Message}"); return; }

        double total = mid.Length;
        clockThread = new Thread(() => ClockLoop(total)) { IsBackground = true };
        playThread = new Thread(() => PlayLoop(mid)) { IsBackground = true };
        clockThread.Start();
        playThread.Start();
    }

    public void TogglePause() {
        paused = !paused;
        if (paused && P.releaseOnPause) {
            kb.ReleaseAll();
            if (sustainActive) { sustainActive = false; }
        }
        Log(paused ? "Playback paused." : "Playback resumed.");
    }

    public void ChangeSpeed(double amount) {
        playbackSpeed = Math.Max(0.1, Math.Min(5.0, playbackSpeed + amount));
        Log($"Speed: {playbackSpeed * 100:0}%");
    }

    // absolute speed in prozent (1-500)
    public void SetSpeedPercent(double pct) {
        playbackSpeed = Math.Max(0.01, Math.Min(5.0, pct / 100.0));
    }
    public double SpeedPercent => playbackSpeed * 100.0;

    public void Stop() {
        if (stop) return;
        stop = true;
        heldNoteCount = 0;
        foreach (var t in timers.ToArray()) t.Dispose();
        timers.Clear();
        kb.ReleaseAll();
        sustainActive = false;
        playThread?.Join(1000);
        clockThread?.Join(1000);
        Log("Playback fully stopped.");
        OnStopped?.Invoke();
    }

    // --- key simulation ---
    int FingerLimit() => P.fingerLimit;

    string FindVelocityKey(int velocity) {
        var vm = P.pianoMap.velocityMap;
        var thresholds = vm.Keys.Select(int.Parse).OrderBy(x => x).ToList();
        int min = 0, max = thresholds.Count - 1, idx = 0;
        while (min <= max) {
            idx = (min + max) / 2;
            if (idx == 0 || idx == thresholds.Count - 1) break;
            if (thresholds[idx] < velocity) min = idx + 1;
            else max = idx - 1;
        }
        return vm[thresholds[idx].ToString()];
    }

    void PressMaybeRelease(string key) {
        int limit = FingerLimit();
        if (limit <= 10 && heldNoteCount >= limit) {
            Log($"Finger limit ({limit}) reached, skipping note");
            return;
        }
        heldNoteCount++;
        kb.Press(key);
        if (P.customHoldLength.enabled) {
            var t = new Timer(_ => {
                kb.Release(key);
                heldNoteCount = Math.Max(0, heldNoteCount - 1);
            }, null, (int)(P.customHoldLength.noteLength * 1000), Timeout.Infinite);
            timers.Add(t);
        }
    }

    void SimulateKey(string msgType, int note, int velocity) {
        note += P.pitchOffset + P.transposeOffset;
        bool allow88 = P.keys88;
        var map61 = P.pianoMap.keyMap61;
        var low = P.pianoMap.keyMap88.lowNotes;
        var high = P.pianoMap.keyMap88.highNotes;
        string ns = note.ToString();

        string key;
        if (map61.TryGetValue(ns, out var k61)) key = k61;
        else if (allow88 && low.TryGetValue(ns, out var kl)) key = kl;
        else if (allow88 && high.TryGetValue(ns, out var kh)) key = kh;
        else { Log($"out of range: {note}"); return; }

        key = SwapYz(key);

        if (msgType == "note_on") {
            if (P.velocity) {
                string velKey = SwapYz(FindVelocityKey(velocity));
                kb.Press("alt"); kb.Press(velKey); kb.Release(velKey); kb.Release("alt");
            }
            if (note >= 36 && note <= 96) {
                bool sym = ShiftSym.IsMatch(key);
                if (P.noDoubles) {
                    if (sym) kb.Release(map61[(note - 1).ToString()]);
                    else kb.Release(key.ToLowerInvariant());
                }
                if (sym) {
                    kb.Press("shift");
                    PressMaybeRelease(map61[(note - 1).ToString()]);
                    kb.Release("shift");
                } else if (key.Any(char.IsUpper)) {
                    kb.Press("shift");
                    PressMaybeRelease(key.ToLowerInvariant());
                    kb.Release("shift");
                } else {
                    PressMaybeRelease(key);
                }
            } else {
                kb.Release(key.ToLowerInvariant());
                kb.Press("ctrl");
                PressMaybeRelease(key.ToLowerInvariant());
                kb.Release("ctrl");
            }
        } else if (msgType == "note_off") {
            if (note >= 36 && note <= 96) {
                if (ShiftSym.IsMatch(key)) kb.Release(map61[(note - 1).ToString()]);
                else kb.Release(key.ToLowerInvariant());
            } else {
                kb.Release(key.ToLowerInvariant());
            }
            heldNoteCount = Math.Max(0, heldNoteCount - 1);
        }
    }

    void ParseMidi(MidiEvent m) {
        if (m.Type == "control_change" && P.sustain && m.Control == 64) {
            if (!sustainActive && m.Value > P.sustainCutoff) {
                sustainActive = true; kb.Press("space");
            } else if (sustainActive && m.Value < P.sustainCutoff) {
                sustainActive = false; kb.Release("space");
            }
        } else if (m.Type is "note_on" or "note_off") {
            if (m.Velocity == 0) SimulateKey("note_off", m.Note, m.Velocity);
            else SimulateKey(m.Type, m.Note, m.Velocity);
        }
    }

    // --- main loops ---
    void PlayLoop(MidiFile mid) {
        Log("nanoMIDI Mid2VK Translator v3.0 (C#)");
        while (!stop) {
            bool finished = PlayOnce(mid);
            if (!P.loopSong || !finished || stop) break;
            kb.ReleaseAll();
        }
        if (!P.loopSong && !stop) {
            // natuerliches ende
            stop = true;
            kb.ReleaseAll();
            OnStopped?.Invoke();
        }
    }

    bool PlayOnce(MidiFile mid) {
        var sw = Stopwatch.StartNew();
        double currentTime = 0;
        bool wasPaused = false;

        foreach (var msg in mid.Events) {
            if (stop) return false;

            double adjustedDelay = msg.Time / playbackSpeed;
            if (P.randomFail.enabled && !msg.IsMeta) {
                if (rng.NextDouble() < P.randomFail.speed / 100.0)
                    adjustedDelay *= 0.5 + rng.NextDouble();
            }
            currentTime += adjustedDelay;

            while (sw.Elapsed.TotalSeconds < currentTime) {
                if (stop) return false;
                if (paused && !wasPaused) {
                    wasPaused = true;
                    if (P.releaseOnPause) {
                        kb.ReleaseAll();
                        if (sustainActive) { sustainActive = false; }
                    }
                }
                if (!paused && wasPaused) wasPaused = false;
                while (paused && !stop) {
                    double before = sw.Elapsed.TotalSeconds;
                    Thread.Sleep(50);
                    currentTime += sw.Elapsed.TotalSeconds - before; // pausenzeit ausgleichen
                }
                double remaining = currentTime - sw.Elapsed.TotalSeconds;
                if (remaining > 0) Thread.Sleep((int)Math.Min(remaining * 1000, 5));
            }

            if (msg.IsMeta) continue;
            if (paused) continue;

            // randomFail transpose
            if (msg.Type == "note_on" && msg.Velocity > 0 && P.randomFail.enabled
                && rng.NextDouble() < P.randomFail.transpose / 100.0) {
                int newNote = msg.Note + rng.Next(-12, 13);
                if (!activeTransposed.ContainsKey(msg.Note)) activeTransposed[msg.Note] = new();
                activeTransposed[msg.Note].Add(newNote);
                ParseMidi(new MidiEvent { Type = msg.Type, Note = newNote, Velocity = msg.Velocity });
                continue;
            }
            if ((msg.Type == "note_off" || (msg.Type == "note_on" && msg.Velocity == 0))
                && activeTransposed.TryGetValue(msg.Note, out var list) && list.Count > 0) {
                int tn = list[0]; list.RemoveAt(0);
                if (list.Count == 0) activeTransposed.Remove(msg.Note);
                ParseMidi(new MidiEvent { Type = msg.Type, Note = tn, Velocity = msg.Velocity });
                continue;
            }
            ParseMidi(msg);
        }
        return true;
    }

    static string FormatTime(double seconds) {
        int h = (int)(seconds / 3600);
        int m = (int)(seconds % 3600 / 60);
        int s = (int)(seconds % 60);
        return $"{h}:{m:00}:{s:00}";
    }

    void ClockLoop(double total) {
        double cur = 0;
        while (!stop) {
            if (!paused) {
                double shown = total > 0 ? cur % total : cur;
                if (Config.Data.appUI.timestamp)
                    OnTime?.Invoke($"{FormatTime(shown)} / {FormatTime(total)}");
                cur += 1;
                for (int i = 0; i < 10; i++) {
                    if (stop) break;
                    Thread.Sleep((int)(100 / playbackSpeed));
                }
            } else {
                Thread.Sleep(100);
            }
        }
    }
}
