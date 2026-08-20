using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace nanoMIDIPlayer.Core.Platform;

// globale hotkeys via WH_KEYBOARD_LL-hook auf eigenem thread mit message-loop.
// im gegensatz zum alten RegisterHotKey-ansatz frisst dieser hook die taste NICHT
// weg: er beobachtet nur (CallNextHookEx auf JEDEM pfad) und die taste kommt
// zusaetzlich beim fokussierten fenster/spiel an (z.b. F3/F5 in minecraft).
// message-loop bleibt noetig, weil windows den hook nur bedient, solange der
// thread, der ihn gesetzt hat, dispatcht -- darum hook + GetMessage im selben thread.
[SupportedOSPlatform("windows")]
public class WindowsHotkeys : IHotkeyBackend {
    const int WM_QUIT = 0x0012;
    const int WH_KEYBOARD_LL = 13;
    const int HC_ACTION = 0;
    const int WM_KEYDOWN = 0x0100;
    const int WM_KEYUP = 0x0101;
    const int WM_SYSKEYDOWN = 0x0104;
    const int WM_SYSKEYUP = 0x0105;
    const uint LLKHF_INJECTED = 0x10; // gesetzt bei events, die per SendInput injiziert wurden

    readonly List<(string key, Action act)> wanted = new();
    readonly Dictionary<uint, Action> handlers = new();
    readonly HashSet<uint> heldDown = new(); // entpreller: pro taste erst wieder feuern nach loslassen

    Thread? thread;
    uint threadId;
    volatile bool running;
    IntPtr hookHandle;
    HookProc? hookProc; // feld haelt den delegate am leben -- sonst sammelt der GC ihn ein und
                         // win32 ruft irgendwann in freigegebenen speicher -> crash

    public void Register(string key, Action onPress) => wanted.Add((key, onPress));

    public void Start() {
        if (thread != null) return;
        running = true;
        thread = new Thread(Loop) { IsBackground = true, Name = "hotkeys" };
        thread.Start();
    }

    void Loop() {
        threadId = GetCurrentThreadId();
        foreach (var (key, act) in wanted) {
            uint vk = Vk(key);
            if (vk != 0) handlers[vk] = act;
        }

        hookProc = HookCallback;
        IntPtr hMod = GetModuleHandle(null);
        hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, hMod, 0);
        if (hookHandle == IntPtr.Zero) {
            // haeufigste ursache: gruppenrichtlinie/AV blockt low-level-hooks, oder
            // kein desktop-interactive session. app degradiert still weiter -- die
            // message-loop unten laeuft trotzdem, nur eben ohne globale hotkeys.
            Console.Error.WriteLine(
                $"[hotkeys] SetWindowsHookEx fehlgeschlagen (win32-fehler {Marshal.GetLastWin32Error()}) " +
                "-- globale hotkeys sind deaktiviert, die app laeuft normal weiter.");
        }

        while (running && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0) { }

        if (hookHandle != IntPtr.Zero) {
            UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
        }
        handlers.Clear();
        heldDown.Clear();
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
        try {
            if (nCode == HC_ACTION) {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int msg = (int)wParam;

                // eigene SendInput-ausgabe (WindowsKeyboard.cs) faellt hier auch durch --
                // ohne diesen filter wuerde die app sich selbst wieder triggern (feedback-loop).
                if ((info.flags & LLKHF_INJECTED) == 0) {
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) {
                        if (handlers.TryGetValue(info.vkCode, out var act) && heldDown.Add(info.vkCode)) {
                            // aktion NICHT synchron im hook ausfuehren -- windows wirft hooks raus,
                            // die zu langsam antworten (LowLevelHooksTimeout), und die aktion
                            // (play/pause/stop/speed) darf die systemweite tastatur nicht blockieren.
                            ThreadPool.QueueUserWorkItem(_ => {
                                try { act(); } catch { }
                            });
                        }
                        // heldDown.Add liefert false bei auto-repeat (taste schon gehalten) ->
                        // wird ignoriert, bis WM_KEYUP die taste wieder freigibt.
                    } else if (msg == WM_KEYUP || msg == WM_SYSKEYUP) {
                        heldDown.Remove(info.vkCode);
                    }
                }
            }
        } catch {
            // eine exception, die aus einem hook-callback nach win32 zurueckfliegt, killt den
            // ganzen prozess -- darum hier schlucken statt propagieren.
        }
        // NIEMALS 1 zurueckgeben / das event schlucken -- immer an die kette weiterreichen,
        // sonst kommt die taste nie im fokussierten spiel an.
        return CallNextHookEx(hookHandle, nCode, wParam, lParam);
    }

    public void Dispose() {
        if (thread == null) return;
        running = false;
        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(500);
        thread = null;
    }

    static uint Vk(string key) => key.ToLowerInvariant() switch {
        "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73, "f5" => 0x74,
        "f6" => 0x75, "f7" => 0x76, "f8" => 0x77, "f9" => 0x78, "f10" => 0x79,
        "f11" => 0x7A, "f12" => 0x7B,
        _ => 0
    };

    [StructLayout(LayoutKind.Sequential)]
    struct MSG {
        public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
        public uint time; public int ptX; public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT {
        public uint vkCode; public uint scanCode; public uint flags;
        public uint time; public IntPtr dwExtraInfo;
    }

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll")]
    static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")]
    static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
}
