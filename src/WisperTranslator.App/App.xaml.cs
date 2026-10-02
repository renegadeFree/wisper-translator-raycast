using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WisperTranslator.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplySystemTheme();
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.Invoke(ApplySystemTheme);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // Spegnimento o disconnessione: il programma deve chiudersi davvero, non nascondersi.
        SessionEnding += (_, _) =>
        {
            window.ForceExit();
            Shutdown();
        };

        // Avvio immediato utile ai test automatici e all'avvio con Windows.
        if (e.Args.Contains("--autostart", StringComparer.OrdinalIgnoreCase))
        {
            _ = window.ToggleSessionAsync();
        }

        if (e.Args.Contains("--overlay", StringComparer.OrdinalIgnoreCase))
        {
            window.ShowOverlay();
        }

        // Collegamento "Impostazioni e sessioni" creato dall'installer.
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            window.OpenSettings(0);
        }
    }

    /// <summary>Segue il tema chiaro/scuro di Windows (requisito RF-09).</summary>
    private static void ApplySystemTheme() =>
        ApplicationThemeManager.Apply(
            ApplicationThemeManager.IsMatchedDark() ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            updateAccent: true);
}
