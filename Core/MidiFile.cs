using System.IO;

namespace nanoMIDIPlayer.Core;

// ein midi event, time = delta sekunden
public class MidiEvent {
    public double Time;        // delta zum vorherigen event
    public string Type = "";   // note_on | note_off | control_change | meta
    public int Note;
    public int Velocity;
    public int Control;
    public int Value;
    public bool IsMeta;
}

// minimaler standard-midi-file parser
public class MidiFile {
    public List<MidiEvent> Events { get; } = new();
    public double Length { get; private set; }

    public MidiFile(string path) {
        var bytes = File.ReadAllBytes(path);
        Parse(bytes);
    }

    class Raw { public long Tick; public int Order; public MidiEvent Ev = new(); public int Tempo = -1; }

    void Parse(byte[] b) {
        int p = 0;
        if (b.Length < 14 || b[0] != 'M' || b[1] != 'T' || b[2] != 'h' || b[3] != 'd')
            throw new InvalidDataException("kein midi file");
        p = 4;
        int headLen = ReadInt32(b, ref p);
        int format = ReadInt16(b, ref p);
        int ntracks = ReadInt16(b, ref p);
        int division = ReadInt16(b, ref p);
        p = 8 + headLen;

        int ticksPerBeat = division > 0 ? division : 480;

        var raws = new List<Raw>();
        int order = 0;

        for (int t = 0; t < ntracks && p + 8 <= b.Length; t++) {
            if (b[p] != 'M' || b[p + 1] != 'T' || b[p + 2] != 'r' || b[p + 3] != 'k') break;
            p += 4;
            int trkLen = ReadInt32(b, ref p);
            int end = p + trkLen;
            long absTick = 0;
            int status = 0;

            while (p < end) {
                absTick += ReadVarLen(b, ref p);
                int cur = b[p];

                if (cur == 0xFF) { // meta
                    p++;
                    int metaType = b[p++];
                    int len = ReadVarLen(b, ref p);
                    if (metaType == 0x51 && len == 3) { // set_tempo
                        int tempo = (b[p] << 16) | (b[p + 1] << 8) | b[p + 2];
                        raws.Add(new Raw { Tick = absTick, Order = order++, Tempo = tempo,
                            Ev = new MidiEvent { Type = "meta", IsMeta = true } });
                    }
                    p += len;
                    continue;
                }
                if (cur == 0xF0 || cur == 0xF7) { // sysex skip
                    p++;
                    int len = ReadVarLen(b, ref p);
                    p += len;
                    continue;
                }

                int data1, data2;
                if (cur >= 0x80) { status = cur; p++; }
                // running status verwendet altes status byte
                int type = status & 0xF0;

                if (type == 0xC0 || type == 0xD0) { // program/aftertouch: 1 datenbyte
                    data1 = b[p++];
                    continue;
                }

                data1 = b[p++];
                data2 = b[p++];

                if (type == 0x90) { // note_on
                    raws.Add(new Raw { Tick = absTick, Order = order++,
                        Ev = new MidiEvent {
                            Type = data2 == 0 ? "note_off" : "note_on",
                            Note = data1, Velocity = data2 } });
                } else if (type == 0x80) { // note_off
                    raws.Add(new Raw { Tick = absTick, Order = order++,
                        Ev = new MidiEvent { Type = "note_off", Note = data1, Velocity = data2 } });
                } else if (type == 0xB0) { // control_change
                    raws.Add(new Raw { Tick = absTick, Order = order++,
                        Ev = new MidiEvent { Type = "control_change", Control = data1, Value = data2 } });
                }
            }
            p = end;
        }

        // nach absolutem tick sortieren (stabil)
        var sorted = raws.OrderBy(r => r.Tick).ThenBy(r => r.Order).ToList();

        long lastTick = 0;
        int curTempo = 500000; // default 120 bpm
        double total = 0;

        foreach (var r in sorted) {
            long dTicks = r.Tick - lastTick;
            double dSec = dTicks * (curTempo / 1_000_000.0) / ticksPerBeat;
            r.Ev.Time = dSec;
            total += dSec;
            lastTick = r.Tick;
            if (r.Tempo > 0) curTempo = r.Tempo; // gilt ab jetzt
            Events.Add(r.Ev);
        }
        Length = total;
    }

    static int ReadInt32(byte[] b, ref int p) {
        int v = (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
        p += 4; return v;
    }
    static int ReadInt16(byte[] b, ref int p) {
        int v = (b[p] << 8) | b[p + 1];
        p += 2; return v;
    }
    static int ReadVarLen(byte[] b, ref int p) {
        int v = 0, c;
        do { c = b[p++]; v = (v << 7) | (c & 0x7F); } while ((c & 0x80) != 0);
        return v;
    }
}
