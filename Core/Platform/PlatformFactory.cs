namespace nanoMIDIPlayer.Core.Platform;

// waehlt das backend fuer das laufende OS
public static class PlatformFactory {
    public static bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static IKeyBackend Keyboard() {
        if (OperatingSystem.IsWindows()) return new WindowsKeyboard();
        if (OperatingSystem.IsMacOS()) return new MacKeyboard();
        return new NullKeyBackend();
    }

    public static IHotkeyBackend Hotkeys() {
        if (OperatingSystem.IsWindows()) return new WindowsHotkeys();
        if (OperatingSystem.IsMacOS()) return new MacHotkeys();
        return new NullHotkeyBackend();
    }
}

// fallback fuer nicht unterstuetzte plattformen (z.b. linux): app startet, sendet aber nichts
sealed class NullKeyBackend : IKeyBackend {
    public void Send(string key, bool up, string mode) { }
    public string? Diagnose() =>
        $"Plattform {Environment.OSVersion.Platform} wird nicht unterstuetzt — "
        + "Tastatur-Ausgabe ist deaktiviert (nur Windows und macOS).";
}

sealed class NullHotkeyBackend : IHotkeyBackend {
    public void Register(string key, Action onPress) { }
    public void Start() { }
    public void Dispose() { }
}
