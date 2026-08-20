# nanoMIDIPlayer

A cross-platform MIDI player written in **C# / Avalonia** for **Windows and macOS**.

nanoMIDIPlayer plays MIDI files by translating MIDI notes into keyboard input, making it suitable for virtual piano games and applications such as **Roblox**.

One codebase, one project, native builds for Windows and macOS.

> **Fork / port of:** https://github.com/NotHammer043/nanoMIDIPlayer
> The original project is written in Python. This version is a complete C# / Avalonia reimplementation and is licensed under the **GNU General Public License v3.0**, like the original. See [LICENSE](LICENSE).

## Features

* 🎹 88-key and 61-key keyboard mapping
* 🚫 No-Doubles mode
* 🔄 QWERTZ support with Y/Z swapping
* 🔁 MIDI looping
* 🎚️ Velocity control
* 🎵 Sustain support
* ✋ Finger limit
* ⏱️ Custom note hold length
* 🎼 Pitch / transpose offset
* ⚡ Playback speed from **10% to 500%**
* ⌨️ Multiple keyboard input modes:

  * `scancode` for games
  * `virtualkey`
  * `unicode` for browsers and applications
* 🌍 Global hotkeys on Windows and macOS
* 🎶 Chord detection with configurable note staggering
* ⏩ Seek bar with ±10-second controls
* 🔄 Automatic GitHub release updater
* ⚙️ Python-compatible configuration file
* 🖥️ Native Windows and macOS support

## Build

### Requirements

* [.NET SDK](https://dotnet.microsoft.com/download)
* Windows or macOS for the respective native input backend

Build the project in Release mode:

```bash
dotnet build -c Release
```

Run the application:

```bash
dotnet run -c Release
```

## Single-File Releases

The release scripts create **self-contained builds**, so users do not need to install .NET separately.

### Windows

```powershell
.\build\publish-win.ps1
```

Output:

```text
dist\win-x64\nanoMIDIPlayer.exe
```

The Windows executable is approximately **45 MB**.

To build for Windows ARM64:

```powershell
.\build\publish-win.ps1 win-arm64
```

### macOS

Run the following on a Mac:

```bash
./build/publish-mac.sh
```

Output:

```text
dist/osx-arm64/nanoMIDIPlayer.app
```

For Intel Macs:

```bash
./build/publish-mac.sh osx-x64
```

The macOS build script:

* Creates the `.app` bundle
* Generates the application icon from `Assets/icon.png`
* Applies ad-hoc code signing

Cross-publishing from Windows is also possible:

```bash
dotnet publish -r osx-arm64 --self-contained true
```

However, cross-publishing only produces the raw executable and does **not** create the macOS `.app` bundle.

## Usage

1. Click **Select File** and load a `.mid` file.
2. Focus the target window, such as your virtual piano.
3. Use the global hotkeys to control playback:

| Key  | Action         |
| ---- | -------------- |
| `F1` | Play           |
| `F2` | Pause          |
| `F3` | Stop           |
| `F4` | Increase speed |
| `F5` | Decrease speed |

The hotkeys use **pass-through input**, meaning the application reacts to the key while the key is still delivered to the currently focused application.

This allows keys such as `F3` and `F5` to remain usable in games like Minecraft.

## macOS Setup

macOS requires additional permissions for applications that generate keyboard input and listen for global hotkeys.

### 1. Enable Accessibility Permissions

Go to:

**System Settings → Privacy & Security → Accessibility**

Enable `nanoMIDIPlayer`.

Restart the application afterward.

Without Accessibility permission, macOS will not allow nanoMIDIPlayer to send keyboard events or receive the required global hotkeys.

The application reports the permission status in both the console and the **Info** tab.

### 2. Enable Standard Function Keys

Go to:

**System Settings → Keyboard**

Enable:

> **Use F1, F2, etc. keys as standard function keys**

Otherwise, macOS may interpret `F1` as a brightness control instead of sending it as a function key.

Alternatively, hold `fn` while using the hotkeys.

### Rebuilds and Permissions

After rebuilding the application, the bundle signature changes.

If global hotkeys suddenly stop working after a rebuild, remove and re-enable `nanoMIDIPlayer` under:

**System Settings → Privacy & Security → Accessibility**

This is a macOS security feature, because apparently letting an application press keys on your behalf requires a small bureaucratic ceremony. 🔐

## Chord Detection

nanoMIDIPlayer includes optional chord detection.

When multiple MIDI notes are played simultaneously, the application can detect them as a chord and slightly stagger their keyboard input instead of sending every key at exactly the same time.

This produces a more natural result when playing virtual pianos.

The amount of staggering can be configured in the settings.

The detection can also optionally account for notes being released simultaneously.

## Input Modes

nanoMIDIPlayer supports several keyboard input modes:

| Mode         | Intended Use                                                |
| ------------ | ----------------------------------------------------------- |
| `scancode`   | Games and applications that process physical keyboard input |
| `virtualkey` | Standard virtual keyboard input                             |
| `unicode`    | Browsers and applications that accept Unicode text input    |

On macOS, `scancode` and `virtualkey` are effectively identical because `CGEvent` works with macOS virtual keycodes rather than Windows-style scan codes.

## Global Hotkeys

Global hotkeys are implemented differently on each platform.

### Windows

Uses:

* `RegisterHotKey`
* Dedicated Windows message-loop thread
* Native Win32 keyboard input

### macOS

Uses:

* `CGEventTap`
* Dedicated `CFRunLoop` thread
* Quartz `CGEvent` input

Hotkeys are intentionally implemented with **pass-through behavior** rather than consuming the key event.

This means pressing `F1`–`F5` can control nanoMIDIPlayer while the focused application still receives the original key.

## Platform Support

|                        | Windows                         | macOS                      |
| ---------------------- | ------------------------------- | -------------------------- |
| UI                     | Avalonia / Win32                | Avalonia / Native          |
| Keyboard input         | `SendInput`                     | `CGEventPost`              |
| Global hotkeys         | `RegisterHotKey` + message loop | `CGEventTap` + `CFRunLoop` |
| Additional permissions | None                            | Accessibility              |

### Linux

Linux can currently start the application, but keyboard input is **not implemented**.

Instead of crashing, nanoMIDIPlayer detects the missing backend and reports the limitation in the console.

## Configuration

The configuration file is stored at:

```text
Documents/nanoMIDIPlayer/config.json
```

The same path is used on both Windows and macOS.

The configuration format is compatible with the original Python version where applicable.

## Automatic Updates

nanoMIDIPlayer checks GitHub Releases for newer versions when the application starts.

If a newer version is available, the updater:

1. Detects the latest release.
2. Downloads the update.
3. Starts the updated version.
4. Replaces the old executable where supported.

On Windows, the running executable can be replaced automatically by the updater.

The updater is included in this C# port and can also be viewed from the **Info** tab.

## Not Ported

The following features from the original project are currently not included:

* Drums Macro
* MIDI Hub
* MIDI-to-QWERTY
* Themes
* Telemetry

The automatic updater **is implemented** in this version.

## Project Structure

```text
nanoMIDIPlayer/
├── Assets/
│   └── icon.png
├── build/
│   ├── publish-win.ps1
│   └── publish-mac.sh
├── LICENSE
└── ...
```

## License

nanoMIDIPlayer is licensed under the **GNU General Public License v3.0**.

This project is derived from:

https://github.com/NotHammer043/nanoMIDIPlayer

The original project is also licensed under GPL-3.0.

All source files in this repository are distributed under the same license unless explicitly stated otherwise.

The release binaries are self-contained builds generated from this source code.

See [LICENSE](LICENSE) for the complete license text.
