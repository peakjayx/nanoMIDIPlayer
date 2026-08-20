using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace nanoMIDIPlayer.Core.Platform;

// globale hotkeys via RegisterHotKey auf eigenem thread mit message-loop.
// hWnd=NULL registriert auf die THREAD-queue, darum muessen register/unregister
// und GetMessage alle im selben thread laufen.
[SupportedOSPlatform("windows")]
public class WindowsHotkeys : IHotkeyBackend {
    const int WM_HOTKEY = 0x0312;
    const int WM_QUIT = 0x0012;

    readonly List<(string key, Action act)> wanted = new();
    readonly Dictionary<int, Action> handlers = new();
    Thread? thread;
    uint threadId;
    volatile bool running;

    public void Register(string key, Action onPress) => wanted.Add((key, onPress));

    public void Start() {
        if (thread != null) return;
        running = true;
        thread = new Thread(Loop) { IsBackground = true, Name = "hotkeys" };
        thread.Start();
    }

    void Loop() {
        threadId = GetCurrentThreadId();
        int id = 1;
        foreach (var (key, act) in wanted) {
            uint vk = Vk(key);
            if (vk == 0) continue;
            if (RegisterHotKey(IntPtr.Zero, id, 0, vk)) handlers[id] = act;
            id++;
        }

        while (running && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0) {
            if (msg.message == WM_HOTKEY && handlers.TryGetValue((int)msg.wParam, out var act))
                act();
        }

        foreach (var hid in handlers.Keys) UnregisterHotKey(IntPtr.Zero, hid);
        handlers.Clear();
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

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")]
    static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
}
