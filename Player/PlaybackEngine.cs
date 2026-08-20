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
    public event Action<double, double>? OnProgress; // (position, duration), ~10x/sekunde

    Thread? playThread, clockThread;
    volatile bool stop, paused;
    double playbackSpeed = 1.0;
    int heldNoteCount;
    bool sustainActive;
    readonly Random rng = new();
    readonly List<Timer> timers = new();
    readonly Dictionary<int, List<int>> activeTransposed = new();

    // seek/position: absolute zeitachse (aufgebaut beim laden, mid.Events.Time ist nur delta)
    double[] absTimes = Array.Empty<double>();
    double duration;
    long posBits; // position als bits, interlocked statt lock im hot pfad (double selbst ist nicht volatile-faehig)

    // seek-anfrage vom ui-thread. gleiches muster wie stop/paused: erst ziel setzen, dann flag,
    // playback-thread wertet das flag in der schleife aus (kein lock im hot pfad noetig)
    volatile bool seekPending;
    double seekTarget;

    static readonly Regex ShiftSym = new("[!@$%^*(]", RegexOptions.Compiled);
    static readonly Dictionary<char, string> YzSwap = new() {
        { 'y', "z" }, { 'z', "y" }, { 'Y', "Z" }, { 'Z', "Y" }
    };

    public bool IsRunning => playThread is { IsAlive: true };
    public double Speed => playbackSpeed;
    public double Duration => duration;
    public double Position => BitConverter.Int64BitsToDouble(Interlocked.Read(ref posBits));

    void SetPosition(double v) => Interlocked.Exchange(ref posBits, BitConverter.DoubleToInt64Bits(v));

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
        stop = false; paused = false; seekPending = false;
        kb.Mode = P.sendMode;
        heldNoteCount = 0; sustainActive = false;
        activeTransposed.Clear();

        MidiFile mid;
        try { mid = new MidiFile(midiFile); }
        catch (Exception e) { Log($"load fehler: {e.Message}"); return; }

        // absolute zeitachse einmalig aufbauen statt bei jedem seek neu zu summieren
        absTimes = new double[mid.Events.Count];
        double acc = 0;
        for (int i = 0; i < mid.Events.Count; i++) { acc += mid.Events[i].Time; absTimes[i] = acc; }
        duration = mid.Length;
        SetPosition(0);

        clockThread = new Thread(ClockLoop) { IsBackground = true };
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

    // absolut springen (sekunden), wird auf [0, Duration] geklemmt.
    // ist nichts geladen (duration <= 0) wird der aufruf ignoriert -- es gibt nichts zum abspielen.
    // wird waehrend eines laufenden Start() nichts konsumiert (kein thread liest das flag), ist also
    // faktisch ein no-op falls direkt nach Stop() aufgerufen; ein nachfolgendes Start() beginnt immer bei 0.
    public void Seek(double seconds) {
        if (duration <= 0) return;
        seekTarget = Math.Clamp(seconds, 0, duration);
        seekPending = true;
    }

    public void Stop() {
        if (stop) return;
        stop = true;
        seekPending = false;
        heldNoteCount = 0;
        foreach (var t in timers.ToArray()) t.Dispose();
        timers.Clear();
        kb.ReleaseAll();
        sustainActive = false;
        activeTransposed.Clear();
        playThread?.Join(1000);
        clockThread?.Join(1000);
        SetPosition(0);
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

    // randomFail-transpose (falls aktiv) + dispatch. aus dem haupt-event-pfad und dem
    // chord-spread-pfad gleichermassen genutzt, damit sich beide identisch verhalten.
    void DispatchEvent(MidiEvent msg) {
        if (msg.Type == "note_on" && msg.Velocity > 0 && P.randomFail.enabled
            && rng.NextDouble() < P.randomFail.transpose / 100.0) {
            int newNote = msg.Note + rng.Next(-12, 13);
            if (!activeTransposed.ContainsKey(msg.Note)) activeTransposed[msg.Note] = new();
            activeTransposed[msg.Note].Add(newNote);
            ParseMidi(new MidiEvent { Type = msg.Type, Note = newNote, Velocity = msg.Velocity });
            return;
        }
        if ((msg.Type == "note_off" || (msg.Type == "note_on" && msg.Velocity == 0))
            && activeTransposed.TryGetValue(msg.Note, out var list) && list.Count > 0) {
            int tn = list[0]; list.RemoveAt(0);
            if (list.Count == 0) activeTransposed.Remove(msg.Note);
            ParseMidi(new MidiEvent { Type = msg.Type, Note = tn, Velocity = msg.Velocity });
            return;
        }
        ParseMidi(msg);
    }

    // --- chord-erkennung ---
    static bool IsNoteEvent(MidiEvent e) => e.Type is "note_on" or "note_off";
    static bool IsRelease(MidiEvent e) => e.Type == "note_off" || (e.Type == "note_on" && e.Velocity == 0);

    // sammelt ab index i alle direkt folgenden events derselben art (anschlag ODER loslassen),
    // deren kumulierte delta-zeit innerhalb von detectWindowMs liegt -> das ist ein akkord.
    List<int> CollectChordGroup(MidiFile mid, int i) {
        var list = new List<int> { i };
        bool isRel = IsRelease(mid.Events[i]);
        double accMs = 0;
        int j = i + 1;
        while (j < mid.Events.Count) {
            var e = mid.Events[j];
            if (!IsNoteEvent(e)) break;
            accMs += e.Time * 1000.0;
            if (accMs > P.chordOffset.detectWindowMs) break;
            if (IsRelease(e) != isRel) break;
            list.Add(j);
            j++;
        }
        return list;
    }

    void Shuffle(List<int> list) {
        for (int n = list.Count - 1; n > 0; n--) {
            int k = rng.Next(n + 1);
            (list[n], list[k]) = (list[k], list[n]);
        }
    }

    // wartet bis sw.Elapsed >= target (bei pause verlaengert sich target automatisch).
    // aktualisiert nebenbei Position (interpoliert zwischen prevNominal und targetNominal).
    // true = ziel erreicht, false = abbruch durch stop/seek (aufrufer prueft welches von beiden).
    bool WaitUntil(Stopwatch sw, ref double target, ref bool wasPaused, double realStart, double prevNominal, double targetNominal) {
        while (sw.Elapsed.TotalSeconds < target) {
            if (stop) return false;
            if (seekPending) return false;
            if (paused && !wasPaused) {
                wasPaused = true;
                if (P.releaseOnPause) {
                    kb.ReleaseAll();
                    if (sustainActive) { sustainActive = false; }
                }
            }
            if (!paused && wasPaused) wasPaused = false;
            while (paused && !stop && !seekPending) {
                double before = sw.Elapsed.TotalSeconds;
                Thread.Sleep(50);
                target += sw.Elapsed.TotalSeconds - before; // pausenzeit ausgleichen
            }
            if (stop) return false;
            if (seekPending) return false;
            double span = target - realStart;
            double frac = span > 0 ? Math.Clamp((sw.Elapsed.TotalSeconds - realStart) / span, 0, 1) : 1;
            SetPosition(prevNominal + (targetNominal - prevNominal) * frac);
            double remaining = target - sw.Elapsed.TotalSeconds;
            if (remaining > 0) Thread.Sleep((int)Math.Min(remaining * 1000, 5));
        }
        SetPosition(targetNominal);
        return true;
    }

    // erstes event, dessen absolute zeit >= t ist (binaersuche ueber die beim laden gebaute zeitachse)
    int FindIndexForTime(double t) {
        int lo = 0, hi = absTimes.Length;
        while (lo < hi) {
            int mid2 = (lo + hi) / 2;
            if (absTimes[mid2] < t) lo = mid2 + 1; else hi = mid2;
        }
        return lo;
    }

    // --- main loops ---
    enum RunResult { Finished, Stopped, SeekRequested }

    void PlayLoop(MidiFile mid) {
        Log("nanoMIDI Mid2VK Translator v3.0 (C#)");
        int startIndex = 0;
        double startPos = 0;

        while (!stop) {
            var result = PlayFrom(mid, startIndex, startPos);
            if (stop) break;

            if (result == RunResult.SeekRequested) {
                double target = seekTarget;
                seekPending = false;
                // alle gehaltenen tasten sofort loslassen -- sonst haengt eine taste fest
                kb.ReleaseAll();
                sustainActive = false;
                heldNoteCount = 0;
                foreach (var t in timers.ToArray()) t.Dispose();
                timers.Clear();
                activeTransposed.Clear();
                startIndex = FindIndexForTime(target);
                startPos = target;
                continue;
            }

            // result == Finished (natuerliches ende dieses durchlaufs)
            if (!P.loopSong) break;
            kb.ReleaseAll();
            startIndex = 0;
            startPos = 0;
        }
        if (!P.loopSong && !stop) {
            // natuerliches ende
            stop = true;
            kb.ReleaseAll();
            OnStopped?.Invoke();
        }
    }

    RunResult PlayFrom(MidiFile mid, int startIndex, double startPos) {
        var sw = Stopwatch.StartNew();
        double currentTime = 0;
        bool wasPaused = false;
        SetPosition(startPos);

        // bei enabled==false oder spreadMs<=0 bleibt das verhalten exakt wie vorher (default-pfad,
        // ausser einem billigen bool-check gibt es keinen zusaetzlichen overhead)
        bool chordOn = P.chordOffset.enabled && P.chordOffset.spreadMs > 0;

        RunResult Aborted() => stop ? RunResult.Stopped : RunResult.SeekRequested;

        for (int i = startIndex; i < mid.Events.Count; i++) {
            if (stop) return RunResult.Stopped;
            if (seekPending) return RunResult.SeekRequested;
            var msg = mid.Events[i];

            // beim (wieder-)einstieg (i==startIndex, z.b. nach seek) nur der rest-abstand bis zum
            // event, sonst das normale delta aus der datei
            double rawDelay = (i == startIndex) ? Math.Max(0, absTimes[i] - startPos) : msg.Time;
            double adjustedDelay = rawDelay / playbackSpeed;
            if (P.randomFail.enabled && !msg.IsMeta) {
                if (rng.NextDouble() < P.randomFail.speed / 100.0)
                    adjustedDelay *= 0.5 + rng.NextDouble();
            }
            currentTime += adjustedDelay;

            double prevNominal = (i == startIndex) ? startPos : absTimes[i - 1];
            double targetNominal = absTimes[i];
            double realStart = currentTime - adjustedDelay;
            if (!WaitUntil(sw, ref currentTime, ref wasPaused, realStart, prevNominal, targetNominal))
                return Aborted();

            if (msg.IsMeta) continue;
            if (paused) continue;

            if (chordOn && IsNoteEvent(msg)) {
                bool isRel = IsRelease(msg);
                if (!isRel || P.chordOffset.applyToRelease) {
                    var group = CollectChordGroup(mid, i);
                    if (group.Count > 1) {
                        int last = group[^1];
                        int nextIdx = last + 1;
                        // real-zeit-budget bis zum naechsten event danach -- das darf der akkord nicht ueberschreiten
                        double realBudgetMs = nextIdx < mid.Events.Count
                            ? (absTimes[nextIdx] - absTimes[last]) * 1000.0 / playbackSpeed
                            : double.PositiveInfinity;
                        // spreadMs ist echtzeit gemeint und wird NICHT durch playbackSpeed geteilt --
                        // ein menschlicher anschlag wird nicht schneller, nur weil das stueck schneller laeuft.
                        // wir kuerzen ihn aber, statt das stueck zu verschleppen (s.o. realBudgetMs).
                        double spread = Math.Max(0, Math.Min(P.chordOffset.spreadMs, realBudgetMs / (group.Count - 1)));

                        var order = new List<int>(group);
                        if (P.chordOffset.randomOrder) Shuffle(order);
                        else order.Sort((a, b) => mid.Events[a].Note.CompareTo(mid.Events[b].Note));

                        double groupStartReal = currentTime;
                        for (int k = 0; k < order.Count; k++) {
                            double baseFireAt = groupStartReal + k * spread / 1000.0;
                            double fireAt = baseFireAt;
                            double nom = absTimes[order[k]];
                            if (!WaitUntil(sw, ref fireAt, ref wasPaused, groupStartReal, nom, nom))
                                return Aborted();
                            groupStartReal += fireAt - baseFireAt; // pausenverlaengerung uebernehmen
                            DispatchEvent(mid.Events[order[k]]);
                        }

                        // naechstes event nach dem akkord bleibt bei seiner regulaeren zeit -- kein wegdriften
                        currentTime = groupStartReal;
                        for (int idx = i + 1; idx <= last; idx++) currentTime += mid.Events[idx].Time / playbackSpeed;
                        SetPosition(absTimes[last]);
                        i = last;
                        continue;
                    }
                }
            }

            DispatchEvent(msg);
        }
        return RunResult.Finished;
    }

    static string FormatTime(double seconds) {
        int h = (int)(seconds / 3600);
        int m = (int)(seconds % 3600 / 60);
        int s = (int)(seconds % 60);
        return $"{h}:{m:00}:{s:00}";
    }

    // meldet die tatsaechliche wiedergabeposition (statt sie wie frueher blind hochzuzaehlen) --
    // sonst springt die seek-bar beim spulen zurueck
    void ClockLoop() {
        int tick = 0;
        while (!stop) {
            double pos = Position, dur = duration;
            OnProgress?.Invoke(pos, dur);
            if (tick % 10 == 0 && Config.Data.appUI.timestamp)
                OnTime?.Invoke($"{FormatTime(pos)} / {FormatTime(dur)}");
            tick++;
            Thread.Sleep(100);
        }
    }
}
