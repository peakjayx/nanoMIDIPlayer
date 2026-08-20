# Projekt-Struktur (C# / Avalonia, Windows + macOS)

## Root
- nanoMIDIPlayerCS.csproj — .NET 10 + Avalonia, embedded defaultConfig,
  single-file publish props, app.manifest nur bei windows-targets
- app.manifest — dpi awareness (nur windows)
- defaultConfig.json — standard-config (aus python-original, embedded resource)
- Program.cs — avalonia entry point (StartWithClassicDesktopLifetime)
- App.axaml / App.axaml.cs — theme-resources laden, config laden, MainWindow bauen
- README.md — build + nutzung + mac-berechtigungen
- structure.md — diese datei

## Core (backend logik, plattformneutral)
- Core/Config.cs — config models + laden/speichern/merge
  (Documents/nanoMIDIPlayer/config.json, mac-pfad explizit gebaut weil
  MyDocuments auf unix $HOME liefert)
- Core/MidiFile.cs — eigener SMF parser -> timed events (delta sekunden), tempo-handling
- Core/KeyboardSender.cs — haelt den zustand gedrueckter tasten, delegiert ans backend
- Core/HotkeyManager.cs — duenne fassade ueber das hotkey-backend
- Core/Updater.cs — self-update gegen github releases (check + download +
  exe-swap via running-exe-rename, rollback auf jedem fehlerpfad)

## Core/Platform (der einzige plattformabhaengige teil)
- IKeyBackend.cs — interfaces IKeyBackend + IHotkeyBackend
- PlatformFactory.cs — waehlt backend nach OS, Null-backends als fallback
- WindowsKeyboard.cs — win32 SendInput (scancode/vk/unicode)
- WindowsHotkeys.cs — WH_KEYBOARD_LL-hook auf eigenem thread mit GetMessage-loop.
  pass-through (CallNextHookEx auf jedem pfad), filtert LLKHF_INJECTED weg damit
  die app ihre eigenen SendInput-tasten nicht als hotkey liest
- MacKeyboard.cs — Quartz CGEvent, eigene modifier-flag-verwaltung,
  ANSI-keycode-map, AXIsProcessTrusted() fuer die diagnose
- MacHotkeys.cs — CGEventTap (listen-only) auf eigenem thread mit CFRunLoop

## Player
- Player/PlaybackEngine.cs — midi->tastatur translator (port midiWindows.py):
  speed, pause, loop, finger-limit, velocity, sustain, swapYZ, randomFail,
  akkord-erkennung + spread, seek (absolute zeitachse absTimes[] + binaersuche)

## UI (Avalonia)
- UI/Theme.axaml — farben, MonoFont (Consolas/Menlo/DejaVu fallback-kette),
  ControlThemes fuer switch/button/entry/slider/combobox
- UI/Styles.axaml — globale defaults (TextBlock/Label)
- UI/MainWindow.axaml(.cs) — sidebar navigation + content, hotkey-registrierung
- UI/MidiPlayerView.axaml(.cs) — datei waehlen (StorageProvider), transport,
  speed/time, konsole; slider + switches werden im code verdrahtet
  (avalonia hat keine WPF-trigger im XAML)
- UI/SettingsView.axaml(.cs) — alle player + app settings
- UI/InfoView.axaml(.cs) — info-tab, zeigt OS + config-pfad + mac-berechtigungshinweis

## build
- build/publish-win.ps1 — single-file exe fuer win-x64 / win-arm64
- build/publish-mac.sh — single-file binary + .app bundle + icns + ad-hoc signatur
- build/Info.plist — bundle-metadaten fuer das .app

## tools
- tools/release.py — bumpt <Version> in der csproj, baut, taggt und legt den
  github-release samt asset an (nur stdlib, token via GITHUB_TOKEN/gh)
