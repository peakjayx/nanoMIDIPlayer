# nanoMIDIPlayer

A cross-platform MIDI player written in **C# and Avalonia**, designed to translate MIDI notes into keyboard input for virtual piano applications and games.

nanoMIDIPlayer provides native builds for **Windows and macOS** while maintaining a shared codebase.

> This project is a C# / Avalonia port and reimplementation of the original Python project by [NotHammer043](https://github.com/NotHammer043/nanoMIDIPlayer).
>
> Both projects are licensed under the **GNU General Public License v3.0**. See [LICENSE](LICENSE) for the full license text.

## Features

* Support for 88-key and 61-key piano layouts
* Configurable keyboard mapping
* QWERTZ support with Y/Z key swapping
* No-Doubles mode
* MIDI looping
* Velocity control
* Sustain support
* Configurable finger limit
* Custom note hold duration
* Pitch and transpose offset
* Playback speed from 10% to 500%
* Multiple keyboard input modes:

  * `scancode` for games and applications using physical key input
  * `virtualkey` for virtual keyboard input
  * `unicode` for browsers and text-based applications
* Global playback hotkeys
* Pass-through hotkeys that do not consume the original key event
* Chord detection with configurable note staggering
* Playback seeking through a timeline slider
* ±10 second seek controls
* Automatic GitHub release updates
* Python-compatible configuration format
* Native Windows and macOS input backends

## Requirements

