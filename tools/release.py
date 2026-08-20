#!/usr/bin/env python3
"""
baut die app und veroeffentlicht sie als neue github release.

  python tools/release.py                          -> patch-bump, build win-x64, release erstellen
  python tools/release.py --minor --rid win-x64 --rid win-arm64
  python tools/release.py --set 1.2.0 --no-git      -> version setzen, aber nicht committen/pushen
  python tools/release.py --dry-run --no-git        -> nur anzeigen, nichts aendern

nur python-stdlib, kein pip install noetig (siehe tools/README.md).
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

# ---------------------------------------------------------------------------
# konstanten
# ---------------------------------------------------------------------------

ROOT = Path(__file__).resolve().parent.parent
CSPROJ = ROOT / "nanoMIDIPlayerCS.csproj"
DIST = ROOT / "dist"

DEFAULT_REPO = "peakjayx/nanoMIDIPlayer"
DEFAULT_RIDS = ["win-x64"]

VERSION_RE = re.compile(r"(<Version>)(\d+\.\d+\.\d+)(</Version>)")
SEMVER_RE = re.compile(r"^\d+\.\d+\.\d+$")


# ---------------------------------------------------------------------------
# kleine helfer fuer konsolenausgabe / fehlerbehandlung
# ---------------------------------------------------------------------------

def log(msg: str) -> None:
    print(f"==> {msg}")


def warn(msg: str) -> None:
    print(f"warnung: {msg}")


def die(msg: str) -> None:
    print(f"fehler: {msg}", file=sys.stderr)
    sys.exit(1)


class GhApiError(Exception):
    """fehler von der github api - status + rohtext des response body."""

    def __init__(self, status: int, body: str):
        super().__init__(f"{status}: {body}")
        self.status = status
        self.body = body


# ---------------------------------------------------------------------------
# cli
# ---------------------------------------------------------------------------

def parse_args(argv=None) -> argparse.Namespace:
    p = argparse.ArgumentParser(
        prog="release.py",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument(
        "--repo",
        default=os.environ.get("NANOMIDI_REPO", DEFAULT_REPO),
        help=f"github owner/repo (default: env NANOMIDI_REPO oder {DEFAULT_REPO})",
    )
    p.add_argument(
        "--token",
        default=None,
        help="github token (sonst GITHUB_TOKEN -> GH_TOKEN -> 'gh auth token')",
    )

    bump = p.add_mutually_exclusive_group()
    bump.add_argument("--major", action="store_true", help="X.0.0 - breaking changes")
    bump.add_argument("--minor", action="store_true", help="x.Y.0 - neue features")
    bump.add_argument("--patch", action="store_true", help="x.y.Z - default, bugfixes")
    bump.add_argument("--set", dest="set_version", metavar="X.Y.Z", help="version explizit setzen")
    bump.add_argument("--no-bump", action="store_true", help="aktuelle version unveraendert releasen")

    p.add_argument(
        "--rid",
        dest="rids",
        action="append",
        metavar="RID",
        help="runtime identifier zum bauen, mehrfach angebbar (default: win-x64)",
    )
    p.add_argument("--allow-dirty", action="store_true", help="warnung bei uncommitted changes unterdruecken")
    p.add_argument("--no-git", action="store_true", help="git add/commit/tag/push komplett ueberspringen")
    p.add_argument("--dry-run", action="store_true", help="nur anzeigen was passieren wuerde, nichts veraendern")

    notes = p.add_mutually_exclusive_group()
    notes.add_argument("--notes", default=None, help="release-notes text (statt auto-generierten notes)")
    notes.add_argument("--notes-file", default=None, metavar="PATH", help="release-notes aus datei lesen")

    args = p.parse_args(argv)

    if "/" not in args.repo:
        die(f"--repo erwartet 'owner/repo', bekommen: {args.repo}")
    if args.set_version and not SEMVER_RE.match(args.set_version):
        die(f"--set erwartet X.Y.Z, bekommen: {args.set_version}")
    if not args.rids:
        args.rids = list(DEFAULT_RIDS)

    return args


# ---------------------------------------------------------------------------
# version: einzige quelle der wahrheit ist <Version> im csproj
# ---------------------------------------------------------------------------

def read_version(csproj: Path) -> str:
    text = csproj.read_text(encoding="utf-8")
    m = VERSION_RE.search(text)
    if not m:
        die(f"konnte <Version>x.y.z</Version> nicht in {csproj} finden")
    return m.group(2)


def write_version(csproj: Path, new_version: str) -> None:
    text = csproj.read_text(encoding="utf-8")
    new_text, n = VERSION_RE.subn(lambda m: f"{m.group(1)}{new_version}{m.group(3)}", text, count=1)
    if n != 1:
        die("version-ersetzung im csproj fehlgeschlagen (regex hat nicht genau einmal getroffen)")
    csproj.write_text(new_text, encoding="utf-8")


def bump_version(version: str, args: argparse.Namespace) -> str:
    if args.set_version:
        return args.set_version
    if args.no_bump:
        return version
    major, minor, patch = (int(p) for p in version.split("."))
    if args.major:
        return f"{major + 1}.0.0"
    if args.minor:
        return f"{major}.{minor + 1}.0"
    return f"{major}.{minor}.{patch + 1}"  # default und --patch


# ---------------------------------------------------------------------------
# git: working tree checken, committen, taggen, pushen
# ---------------------------------------------------------------------------

def _git(args: list[str], **kw):
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, **kw)


def is_git_repo() -> bool:
    try:
        r = _git(["rev-parse", "--is-inside-work-tree"])
    except FileNotFoundError:
        return False
    return r.returncode == 0 and r.stdout.strip() == "true"


def git_remotes() -> list[str]:
    r = _git(["remote"])
    if r.returncode != 0:
        return []
    return [line.strip() for line in r.stdout.splitlines() if line.strip()]


def check_working_tree(args: argparse.Namespace) -> None:
    if not is_git_repo():
        return  # wird weiter unten in do_git_steps sauber gemeldet
    r = _git(["status", "--porcelain"])
    if r.returncode == 0 and r.stdout.strip():
        if args.allow_dirty:
            log("arbeitsverzeichnis ist nicht sauber, aber --allow-dirty gesetzt")
        else:
            warn("arbeitsverzeichnis hat uncommitted changes (--allow-dirty zum stummschalten)")


def do_git_steps(version: str, args: argparse.Namespace) -> None:
    tag = f"v{version}"

    if args.no_git:
        log("git-schritte uebersprungen (--no-git)")
        return
    if not is_git_repo():
        warn("kein git-repository gefunden - ueberspringe git add/commit/tag/push")
        return
    remotes = git_remotes()
    if not remotes:
        warn("kein git remote konfiguriert - ueberspringe git add/commit/tag/push")
        return
    remote = remotes[0]

    if args.dry_run:
        log(f"[dry-run] git add {CSPROJ.name}")
        log(f"[dry-run] git commit -m \"release {tag}\"")
        log(f"[dry-run] git tag {tag}")
        log(f"[dry-run] git push {remote} && git push {remote} {tag}")
        return

    log(f"==> git commit + tag {tag}")
    for cmd in (
        ["add", CSPROJ.name],
        ["commit", "-m", f"release {tag}"],
        ["tag", tag],
        ["push", remote],
        ["push", remote, tag],
    ):
        r = _git(cmd)
        if r.returncode != 0:
            die(f"git {' '.join(cmd)} fehlgeschlagen:\n{r.stderr.strip()}")


# ---------------------------------------------------------------------------
# build
# ---------------------------------------------------------------------------

def run_build(rids: list[str], dry_run: bool) -> None:
    for rid in rids:
        out_dir = f"dist/{rid}"
        cmd = [
            "dotnet", "publish", "nanoMIDIPlayerCS.csproj",
            "-c", "Release",
            "-r", rid,
            "--self-contained", "true",
            "-o", out_dir,
        ]
        if dry_run:
            log(f"[dry-run] {' '.join(cmd)}")
            continue
        log(f"build {rid}")
        proc = subprocess.run(cmd, cwd=ROOT)
        if proc.returncode != 0:
            die(
                f"build fuer {rid} fehlgeschlagen (exit {proc.returncode}) - "
                "release wird abgebrochen, es wurde nichts committet oder veroeffentlicht"
            )


# ---------------------------------------------------------------------------
# assets einsammeln (build-output -> release-asset-name)
# ---------------------------------------------------------------------------

def zip_directory(src_dir: Path, zip_path: Path) -> None:
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for f in src_dir.rglob("*"):
            if f.is_dir():
                continue
            arcname = (Path(src_dir.name) / f.relative_to(src_dir)).as_posix()
            zi = zipfile.ZipInfo(arcname)
            zi.compress_type = zipfile.ZIP_DEFLATED
            # unix-executable-bit setzen, sonst geht "ausfuehrbar" beim entpacken auf mac verloren
            executable = f.name == "nanoMIDIPlayer" and "MacOS" in f.parts
            zi.external_attr = ((0o755 if executable else 0o644) & 0xFFFF) << 16
            zf.writestr(zi, f.read_bytes())


def zip_bare_binary(src_file: Path, zip_path: Path) -> None:
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        zi = zipfile.ZipInfo(src_file.name)
        zi.compress_type = zipfile.ZIP_DEFLATED
        zi.external_attr = (0o755 & 0xFFFF) << 16
        zf.writestr(zi, src_file.read_bytes())


def collect_assets(rids: list[str], dry_run: bool) -> list[tuple[str, Path]]:
    """liefert liste von (asset_name, pfad zur datei die hochgeladen wird)."""
    assets: list[tuple[str, Path]] = []
    for rid in rids:
        rid_dir = DIST / rid
        if rid.startswith("win"):
            exe = rid_dir / "nanoMIDIPlayer.exe"
            asset_name = f"nanoMIDIPlayer-{rid}.exe"
            if exe.exists():
                assets.append((asset_name, exe))
            elif dry_run:
                assets.append((asset_name, exe))  # im dry-run auch ohne build-output anzeigen
            else:
                warn(f"{exe} nicht gefunden - asset {asset_name} wird uebersprungen")
        elif rid.startswith("osx"):
            asset_name = f"nanoMIDIPlayer-{rid}.zip"
            app_bundle = rid_dir / "nanoMIDIPlayer.app"
            bare_bin = rid_dir / "nanoMIDIPlayer"
            zip_path = rid_dir / asset_name
            if app_bundle.exists():
                if not dry_run:
                    zip_directory(app_bundle, zip_path)
                assets.append((asset_name, zip_path))
            elif bare_bin.exists():
                if not dry_run:
                    zip_bare_binary(bare_bin, zip_path)
                assets.append((asset_name, zip_path))
            elif dry_run:
                assets.append((asset_name, zip_path))
            else:
                warn(f"kein build-ergebnis fuer {rid} in {rid_dir} gefunden - mac-asset wird uebersprungen")
        else:
            warn(f"unbekannter rid '{rid}' - kein asset-namensschema dafuer, wird uebersprungen")
    return assets


# ---------------------------------------------------------------------------
# github api (nur stdlib: urllib)
# ---------------------------------------------------------------------------

def gh_request(method: str, url: str, token: str, data=None, content_type: str = "application/json"):
    headers = {
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "nanoMIDIPlayer-release-tool",
    }
    body = None
    if data is not None:
        if isinstance(data, (dict, list)):
            body = json.dumps(data).encode("utf-8")
            headers["Content-Type"] = "application/json"
        else:
            body = data
            headers["Content-Type"] = content_type

    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as resp:
            raw = resp.read()
            return resp.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        err_body = e.read().decode("utf-8", errors="replace")
        raise GhApiError(e.code, err_body) from None
    except urllib.error.URLError as e:
        raise GhApiError(0, str(e.reason)) from None


def resolve_token(explicit: str | None) -> str | None:
    if explicit:
        return explicit
    if os.environ.get("GITHUB_TOKEN"):
        return os.environ["GITHUB_TOKEN"]
    if os.environ.get("GH_TOKEN"):
        return os.environ["GH_TOKEN"]
    try:
        r = subprocess.run(["gh", "auth", "token"], capture_output=True, text=True)
        token = r.stdout.strip()
        if r.returncode == 0 and token:
            return token
    except FileNotFoundError:
        pass
    return None


def resolve_notes(args: argparse.Namespace) -> str | None:
    if args.notes:
        return args.notes
    if args.notes_file:
        path = Path(args.notes_file)
        if not path.is_file():
            die(f"--notes-file nicht gefunden: {path}")
        return path.read_text(encoding="utf-8")
    return None  # None = generate_release_notes: true verwenden


def create_or_get_release(repo: str, token: str, tag: str, notes: str | None, dry_run: bool) -> dict:
    owner, name = repo.split("/", 1)
    url = f"https://api.github.com/repos/{owner}/{name}/releases"
    payload = {"tag_name": tag, "name": tag}
    if notes is not None:
        payload["body"] = notes
    else:
        payload["generate_release_notes"] = True

    if dry_run:
        log(f"[dry-run] POST {url}")
        log(f"[dry-run] payload: {json.dumps(payload, ensure_ascii=False)}")
        return {"html_url": f"https://github.com/{repo}/releases/tag/{tag} (dry-run)", "upload_url": "", "assets": []}

    try:
        _, body = gh_request("POST", url, token, data=payload)
        return body
    except GhApiError as e:
        if e.status == 422 and "already_exists" in e.body:
            warn(f"release fuer tag {tag} existiert schon - bestehende release wird weiterverwendet")
            _, body = gh_request("GET", f"{url}/tags/{tag}", token)
            return body
        die(f"github api fehler {e.status} beim erstellen der release:\n{e.body}")


def upload_asset_url(upload_url_template: str, name: str) -> str:
    base = upload_url_template.split("{", 1)[0]
    return f"{base}?name={urllib.parse.quote(name)}"


def upload_asset(repo: str, token: str, release: dict, asset_name: str, asset_path: Path, dry_run: bool) -> None:
    if dry_run:
        log(f"[dry-run] upload {asset_name} <- {asset_path}")
        return

    owner, name = repo.split("/", 1)
    for existing in release.get("assets", []) or []:
        if existing.get("name") == asset_name:
            warn(f"asset {asset_name} existiert schon auf der release - wird geloescht und neu hochgeladen")
            del_url = f"https://api.github.com/repos/{owner}/{name}/releases/assets/{existing['id']}"
            try:
                gh_request("DELETE", del_url, token)
            except GhApiError as e:
                die(f"konnte bestehendes asset {asset_name} nicht loeschen: {e.status} {e.body}")

    data = asset_path.read_bytes()
    url = upload_asset_url(release["upload_url"], asset_name)
    log(f"upload {asset_name} ({len(data) / 1024 / 1024:.1f} MB)")
    try:
        gh_request("POST", url, token, data=data, content_type="application/octet-stream")
    except GhApiError as e:
        die(f"upload von {asset_name} fehlgeschlagen: {e.status} {e.body}")


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------

def main(argv=None) -> int:
    args = parse_args(argv)

    log(f"repo: {args.repo}")
    log(f"rids: {', '.join(args.rids)}")
    if args.dry_run:
        log("dry-run modus - es wird nichts geschrieben, committet, gepusht oder hochgeladen")

    # token frueh aufloesen, damit wir nicht erst nach einem langen build merken dass er fehlt
    token = resolve_token(args.token)
    if not args.dry_run and not token:
        die("kein github token gefunden (--token / GITHUB_TOKEN / GH_TOKEN / 'gh auth token')")
    if args.dry_run and not token:
        warn("kein github token gefunden - im echten lauf wuerde das hier abbrechen")

    # a) working tree checken
    check_working_tree(args)

    # b) version bumpen
    old_version = read_version(CSPROJ)
    new_version = bump_version(old_version, args)
    suffix = " (unveraendert)" if new_version == old_version else ""
    log(f"version: {old_version} -> {new_version}{suffix}")
    if args.dry_run:
        log(f"[dry-run] {CSPROJ.name} wird NICHT geschrieben")
    else:
        write_version(CSPROJ, new_version)

    # c) build (bricht bei fehler sofort mit exit != 0 ab)
    run_build(args.rids, args.dry_run)

    # d) git: add, commit, tag, push
    do_git_steps(new_version, args)

    # e) github release erstellen
    tag = f"v{new_version}"
    notes = resolve_notes(args)
    release = create_or_get_release(args.repo, token, tag, notes, args.dry_run)

    # f) assets bauen/hochladen
    assets = collect_assets(args.rids, args.dry_run)
    if not assets:
        warn("keine build-assets gefunden - release bekommt keine anhaenge")
    for asset_name, asset_path in assets:
        upload_asset(args.repo, token, release, asset_name, asset_path, args.dry_run)

    # g) ergebnis
    log(f"fertig: {release.get('html_url', '<unbekannt>')}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("\nabgebrochen", file=sys.stderr)
        sys.exit(130)
