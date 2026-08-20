using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace nanoMIDIPlayer.Core.Platform;

// win32 SendInput tastatur-simulation
[SupportedOSPlatform("windows")]
public class WindowsKeyboard : IKeyBackend {
    public string? Diagnose() => null;

    public void Send(string key, bool up, string mode) {
        if (mode == "unicode" && key.Length == 1 && !IsModifier(key)) {
            SendUnicode(key[0], up);
            return;
        }
        ushort vk = Vk(key);
        if (vk == 0) return;
        SendVk(vk, up, mode);
    }

    static bool IsModifier(string k) => k is "shift" or "ctrl" or "alt" or "space";

    static ushort Vk(string k) => k switch {
        "shift" => 0x10,
        "ctrl" => 0x11,
        "alt" => 0x12,
        "space" => 0x20,
        _ when k.Length == 1 => char.ToUpperInvariant(k[0]) is var c && (c >= 'A' && c <= 'Z' || c >= '0' && c <= '9')
            ? (ushort)c : (ushort)0,
        _ => 0
    };

    static void SendVk(ushort vk, bool up, string mode) {
        var inp = new INPUT { type = INPUT_KEYBOARD };
        inp.u.ki.wVk = vk;
        inp.u.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        if (mode == "scancode") {
            ushort scan = (ushort)MapVirtualKey(vk, 0);
            if (scan != 0) {
                inp.u.ki.wVk = 0;
                inp.u.ki.wScan = scan;
                inp.u.ki.dwFlags |= KEYEVENTF_SCANCODE;
            }
        }
        SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
    }

    static void SendUnicode(char ch, bool up) {
        var inp = new INPUT { type = INPUT_KEYBOARD };
        inp.u.ki.wScan = ch;
        inp.u.ki.dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0);
        SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
    }

    // --- win32 ---
    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_SCANCODE = 0x0008;
    const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT {
        public ushort wVk; public ushort wScan; public uint dwFlags;
        public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT {
        public int dx; public int dy; public uint mouseData;
        public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);
}