* [.NET SDK](https://dotnet.microsoft.com/download)
* Windows or macOS for native keyboard input support

Linux is currently supported only for launching the application. A Linux keyboard input backend has not yet been implemented.

## Building

Build the application in Release configuration:

```bash
dotnet build -c Release
```

Run the application:

```bash
dotnet run -c Release
```

## Release Builds

nanoMIDIPlayer can be published as a self-contained application, meaning the target system does not require a separate .NET installation.

### Windows

Run:

```powershell
.\build\publish-win.ps1
```

The resulting executable is located at:

```text
dist\win-x64\nanoMIDIPlayer.exe
```

The Windows x64 release is approximately 45 MB.

To build for Windows ARM64:

```powershell
.\build\publish-win.ps1 win-arm64
```

### macOS

The macOS release script should be executed on a Mac:

```bash
./build/publish-mac.sh
```

The default ARM64 build produces:

```text
dist/osx-arm64/nanoMIDIPlayer.app
```

For Intel-based Macs:

```bash
./build/publish-mac.sh osx-x64
```

The macOS build script performs the following tasks:

* Creates the `.app` application bundle
* Generates the application icon from `Assets/icon.png`
* Applies ad-hoc code signing

Cross-publishing from Windows is also possible:

```bash
dotnet publish -r osx-arm64 --self-contained true
```

However, cross-publishing produces only the executable and does not create the macOS `.app` bundle.

## Usage

1. Select **Select File** and load a `.mid` file.
2. Focus the target application or virtual piano.
3. Control playback using the global hotkeys.

| Key | Action                  |
| --- | ----------------------- |
| F1  | Play                    |
| F2  | Pause                   |
| F3  | Stop                    |
| F4  | Increase playback speed |
| F5  | Decrease playback speed |

The global hotkeys use pass-through input. nanoMIDIPlayer can respond to the configured function keys without preventing the focused application from receiving them.

This allows keys such as F3 and F5 to remain available in applications and games such as Minecraft.

## MIDI Playback

nanoMIDIPlayer converts MIDI note events into keyboard input according to the configured piano layout.

Playback supports:

* Velocity-sensitive note handling
* Sustain
* Configurable note hold duration
* Finger limits
* Transposition
* Playback speed adjustment
* Looping
* Timeline-based seeking

Playback speed can be configured between **10% and 500%**.

## Chord Detection

nanoMIDIPlayer includes an optional chord detection system.

When multiple MIDI notes occur simultaneously, they can be detected as a chord and sent to the target application with a configurable delay between individual notes.

This prevents large groups of keys from being pressed at exactly the same time and can produce more natural-sounding results in virtual piano applications.

Chord detection can also optionally take simultaneously released notes into account.

The timing offset is configurable through the application settings.

## Keyboard Input Modes

nanoMIDIPlayer supports multiple methods of generating keyboard input.

| Mode         | Intended Use                                                |
| ------------ | ----------------------------------------------------------- |
| `scancode`   | Games and applications that process physical keyboard input |
| `virtualkey` | Applications using virtual keyboard events                  |
| `unicode`    | Browsers and applications accepting Unicode input           |

On macOS, `scancode` and `virtualkey` use the same underlying mechanism because `CGEvent` operates with macOS virtual keycodes rather than Windows-style scan codes.

## Global Hotkeys

Global hotkeys are implemented using native platform APIs.

### Windows

* `RegisterHotKey`
* Dedicated Windows message-loop thread
* `SendInput` for keyboard events

### macOS

* `CGEventTap`
* Dedicated `CFRunLoop` thread
* `CGEventPost` for keyboard events

Hotkeys are intentionally implemented with pass-through behavior rather than consuming the original keyboard event.

As a result, pressing F1–F5 can control nanoMIDIPlayer while the focused application continues to receive the corresponding key event.

## macOS Configuration

macOS requires Accessibility permissions for applications that generate keyboard events and monitor global keyboard shortcuts.

### Accessibility Permission

Open:

**System Settings → Privacy & Security → Accessibility**

Enable `nanoMIDIPlayer` and restart the application.

Without this permission, macOS prevents nanoMIDIPlayer from sending keyboard events and receiving the required global hotkeys.

The application reports the current permission status in both the console and the **Info** tab.

### Function Keys

Open:

**System Settings → Keyboard**

Enable:

**Use F1, F2, etc. keys as standard function keys**

Without this option, macOS may interpret F1–F5 as system controls such as display brightness.

Alternatively, the function keys can be used together with the `fn` key.

### Rebuilding the Application

Each rebuild changes the application bundle and therefore its signature.

If global keyboard input stops working after rebuilding, remove and re-enable nanoMIDIPlayer under:

**System Settings → Privacy & Security → Accessibility**

## Configuration

The configuration file is stored at:

```text
Documents/nanoMIDIPlayer/config.json
```

The same configuration path is used on both Windows and macOS.

The configuration format is compatible with the original Python implementation where applicable.

## Automatic Updates

nanoMIDIPlayer checks GitHub Releases for available updates during application startup.

When a newer release is detected, the updater can:

1. Download the latest release.
2. Start the updated version.
3. Replace the previous executable where supported.

On Windows, the updater can replace the currently installed executable automatically.

The updater is integrated into this C# implementation and its status can be viewed from the **Info** tab.

## Platform Support

| Component              | Windows                         | macOS                      |
| ---------------------- | ------------------------------- | -------------------------- |
| UI Framework           | Avalonia / Win32                | Avalonia / Native          |
| Keyboard Input         | `SendInput`                     | `CGEventPost`              |
| Global Hotkeys         | `RegisterHotKey` + Message Loop | `CGEventTap` + `CFRunLoop` |
| Additional Permissions | None                            | Accessibility              |

### Linux

Linux can currently launch the application, but keyboard input is not implemented.

Instead of terminating unexpectedly, nanoMIDIPlayer detects the unavailable backend and reports the limitation through the console.

## Not Currently Ported

The following functionality from the original Python implementation is currently not included:

* Drums Macro
* MIDI Hub
* MIDI-to-QWERTY
* Themes
* Telemetry

The automatic updater is implemented in this version.

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

This project is derived from the original nanoMIDIPlayer implementation:

https://github.com/NotHammer043/nanoMIDIPlayer

The original project is also licensed under GPL-3.0.

Unless explicitly stated otherwise, all source files in this repository are distributed under the same license.

Release binaries are self-contained builds generated from this source code.

See [LICENSE](LICENSE) for the complete license text.
