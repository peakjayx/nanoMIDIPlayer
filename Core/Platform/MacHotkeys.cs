using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace nanoMIDIPlayer.Core.Platform;

// globale hotkeys auf macOS via CGEventTap (listen-only) auf eigenem thread
// mit eigenem CFRunLoop. braucht dasselbe Bedienungshilfen-Recht wie MacKeyboard.
[SupportedOSPlatform("macos")]
public class MacHotkeys : IHotkeyBackend {
    const string AppServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    const uint kCGSessionEventTap = 1;
    const uint kCGHeadInsertEventTap = 0;
    const uint kCGEventTapOptionListenOnly = 1;
    const uint kCGEventKeyDown = 10;
    const uint kCGEventTapDisabledByTimeout = 0xFFFFFFFE;
    const int kCGKeyboardEventKeycode = 9;

    readonly Dictionary<long, Action> handlers = new();
    Thread? thread;
    IntPtr runLoop, tapPort;
    TapCallback? callback; // feld haelt den delegate am leben (sonst GC)

    public void Register(string key, Action onPress) {
        long code = KeyCode(key);
        if (code >= 0) handlers[code] = onPress;
    }

    public void Start() {
        if (thread != null || handlers.Count == 0) return;
        thread = new Thread(Loop) { IsBackground = true, Name = "hotkeys" };
        thread.Start();
    }

    void Loop() {
        callback = OnEvent;
        tapPort = CGEventTapCreate(kCGSessionEventTap, kCGHeadInsertEventTap,
            kCGEventTapOptionListenOnly, 1UL << (int)kCGEventKeyDown, callback, IntPtr.Zero);
        if (tapPort == IntPtr.Zero) return; // kein Bedienungshilfen-Recht

        IntPtr src = CFMachPortCreateRunLoopSource(IntPtr.Zero, tapPort, 0);
        runLoop = CFRunLoopGetCurrent();
        IntPtr mode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", 0x08000100);
        CFRunLoopAddSource(runLoop, src, mode);
        CGEventTapEnable(tapPort, true);
        CFRunLoopRun();

        CFRelease(mode);
        CFRelease(src);
    }

    IntPtr OnEvent(IntPtr proxy, uint type, IntPtr evt, IntPtr userInfo) {
        if (type == kCGEventTapDisabledByTimeout) {
            CGEventTapEnable(tapPort, true);
            return evt;
        }
        if (type == kCGEventKeyDown) {
            long code = CGEventGetIntegerValueField(evt, kCGKeyboardEventKeycode);
            if (handlers.TryGetValue(code, out var act)) {
                try { act(); } catch { }
            }
        }
        return evt; // listen-only: event unveraendert durchlassen
    }

    public void Dispose() {
        if (thread == null) return;
        if (runLoop != IntPtr.Zero) CFRunLoopStop(runLoop);
        thread.Join(500);
        thread = null;
    }

    // macOS virtual keycodes der F-tasten
    static long KeyCode(string key) => key.ToLowerInvariant() switch {
        "f1" => 0x7A, "f2" => 0x78, "f3" => 0x63, "f4" => 0x76, "f5" => 0x60,
        "f6" => 0x61, "f7" => 0x62, "f8" => 0x64, "f9" => 0x65, "f10" => 0x6D,
        "f11" => 0x67, "f12" => 0x6F,
        _ => -1
    };

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr TapCallback(IntPtr proxy, uint type, IntPtr evt, IntPtr userInfo);

    [DllImport(AppServices)]
    static extern IntPtr CGEventTapCreate(uint tap, uint place, uint options,
        ulong eventsOfInterest, TapCallback callback, IntPtr userInfo);

    [DllImport(AppServices)]
    static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(AppServices)]
    static extern long CGEventGetIntegerValueField(IntPtr evt, int field);

    [DllImport(CoreFoundation)]
    static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr alloc, IntPtr port, nint order);
    [DllImport(CoreFoundation)]
    static extern IntPtr CFRunLoopGetCurrent();
    [DllImport(CoreFoundation)]
    static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);
    [DllImport(CoreFoundation)]
    static extern void CFRunLoopRun();
    [DllImport(CoreFoundation)]
    static extern void CFRunLoopStop(IntPtr rl);
    [DllImport(CoreFoundation)]
    static extern IntPtr CFStringCreateWithCString(IntPtr alloc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string str, uint encoding);
    [DllImport(CoreFoundation)]
    static extern void CFRelease(IntPtr obj);
}
