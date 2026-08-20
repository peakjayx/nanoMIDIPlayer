namespace nanoMIDIPlayer.Core.Platform;

// plattform-abstraktion fuer tastatur-injection
public interface IKeyBackend {
    // mode: scancode | virtualkey | unicode
    void Send(string key, bool up, string mode);
    // freitext-hinweis fuer die konsole (z.b. fehlende mac-berechtigung), null = alles ok
    string? Diagnose();
}

// plattform-abstraktion fuer globale hotkeys
public interface IHotkeyBackend : IDisposable {
    void Register(string key, Action onPress);
    void Start();
}
