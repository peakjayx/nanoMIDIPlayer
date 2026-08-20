using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using nanoMIDIPlayer.Core;

namespace nanoMIDIPlayer.UI;

public partial class InfoView : UserControl {
    bool init = true;
    UpdateInfo? pending; // zuletzt gefundenes, noch nicht installiertes update

    public InfoView() {
        InitializeComponent();

        string os = OperatingSystem.IsWindows() ? "Windows"
                  : OperatingSystem.IsMacOS() ? "macOS"
                  : "nicht unterstuetzt";
        PlatformLine.Text = $"C# / Avalonia · {os} · v{Updater.CurrentVersion}";
        ConfigLine.Text = Config.ConfigPath;

        if (OperatingSystem.IsMacOS()) {
            PermissionBlock.IsVisible = true;
            PermissionBlock.Text =
                "macOS braucht das Recht \"Bedienungshilfen\", sonst kann die App weder "
                + "Tasten senden noch die F1–F5 Hotkeys empfangen:\n"
                + "Systemeinstellungen > Datenschutz & Sicherheit > Bedienungshilfen > "
                + "nanoMIDIPlayer aktivieren, danach die App neu starten.\n\n"
                + "Damit F1–F5 als Hotkeys ankommen, muss in Systemeinstellungen > Tastatur "
                + "die Option \"F1, F2 usw. als Standardfunktionstasten verwenden\" aktiv sein "
                + "(sonst mit fn gedrückt bedienen).";
        }

        var u = Config.Data.updater;
        AutoCheckBox.IsChecked = u.autoCheck;
        AutoInstallBox.IsChecked = u.autoInstall;
        PrereleaseBox.IsChecked = u.prerelease;
        UpdateStatusLine.Text = "status: bereit";
        // macOS: auto-update nicht implementiert -- install-button bleibt sinnlos, weglassen
        if (!OperatingSystem.IsWindows())
            UpdateStatusLine.Text += " (auto-install nur unter windows)";
        init = false;

        // updater-meldungen (check-ergebnis, download-/swap-fehler, ...) in die statuszeile spiegeln
        Updater.OnLog += msg => Dispatcher.UIThread.Post(() => UpdateStatusLine.Text = msg);
    }

    void OnUpdaterOptionChanged(object? s, RoutedEventArgs e) {
        if (init) return;
        var u = Config.Data.updater;
        u.autoCheck = AutoCheckBox.IsChecked == true;
        u.autoInstall = AutoInstallBox.IsChecked == true;
        u.prerelease = PrereleaseBox.IsChecked == true;
        Config.Save();
    }

    async void OnCheckUpdate(object? s, RoutedEventArgs e) {
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.IsVisible = false;
        pending = null;
        UpdateStatusLine.Text = "suche…";

        var info = await Updater.CheckAsync();

        CheckUpdateButton.IsEnabled = true;
        if (info == null) return; // Updater.OnLog hat die statuszeile schon gesetzt ("kein update verfügbar" / fehler)

        pending = info;
        InstallUpdateButton.IsVisible = true;
    }

    async void OnInstallUpdate(object? s, RoutedEventArgs e) {
        if (pending == null) return;

        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateProgress.IsVisible = true;
        UpdateProgress.Value = 0;

        var progress = new Progress<double>(p => UpdateProgress.Value = p);
        var ok = await Updater.DownloadAndInstallAsync(pending, progress);

        if (!ok) {
            // Updater.OnLog hat den grund schon in die statuszeile geschrieben
            UpdateProgress.IsVisible = false;
            CheckUpdateButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
        }
        // bei erfolg feuert Updater.OnRestartNeeded -> MainWindow schliesst sich, hier nichts mehr zu tun
    }
}
