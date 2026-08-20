using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;

namespace nanoMIDIPlayer.Core;

// ergebnis eines update-checks
public class UpdateInfo {
    public string Version { get; init; } = "";     // X.Y.Z ohne "v"
    public string TagName { get; init; } = "";
    public string AssetUrl { get; init; } = "";
    public string AssetName { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
    public bool Prerelease { get; init; }
}

// nur die felder aus der github-release-api die wir brauchen
class GhAsset {
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string Url { get; set; } = "";
}

class GhRelease {
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("assets")] public List<GhAsset> Assets { get; set; } = new();
}

// self-update gegen github releases von peakjayx/nanoMIDIPlayer.
// windows: exe wird direkt im platz ausgetauscht (running-exe-rename-trick, kein installer noetig).
// macOS: nur erkennung + hinweis, kein auto-swap (siehe DownloadAndInstallAsync).
public static class Updater {
    const string Repo = "peakjayx/nanoMIDIPlayer";
    const string ApiLatest = "https://api.github.com/repos/" + Repo + "/releases/latest";
    const string ApiAll = "https://api.github.com/repos/" + Repo + "/releases";

    public static event Action<string>? OnLog;
    // wird gefeuert, nachdem die neue version erfolgreich gestartet wurde -> app muss jetzt sauber beenden
    public static event Action? OnRestartNeeded;

    static void Log(string msg) => OnLog?.Invoke(msg);

    static readonly HttpClient Http = BuildClient();

    static HttpClient BuildClient() {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // github verlangt einen User-Agent, sonst 403
        c.DefaultRequestHeaders.UserAgent.ParseAdd("nanoMIDIPlayer");
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        c.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return c;
    }

    public static readonly string CurrentVersion = ReadCurrentVersion();

    static string ReadCurrentVersion() {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    // "v1.0.1" -> Version(1,0,1); ungueltig -> null
    static Version? ParseTag(string tag) {
        var s = tag.TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }

    // asset-name passend zum aktuellen RID -- feste vorgabe des release-tools
    static string? AssetNameForCurrentPlatform() {
        bool arm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsWindows()) return arm64 ? "nanoMIDIPlayer-win-arm64.exe" : "nanoMIDIPlayer-win-x64.exe";
        if (OperatingSystem.IsMacOS()) return arm64 ? "nanoMIDIPlayer-osx-arm64.zip" : "nanoMIDIPlayer-osx-x64.zip";
        return null; // linux etc. -- nicht unterstuetzt
    }

    // prueft auf ein neueres release. wirft nie -- bei jedem problem null + log.
    public static async Task<UpdateInfo?> CheckAsync() {
        try {
            GhRelease? best;
            if (Config.Data.updater.prerelease) {
                var json = await Http.GetStringAsync(ApiAll);
                var all = JsonSerializer.Deserialize<List<GhRelease>>(json) ?? new();
                best = all.Where(r => !r.Draft && ParseTag(r.TagName) != null)
                           .OrderByDescending(r => ParseTag(r.TagName))
                           .FirstOrDefault();
            } else {
                var json = await Http.GetStringAsync(ApiLatest);
                best = JsonSerializer.Deserialize<GhRelease>(json);
            }

            if (best == null) { Log("kein release gefunden"); return null; }
            var remote = ParseTag(best.TagName);
            if (remote == null) { Log($"ungültiger tag: {best.TagName}"); return null; }

            var current = ParseTag(CurrentVersion) ?? new Version(0, 0, 0);
            if (remote <= current) { Log("kein update verfügbar"); return null; }

            var assetName = AssetNameForCurrentPlatform();
            if (assetName == null) { Log("plattform wird nicht unterstützt"); return null; }

            var asset = best.Assets.FirstOrDefault(a => a.Name == assetName);
            if (asset == null) { Log($"kein passendes asset ({assetName}) im release {best.TagName}"); return null; }

            Log($"v{remote.Major}.{remote.Minor}.{remote.Build} verfügbar");
            return new UpdateInfo {
                Version = $"{remote.Major}.{remote.Minor}.{remote.Build}",
                TagName = best.TagName,
                AssetUrl = asset.Url,
                AssetName = asset.Name,
                ReleaseUrl = best.HtmlUrl,
                Prerelease = best.Prerelease
            };
        } catch (Exception ex) {
            // kein internet / api down / rate-limit / kaputtes json -> einfach kein update
            Log($"update-check fehlgeschlagen: {ex.Message}");
            return null;
        }
    }

