using System.IO;
using System.Windows;
using Microsoft.Win32;
using WisperTranslator.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WisperTranslator.App;

public partial class App : Application
{
    public App()
    {
        // Diagnosi: se qualcosa sfugge, resta una traccia su file invece di far sparire l'app.
        DispatcherUnhandledException += (_, args) =>
        {
            LogFatal("Dispatcher", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogFatal("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogFatal("Task", args.Exception);
            args.SetObserved();
        };
    }

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
            _ = SafeAsync(window.ToggleSessionAsync);
        }

        // Collaudo dello stop: --autostop <secondi> ferma la sessione e chiude da sola.
        if (ArgumentValue(e.Args, "--autostop") is { } stopAfter && int.TryParse(stopAfter, out var seconds))
        {
            _ = SafeAsync(() => window.AutostopAsync(seconds));
        }

        if (e.Args.Contains("--overlay", StringComparer.OrdinalIgnoreCase))
        {
            window.ShowOverlay();
        }

        // Collegamento "Impostazioni e sessioni" creato dall'installer.
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)
            || e.Args.Any(arg => arg.StartsWith("--settings=", StringComparison.OrdinalIgnoreCase)))
        {
            // --settings=<n> apre direttamente una scheda (utile per supporto e screenshot):
            // 0 Aspetto, 1 Prestazioni, 2 Conversazione, 3 Barra, 4 Modelli, 5 Template, 6 Storico, 7 IA.
            var index = 0;
            var inline = e.Args.FirstOrDefault(arg => arg.StartsWith("--settings=", StringComparison.OrdinalIgnoreCase));
            if (inline is not null && int.TryParse(inline["--settings=".Length..], out var parsed))
            {
                index = parsed;
            }
            else if (ArgumentValue(e.Args, "--settings") is { } value && int.TryParse(value, out var parsedNext))
            {
                index = parsedNext;
            }

            window.OpenSettings(index);
        }
    }

    /// <summary>Segue il tema chiaro/scuro di Windows (requisito RF-09).</summary>
    private static void ApplySystemTheme() =>
        ApplicationThemeManager.Apply(
            ApplicationThemeManager.IsMatchedDark() ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            updateAccent: true);

    private static void LogFatal(string source, Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var path = Path.Combine(AppPaths.EnsureSubdirectory("logs"), "errori.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] {exception}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // se non si riesce a scrivere il log, non si peggiora la situazione
        }
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            LogFatal("Background", exception);
        }
    }
}
