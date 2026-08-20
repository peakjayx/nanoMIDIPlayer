# baut nanoMIDIPlayer.exe als einzelne self-contained datei fuer windows.
#
#   .\build\publish-win.ps1                    -> win-x64
#   .\build\publish-win.ps1 win-arm64          -> arm64
#   .\build\publish-win.ps1 -Release           -> win-x64 bauen, danach tools/release.py aufrufen
#   .\build\publish-win.ps1 -Release --minor   -> zusaetzliche args gehen 1:1 an tools/release.py durch
#
param(
    [string]$Rid = "win-x64",
    [switch]$Release,
    # alles was hier nicht oben passt (z.b. --minor, --dry-run, --repo ...) wird 1:1
    # an tools/release.py durchgereicht
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ReleaseArgs
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out  = Join-Path $root "dist\$Rid"

Write-Host "==> publish $Rid"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

dotnet publish (Join-Path $root "nanoMIDIPlayerCS.csproj") `
    -c Release `
    -r $Rid `
    --self-contained true `
    -o $out

if ($LASTEXITCODE -ne 0) {
    Write-Host "publish fehlgeschlagen (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

$exe = Join-Path $out "nanoMIDIPlayer.exe"
$mb  = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "fertig: $exe ($mb MB)"

# -Release ist opt-in: nur wenn explizit gesetzt, und nur weil der publish oben
# gerade erfolgreich war (sonst haette exit oben schon abgebrochen)
if ($Release) {
    Write-Host ""
    Write-Host "==> release (tools/release.py)"
    python (Join-Path $root "tools\release.py") @ReleaseArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "release fehlgeschlagen (exit $LASTEXITCODE)"
        exit $LASTEXITCODE
    }
}
