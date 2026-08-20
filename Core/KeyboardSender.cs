using nanoMIDIPlayer.Core.Platform;

namespace nanoMIDIPlayer.Core;

// plattformneutrale tastatur-simulation.
// haelt den zustand der gedrueckten tasten, das eigentliche senden
// macht das backend fuer das jeweilige OS.
public class KeyboardSender {
    public string Mode = "scancode"; // scancode | virtualkey | unicode
    readonly HashSet<string> pressed = new();
    readonly object gate = new();
    readonly IKeyBackend backend = PlatformFactory.Keyboard();

    public string? Diagnose() => backend.Diagnose();

    public void Press(string key) {
        lock (gate) { pressed.Add(key); backend.Send(key, false, Mode); }
    }

    public void Release(string key) {
        lock (gate) { pressed.Remove(key); backend.Send(key, true, Mode); }
    }

    public IReadOnlyCollection<string> Held { get { lock (gate) return pressed.ToArray(); } }

    public void ReleaseAll() {
        lock (gate) {
            foreach (var k in pressed.ToArray()) backend.Send(k, true, Mode);
            pressed.Clear();
        }
    }
}