    // beim start aufrufen: prueft (falls autoCheck) und installiert (falls autoInstall) im hintergrund.
    // schluckt alle fehler -- die app muss auch ohne internet normal starten.
    public static async Task StartupCheckAsync() {
        if (!Config.Data.updater.autoCheck) return;
        try {
            var info = await CheckAsync();
            if (info == null) return;
            if (Config.Data.updater.autoInstall)
                await DownloadAndInstallAsync(info, null);
        } catch (Exception ex) {
            Log($"update-check fehlgeschlagen: {ex.Message}");
        }
    }

    // leftover von einem vorherigen swap wegraeumen (kann beim vorherigen start noch gesperrt gewesen sein)
    public static void CleanupOldExe() {
        if (!OperatingSystem.IsWindows()) return;
        try {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var old = Path.Combine(Path.GetDirectoryName(exe)!, "nanoMIDIPlayer.old.exe");
            if (File.Exists(old)) File.Delete(old);
        } catch { /* evtl. noch gesperrt -- naechstes mal wieder versuchen */ }
    }

    public static async Task<bool> DownloadAndInstallAsync(UpdateInfo info, IProgress<double>? progress) {
        try {
            if (OperatingSystem.IsWindows()) return await InstallWindows(info, progress);
            if (OperatingSystem.IsMacOS()) {
                // .app-bundle-austausch waehrend die app laeuft ist nicht trivial (codesign/quarantine/
                // laufender prozess im bundle) -- bewusst nicht implementiert, nur hinweis in der UI.
                Log($"neue version {info.Version} verfügbar -- bitte manuell laden: {info.ReleaseUrl}");
                return false;
            }
            Log("auto-update auf dieser plattform nicht unterstützt");
            return false;
        } catch (Exception ex) {
            Log($"update fehlgeschlagen: {ex.Message}");
            return false;
        }
    }

    static void TryDelete(string path) {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    static async Task<bool> InstallWindows(UpdateInfo info, IProgress<double>? progress) {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) { Log("ProcessPath leer -- update nicht moeglich"); return false; }

        var dir = Path.GetDirectoryName(exe)!;
        var newPath = Path.Combine(dir, "nanoMIDIPlayer.new.exe");
        var oldPath = Path.Combine(dir, "nanoMIDIPlayer.old.exe");

        // 1) download in eine temp-datei im selben verzeichnis (= selbes volume fuer den move).
        //    erst wenn der download komplett durch ist, ruehren wir die echte exe an.
        try {
            TryDelete(newPath);
            using var resp = await Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;
            await using (var src = await resp.Content.ReadAsStreamAsync())
            await using (var dst = new FileStream(newPath, FileMode.Create, FileAccess.Write)) {
                var buf = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buf)) > 0) {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    read += n;
                    if (total > 0) progress?.Report((double)read / total);
                }
            }
        } catch (Exception ex) {
            Log($"download fehlgeschlagen: {ex.Message}");
            TryDelete(newPath);
            return false;
        }

        // 2) laufende exe umbenennen (windows erlaubt das, auch waehrend sie laeuft)
        try {
            TryDelete(oldPath); // stale .old von einem frueheren update
            File.Move(exe, oldPath);
        } catch (Exception ex) {
            Log($"konnte laufende exe nicht umbenennen: {ex.Message}");
            TryDelete(newPath);
            return false;
        }

        // 3) neue version an die urspruengliche stelle
        try {
            File.Move(newPath, exe);
        } catch (Exception ex) {
            Log($"konnte neue version nicht einsetzen: {ex.Message}");
            // rollback: alte exe zurueck an ihren platz
            try { if (!File.Exists(exe)) File.Move(oldPath, exe); } catch (Exception rb) { Log($"rollback fehlgeschlagen: {rb.Message}"); }
            TryDelete(newPath);
            return false;
        }

        // 4) neue version starten
        try {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir });
        } catch (Exception ex) {
            Log($"konnte neue version nicht starten: {ex.Message}");
            // rollback: neue (offenbar kaputte) exe raus, alte zurueck -- app bleibt lauffaehig
            try {
                File.Delete(exe);
                File.Move(oldPath, exe);
            } catch (Exception rb) {
                Log($"rollback fehlgeschlagen: {rb.Message}");
            }
            return false;
        }

        Log($"update auf {info.Version} installiert, starte neu...");
        OnRestartNeeded?.Invoke();
        return true;
    }
}
