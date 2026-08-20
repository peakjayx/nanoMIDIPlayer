using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace nanoMIDIPlayer.Core.Platform;

// macOS tastatur-simulation via Quartz Event Services (CGEvent)
// braucht "Bedienungshilfen"-Recht: Systemeinstellungen > Datenschutz & Sicherheit > Bedienungshilfen
[SupportedOSPlatform("macos")]
public class MacKeyboard : IKeyBackend {
    const string AppServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    const uint kCGHIDEventTap = 0;
    const int kCGEventSourceStateHIDSystemState = 1;

    const ulong FlagShift = 0x00020000;
    const ulong FlagControl = 0x00040000;
    const ulong FlagAlternate = 0x00080000;

    readonly IntPtr source = CGEventSourceCreate(kCGEventSourceStateHIDSystemState);
    // modifier-zustand selbst fuehren: auf macOS uebernimmt CGEventPost die flags
    // nicht zuverlaessig aus vorher gesendeten modifier-events
    ulong flags;

    public string? Diagnose() =>
        AXIsProcessTrusted() ? null
            : "macOS: Bedienungshilfen-Recht fehlt — Systemeinstellungen > Datenschutz & "
            + "Sicherheit > Bedienungshilfen > nanoMIDIPlayer aktivieren, dann App neu starten.";

    public void Send(string key, bool up, string mode) {
        // scancode/virtualkey sind auf macOS identisch (CGEvent kennt nur virtual keycodes)
        if (mode == "unicode" && key.Length == 1 && !IsModifier(key)) {
            SendUnicode(key[0], up);
            return;
        }

        ulong mod = ModifierFlag(key);
        if (mod != 0) {
            if (up) flags &= ~mod;
            else flags |= mod;
        }

        if (!KeyCode(key, out ushort code)) return;
        Post(code, !up);
    }

    static bool IsModifier(string k) => k is "shift" or "ctrl" or "alt" or "space";

    static ulong ModifierFlag(string k) => k switch {
        "shift" => FlagShift,
        "ctrl" => FlagControl,
        "alt" => FlagAlternate,
        _ => 0
    };

    void Post(ushort code, bool down) {
        IntPtr ev = CGEventCreateKeyboardEvent(source, code, down);
        if (ev == IntPtr.Zero) return;
        CGEventSetFlags(ev, flags);
        CGEventPost(kCGHIDEventTap, ev);
        CFRelease(ev);
    }

    void SendUnicode(char ch, bool up) {
        IntPtr ev = CGEventCreateKeyboardEvent(source, 0, !up);
        if (ev == IntPtr.Zero) return;
        CGEventKeyboardSetUnicodeString(ev, 1, new[] { (ushort)ch });
        CGEventPost(kCGHIDEventTap, ev);
        CFRelease(ev);
    }

    // --- keycode map (ANSI / US-QWERTY physische positionen) ---
    // wie scancode-mode auf windows: es wird die PHYSISCHE taste geschickt,
    // das ziel-programm interpretiert sie mit dem aktiven layout
    static readonly Dictionary<string, ushort> Named = new() {
        { "space", 0x31 }, { "shift", 0x38 }, { "alt", 0x3A }, { "ctrl", 0x3B },
        { "cmd", 0x37 }, { "enter", 0x24 }, { "tab", 0x30 }, { "esc", 0x35 },
    };

    static readonly Dictionary<char, ushort> Chars = new() {
        { 'a', 0x00 }, { 's', 0x01 }, { 'd', 0x02 }, { 'f', 0x03 }, { 'h', 0x04 },
        { 'g', 0x05 }, { 'z', 0x06 }, { 'x', 0x07 }, { 'c', 0x08 }, { 'v', 0x09 },
        { 'b', 0x0B }, { 'q', 0x0C }, { 'w', 0x0D }, { 'e', 0x0E }, { 'r', 0x0F },
        { 'y', 0x10 }, { 't', 0x11 }, { 'o', 0x1F }, { 'u', 0x20 }, { 'i', 0x22 },
        { 'p', 0x23 }, { 'l', 0x25 }, { 'j', 0x26 }, { 'k', 0x28 }, { 'n', 0x2D },
        { 'm', 0x2E },
        { '1', 0x12 }, { '2', 0x13 }, { '3', 0x14 }, { '4', 0x15 }, { '5', 0x17 },
        { '6', 0x16 }, { '7', 0x1A }, { '8', 0x1C }, { '9', 0x19 }, { '0', 0x1D },
        { '-', 0x1B }, { '=', 0x18 }, { '[', 0x21 }, { ']', 0x1E }, { '\\', 0x2A },
        { ';', 0x29 }, { '\'', 0x27 }, { ',', 0x2B }, { '.', 0x2F }, { '/', 0x2C },
        { '`', 0x32 },
    };

    static bool KeyCode(string key, out ushort code) {
        if (Named.TryGetValue(key, out code)) return true;
        if (key.Length == 1 && Chars.TryGetValue(char.ToLowerInvariant(key[0]), out code)) return true;
        code = 0;
        return false;
    }

    // --- native ---
    [DllImport(AppServices)]
    static extern IntPtr CGEventSourceCreate(int stateID);

    [DllImport(AppServices)]
    static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey,
        [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(AppServices)]
    static extern void CGEventPost(uint tap, IntPtr evt);

    [DllImport(AppServices)]
    static extern void CGEventSetFlags(IntPtr evt, ulong flags);

    [DllImport(AppServices)]
    static extern void CGEventKeyboardSetUnicodeString(IntPtr evt, nuint length,
        [In] ushort[] str);

    [DllImport(AppServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    static extern bool AXIsProcessTrusted();

    [DllImport(CoreFoundation)]
    static extern void CFRelease(IntPtr obj);
}
