using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace nanoMIDIPlayer.Core;

// config models
public class CustomHold {
    public bool enabled { get; set; } = false;
    public double noteLength { get; set; } = 0.1;
}

public class RandomFail {
    public bool enabled { get; set; } = false;
    public double speed { get; set; } = 5.0;
    public double transpose { get; set; } = 5.0;
}

public class Map88 {
    public Dictionary<string, string> lowNotes { get; set; } = new();
    public Dictionary<string, string> highNotes { get; set; } = new();
}

public class PianoMap {
    [JsonPropertyName("88keyMap")] public Map88 keyMap88 { get; set; } = new();
    [JsonPropertyName("61keyMap")] public Dictionary<string, string> keyMap61 { get; set; } = new();
    public Dictionary<string, string> velocityMap { get; set; } = new();
}

// chord-erkennung: noten die praktisch gleichzeitig kommen werden
// um spreadMs versetzt gespielt (menschlicher anschlag statt block)
public class ChordOffset {
    public bool enabled { get; set; } = false;
    public double spreadMs { get; set; } = 0;        // versatz pro note innerhalb eines akkords
    public double detectWindowMs { get; set; } = 15; // events innerhalb dieses fensters = ein akkord
    public bool applyToRelease { get; set; } = true; // auch beim loslassen versetzen
    public bool randomOrder { get; set; } = false;   // reihenfolge im akkord zufaellig
}

public class PlayerConfig {
    public string currentFile { get; set; } = "";
    public List<string> midiList { get; set; } = new();
    public string sendMode { get; set; } = "scancode"; // scancode | virtualkey | unicode
    public bool loopSong { get; set; } = false;
    public bool releaseOnPause { get; set; } = true;
    public bool velocity { get; set; } = false;
    public bool sustain { get; set; } = false;
    public bool noDoubles { get; set; } = true;
    public bool swapYZ { get; set; } = false;
    [JsonPropertyName("88Keys")] public bool keys88 { get; set; } = true;
    public int sustainCutoff { get; set; } = 63;
    public int fingerLimit { get; set; } = 11;
    public int pitchOffset { get; set; } = 0;
    public int transposeOffset { get; set; } = 0;
    public CustomHold customHoldLength { get; set; } = new();
    public RandomFail randomFail { get; set; } = new();
    public ChordOffset chordOffset { get; set; } = new();
    public PianoMap pianoMap { get; set; } = new();
}

public class Hotkeys {
    public string play { get; set; } = "f1";
    public string pause { get; set; } = "f2";
    public string stop { get; set; } = "f3";
    public string speedup { get; set; } = "f4";
    public string slowdown { get; set; } = "f5";
}

public class AppUI {
    public bool topmost { get; set; } = false;
    public bool timestamp { get; set; } = true;
}

public class UpdaterConfig {
    public bool autoCheck { get; set; } = true;    // beim start nach updates suchen
    public bool autoInstall { get; set; } = true;  // gefundenes update ohne rueckfrage einspielen
    public bool prerelease { get; set; } = false;  // auch pre-releases beruecksichtigen
}

public class AppConfig {
    public PlayerConfig midiPlayer { get; set; } = new();
    public Hotkeys hotkeys { get; set; } = new();
    public AppUI appUI { get; set; } = new();
    public UpdaterConfig updater { get; set; } = new();
}

// config lade/speicher logik
public static class Config {
    // auf unix liefert MyDocuments $HOME (nicht ~/Documents) -> mac-pfad explizit bauen,
    // damit die config dort liegt wo das python-original sie erwartet
    static readonly string BaseDir = Path.Combine(DocumentsDir(), "nanoMIDIPlayer");
    public static readonly string ConfigPath = Path.Combine(BaseDir, "config.json");

    static string DocumentsDir() {
        if (OperatingSystem.IsWindows())
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) home = Environment.CurrentDirectory;
        return Path.Combine(home, "Documents");
    }

    static readonly JsonSerializerOptions Opts = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static AppConfig Data { get; private set; } = new();

    public static void Load() {
        Directory.CreateDirectory(BaseDir);
        AppConfig def = LoadEmbeddedDefault();

        if (File.Exists(ConfigPath)) {
            try {
                var user = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Opts);
                Data = user ?? def;
            } catch {
                Data = def;
            }
        } else {
            Data = def;
        }

        FillDefaults(Data, def);
        Save();
    }

    // fehlende maps ersetzen
    static void FillDefaults(AppConfig d, AppConfig def) {
        d.midiPlayer ??= def.midiPlayer;
        d.hotkeys ??= def.hotkeys;
        d.appUI ??= def.appUI;
        d.updater ??= def.updater;
        d.midiPlayer.chordOffset ??= def.midiPlayer.chordOffset;
        d.midiPlayer.pianoMap ??= def.midiPlayer.pianoMap;
        if (d.midiPlayer.pianoMap.keyMap61.Count == 0)
            d.midiPlayer.pianoMap.keyMap61 = def.midiPlayer.pianoMap.keyMap61;
        if (d.midiPlayer.pianoMap.velocityMap.Count == 0)
            d.midiPlayer.pianoMap.velocityMap = def.midiPlayer.pianoMap.velocityMap;
        if (d.midiPlayer.pianoMap.keyMap88.lowNotes.Count == 0)
            d.midiPlayer.pianoMap.keyMap88 = def.midiPlayer.pianoMap.keyMap88;
    }

    static AppConfig LoadEmbeddedDefault() {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("defaultConfig.json"));
        if (name != null) {
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            var cfg = JsonSerializer.Deserialize<AppConfig>(r.ReadToEnd(), Opts);
            if (cfg != null) return cfg;
        }
        return new AppConfig();
    }

    public static void Save() {
        try {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Data, Opts));
        } catch { }
    }
}
