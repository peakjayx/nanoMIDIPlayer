# tools

## release.py

baut die app und veroeffentlicht sie danach als neue github release
(inkl. version-bump, git tag, upload der assets). nur python-stdlib,
kein `pip install` noetig — laeuft auf jeder frischen maschine mit
python 3.

### voraussetzungen

- ein github token mit `repo`-scope (zum erstellen von releases). wird
  in dieser reihenfolge gesucht: `--token` → env `GITHUB_TOKEN` → env
  `GH_TOKEN` → `gh auth token` (wenn `gh` installiert und eingeloggt ist)
- `dotnet` im PATH (fuer den build-schritt)
- `git` im PATH, falls du die git-schritte nicht mit `--no-git` ueberspringst

### benutzung

```powershell
# normalfall: patch-bump, win-x64 bauen, release erstellen
python tools/release.py

# version explizit bumpen
python tools/release.py --minor
python tools/release.py --major

# version fest setzen statt bumpen
python tools/release.py --set 1.2.0

# aktuelle version aus der csproj unveraendert releasen
python tools/release.py --no-bump

# mehrere plattformen in einem release
python tools/release.py --rid win-x64 --rid win-arm64

# erst mal nur gucken was passieren wuerde, nichts wird veraendert
python tools/release.py --dry-run --no-git

# git komplett raushalten (kein commit/tag/push)
python tools/release.py --no-git
```

ueber `build/publish-win.ps1 -Release` laeuft das gleiche automatisch
nach einem erfolgreichen windows-build:

```powershell
.\build\publish-win.ps1 -Release
.\build\publish-win.ps1 -Release --minor --dry-run
```

zusaetzliche argumente nach `-Release` gehen 1:1 an `tools/release.py`
durch. `-Rid` betrifft nur den ps1-build selbst — falls du dem python
tool eine andere/zusaetzliche rid mitgeben willst, `--rid ...` explizit
anhaengen.

### was das script macht

1. checkt ob das arbeitsverzeichnis sauber ist (nur warnung, kein abbruch —
   `--allow-dirty` zum stummschalten)
2. bumpt `<Version>` in `nanoMIDIPlayerCS.csproj` (single source of truth,
   keine separate version.json)
3. baut per `dotnet publish` fuer jede angegebene `--rid` (default: `win-x64`).
   schlaegt der build fehl, bricht alles ab — es wird nie ein release aus
   einem kaputten build erzeugt
4. committet die csproj, taggt `v{version}`, pusht (wird übersprungen mit
   warnung falls kein git-repo bzw. kein remote da ist)
5. erstellt die github release ueber die REST api (auto-generierte notes,
   ausser `--notes` / `--notes-file` ist gesetzt)
6. laedt die build-assets hoch, benennt sie dabei passend fuer den
   in-app-updater um:
   - `nanoMIDIPlayer-win-x64.exe`
   - `nanoMIDIPlayer-win-arm64.exe`
   - `nanoMIDIPlayer-osx-arm64.zip`
   - `nanoMIDIPlayer-osx-x64.zip`

   existiert ein asset mit dem namen schon auf der release, wird es geloescht
   und neu hochgeladen. mac-assets werden übersprungen wenn nichts gebaut
   wurde (die reinen `dotnet publish` outputs fuer osx-rids sind kein
   fertiges `.app`-bundle — dafuer erst `build/publish-mac.sh` auf einem mac
   laufen lassen, das script findet das bundle dann automatisch und zippt es)
7. druckt am ende die url der fertigen release

alle flags: `python tools/release.py --help`
