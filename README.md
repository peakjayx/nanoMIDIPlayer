# nanoMIDIPlayer — C# / Windows + macOS

Port des MIDI Players. Spielt MIDI-Dateien ab, indem Noten in Tastendrücke
übersetzt werden — für virtuelle Pianos (z.B. Roblox).
Eine Codebase, ein Build, läuft nativ auf Windows und macOS.

> Fork / Port von [NotHammer043/nanoMIDIPlayer](https://github.com/NotHammer043/nanoMIDIPlayer)
> (Python). Diese Version ist eine Neuimplementierung in C# / Avalonia und steht
> wie das Original unter der **GNU General Public License v3.0** — siehe [LICENSE](LICENSE).

## Build & Start
```
dotnet build -c Release
dotnet run -c Release
```

## Single-File Release
Ergebnis ist jeweils **eine** ausführbare Datei, self-contained (kein .NET nötig):

```powershell
# Windows
.\build\publish-win.ps1              # -> dist\win-x64\nanoMIDIPlayer.exe   (~45 MB)
.\build\publish-win.ps1 win-arm64
```
```bash
# macOS (auf einem Mac ausführen)
./build/publish-mac.sh               # -> dist/osx-arm64/nanoMIDIPlayer.app
./build/publish-mac.sh osx-x64       # intel
```
Das Mac-Script baut das `.app`-Bundle, erzeugt das Icon aus `Assets/icon.png`
und signiert ad-hoc. Cross-Publish von Windows aus geht auch
(`dotnet publish -r osx-arm64 --self-contained true`), erzeugt aber nur das
nackte Binary ohne Bundle.

## Nutzung
1. "Select File" → .mid laden
2. Ziel-Fenster (Piano-Spiel) fokussieren
3. F1 Play · F2 Pause · F3 Stop · F4 Speed+ · F5 Speed-

## macOS — einmalige Einrichtung
Ohne diese zwei Punkte passiert nichts:

1. **Bedienungshilfen-Recht**: Systemeinstellungen → Datenschutz & Sicherheit →
   Bedienungshilfen → `nanoMIDIPlayer` aktivieren, danach App neu starten.
   Ohne das Recht kann macOS weder Tasten senden noch die Hotkeys empfangen.
   Die App meldet das beim Start in der Konsole und im Info-Tab.
2. **F-Tasten**: Systemeinstellungen → Tastatur → "F1, F2 usw. als
   Standardfunktionstasten verwenden" aktivieren — sonst löst F1 die
   Helligkeitssteuerung aus statt den Hotkey (alternativ mit `fn` bedienen).

Nach jedem Rebuild ändert sich die Signatur des Bundles; wenn Hotkeys plötzlich
nicht mehr gehen, das Recht in den Systemeinstellungen einmal ab- und wieder
anwählen.

## Features
- 88 / 61 Key Mapping, No Doubles, Swap Y/Z (QWERTZ), Loop
- Velocity, Sustain, Finger Limit, Custom Hold Length
- Pitch / Transpose Offset, Speed 10–500%
- Send Mode: scancode (games) / virtualkey / unicode (browser)
  — auf macOS sind scancode und virtualkey identisch, weil CGEvent nur
  virtual keycodes kennt
- Globale Hotkeys auf beiden Plattformen — **pass-through**: die App reagiert
  auf F1–F5, die Taste kommt trotzdem im fokussierten Spiel an (F3/F5 bleiben
  in Minecraft nutzbar). Kein `RegisterHotKey`, das Tasten wegfrisst.
- Akkord-Erkennung: gleichzeitig gespielte (und optional losgelassene) Noten
  werden erkannt und um einstellbare Millisekunden versetzt angeschlagen,
  statt als Block — klingt menschlicher. Regler in den Settings.
- Spulleiste: vor-/zurückspulen per Slider oder ±10s-Buttons
- Autoupdater: prüft beim Start die GitHub-Releases, lädt die neue Version
  und startet sie (Windows tauscht die laufende .exe direkt aus)
- Config in `Documents/nanoMIDIPlayer/config.json` (python-kompatibel, gleicher
  Pfad unter Windows und macOS)

## Plattform-Technik
| | Windows | macOS |
|---|---|---|
| UI | Avalonia (Win32) | Avalonia (Native) |
| Tasten senden | `SendInput` | `CGEventPost` (Quartz) |
| Globale Hotkeys | `RegisterHotKey` + Message-Loop-Thread | `CGEventTap` + CFRunLoop-Thread |
| Berechtigung | keine | Bedienungshilfen |

Linux startet, sendet aber keine Tasten (kein Backend implementiert) — die App
meldet das in der Konsole statt zu crashen.

## Nicht portiert
Drums Macro, MIDI Hub, MIDI-to-QWERTY, Themes, Telemetry.
(Der Updater ist in dieser Version implementiert — siehe Info-Tab.)

## Lizenz
GPL-3.0. Abgeleitet von [NotHammer043/nanoMIDIPlayer](https://github.com/NotHammer043/nanoMIDIPlayer),
ebenfalls GPL-3.0. Alle Dateien in diesem Repo stehen unter derselben Lizenz.
Die Release-Binaries sind self-contained Builds dieses Quellcodes.
