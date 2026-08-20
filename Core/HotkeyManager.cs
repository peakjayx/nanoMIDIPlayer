using nanoMIDIPlayer.Core.Platform;

namespace nanoMIDIPlayer.Core;

// globale hotkeys, plattformneutral.
// erst alle Register(...) aufrufen, dann Start().
public class HotkeyManager : IDisposable {
    readonly IHotkeyBackend backend = PlatformFactory.Hotkeys();

    public void Register(string key, Action onPress) => backend.Register(key, onPress);
    public void Start() => backend.Start();
    public void Dispose() => backend.Dispose();
}
