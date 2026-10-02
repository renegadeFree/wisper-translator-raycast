using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WisperTranslator.App.Interop;
using WisperTranslator.App.Windows;
using WisperTranslator.Core;
using WisperTranslator.Core.History;
using WisperTranslator.Core.Session;
using WisperTranslator.Core.Settings;
using Wpf.Ui.Controls;

namespace WisperTranslator.App;

public partial class MainWindow : FluentWindow
{
    private const int HotkeyShowHide = 1;
    private const int HotkeySystem = 2;
    private const int HotkeyMicrophone = 3;
    private const int HotkeySwap = 4;
    private const int HotkeyOverlay = 5;
    private const int WmHotkey = 0x0312;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ObservableCollection<Cue> _cues = [];
    private TranscriptionSession? _session;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _tray;
    private HwndSource? _source;
    private DispatcherTimer? _historyTimer;
    private DispatcherTimer? _levelTimer;
    private bool _starting;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();

        CueList.ItemsSource = _cues;
        DirectionBox.ItemsSource = new[] { "IT → EN", "EN → IT" };
        DirectionBox.SelectedIndex = _settings.SourceLanguage == "it" ? 0 : 1;
        SystemToggle.IsChecked = _settings.SystemAudio;
        MicrophoneToggle.IsChecked = _settings.Microphone;
        Topmost = _settings.Topmost;
        FontSize = _settings.FontSize;
        Opacity = _settings.Opacity;

        RestorePlacement();
        CreateTrayIcon();

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    internal AppSettings Settings => _settings;

    internal IReadOnlyList<Cue> Cues => _cues;

    private void RestorePlacement()
    {
        Width = Math.Max(MinWidth, _settings.WindowWidth);
        Height = Math.Max(MinHeight, _settings.WindowHeight);

        var area = SystemParameters.WorkArea;
        Left = _settings.WindowLeft is { } left && left > area.Left - Width && left < area.Right
            ? left
            : area.Right - Width - 24;
        Top = _settings.WindowTop is { } top && top > area.Top - Height && top < area.Bottom
            ? top
            : area.Bottom - Height - 24;
    }

    /// <summary>Icona nell'area di notifica: il programma resta raggiungibile anche in background.</summary>
    private void CreateTrayIcon()
    {
        _tray = new TrayIcon();
        _tray.VisibilityToggleRequested += ToggleWindowVisibility;
        _tray.SessionToggleRequested += () => _ = ToggleSessionAsync();
        _tray.SystemAudioToggleRequested += () => Dispatcher.Invoke(() => SystemToggle.IsChecked = SystemToggle.IsChecked != true);
        _tray.MicrophoneToggleRequested += () => Dispatcher.Invoke(() => MicrophoneToggle.IsChecked = MicrophoneToggle.IsChecked != true);
        _tray.DirectionChangeRequested += language => Dispatcher.Invoke(() =>
        {
            if (DirectionBox.SelectedIndex != (language == "it" ? 0 : 1))
            {
                DirectionBox.SelectedIndex = language == "it" ? 0 : 1;
            }
        });
        _tray.OverlayToggleRequested += () => Dispatcher.Invoke(ToggleOverlay);
        _tray.HistoryRequested += () => Dispatcher.Invoke(() => OpenSettings(2));
        _tray.ModelsRequested += () => Dispatcher.Invoke(() => OpenSettings(1));
        _tray.SettingsRequested += () => Dispatcher.Invoke(() => OpenSettings(0));
        _tray.OpenDataFolderRequested += OpenDataFolder;
        _tray.AboutRequested += ShowAbout;
        _tray.ExitRequested += ExitApplication;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);

        RegisterHotkeys();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncMenus();
        UpdateTray("Pronto");

        if (_settings.OverlayEnabled)
        {
            ShowOverlay();
        }

        if (_settings.RetentionDays > 0)
        {
            PurgeHistory();
            _historyTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _historyTimer.Tick += (_, _) => PurgeHistory();
            _historyTimer.Start();
        }

        _levelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _levelTimer.Tick += (_, _) => UpdateLevel();
        _levelTimer.Start();

        if (_settings.StartWithWindows != StartupRegistration.IsEnabled())
        {
            StartupRegistration.Set(_settings.StartWithWindows);
        }
    }

    private void UpdateLevel()
    {
        var levels = _session?.Levels() ?? [];
        if (levels.Count == 0)
        {
            LevelText.Text = string.Empty;
            return;
        }

        var peak = levels.Where(level => level.Enabled).Select(level => level.Level).DefaultIfEmpty(0).Max();
        var bars = (int)Math.Round(Math.Clamp(peak * 12, 0, 6));
        LevelText.Text = new string('●', bars) + new string('○', 6 - bars);
    }

    private void PurgeHistory()
    {
        try
        {
            using var store = new SessionStore(retentionDays: Math.Max(1, _settings.RetentionDays));
            store.PurgeExpired();
        }
        catch (Exception)
        {
            // lo storico non deve impedire l'avvio
        }
    }

    private void RegisterHotkeys()
    {
        var modifiers = Win32.ModControl | Win32.ModAlt;
        var failures = new List<string>();

        if (!Win32.TryRegisterHotKey(this, HotkeyShowHide, modifiers, 0x57))
        {
            failures.Add("Ctrl+Alt+W");
        }

        if (!Win32.TryRegisterHotKey(this, HotkeySystem, modifiers, 0x53))
        {
            failures.Add("Ctrl+Alt+S");
        }

        if (!Win32.TryRegisterHotKey(this, HotkeyMicrophone, modifiers, 0x4E))
        {
            failures.Add("Ctrl+Alt+N");
        }

        if (!Win32.TryRegisterHotKey(this, HotkeySwap, modifiers, 0x4C))
        {
            failures.Add("Ctrl+Alt+L");
        }

        if (!Win32.TryRegisterHotKey(this, HotkeyOverlay, modifiers, 0x4F))
        {
            failures.Add("Ctrl+Alt+O");
        }

        if (failures.Count > 0)
        {
            SetStatus($"Hotkey non disponibili (già usate da un altro programma): {string.Join(", ", failures)}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return IntPtr.Zero;
        }

        switch (wParam.ToInt32())
        {
            case HotkeyShowHide:
                ToggleWindowVisibility();
                break;

            case HotkeySystem:
                SystemToggle.IsChecked = SystemToggle.IsChecked != true;
                break;

            case HotkeyMicrophone:
                MicrophoneToggle.IsChecked = MicrophoneToggle.IsChecked != true;
                break;

            case HotkeySwap:
                DirectionBox.SelectedIndex = DirectionBox.SelectedIndex == 0 ? 1 : 0;
                break;

            case HotkeyOverlay:
                ToggleOverlay();
                break;
        }

        handled = true;
        return IntPtr.Zero;
    }

    private void ToggleWindowVisibility()
    {
        Dispatcher.Invoke(() =>
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                Show();
                Activate();
            }

            UpdateTray(StatusText.Text);
        });
    }

    private async void OnStartStopClicked(object sender, RoutedEventArgs e) => await ToggleSessionAsync();

    internal async Task ToggleSessionAsync()
    {
        if (_starting)
        {
            return;
        }

        if (_session is { IsRunning: true })
        {
            await StopSessionAsync();
            return;
        }

        _starting = true;
        StartButton.IsEnabled = false;
        StartButton.Content = "Avvio...";
        _cues.Clear();

        _settings.SystemAudio = SystemToggle.IsChecked == true;
        _settings.Microphone = MicrophoneToggle.IsChecked == true;
        _settings.SourceLanguage = DirectionBox.SelectedIndex == 0 ? "it" : "en";

        var session = new TranscriptionSession(_settings.ToSessionOptions());
        session.CueUpdated += OnCueUpdated;
        session.StatusChanged += message => Dispatcher.Invoke(() =>
        {
            SetStatus(message);
            UpdateTray(message);
        });
        _session = session;

        try
        {
            await session.StartAsync();
            StartButton.Content = "Ferma";
            StartButton.Icon = new SymbolIcon(SymbolRegular.Stop24);
            MenuStartStop.Header = "Ferma";
            MenuStartStop.Icon = new SymbolIcon(SymbolRegular.Stop24);
            SyncMenus();
            UpdateTray("In ascolto");
            _tray?.ShowBalloon("Wisper Translator", "Sessione avviata: i sottotitoli sono attivi.");
        }
        catch (Exception exception)
        {
            SetStatus($"Errore: {exception.Message}");
            _tray?.ShowBalloon("Wisper Translator", exception.Message, warning: true);
            await session.DisposeAsync();
            _session = null;
            StartButton.Content = "Avvia";
        }
        finally
        {
            StartButton.IsEnabled = true;
            _starting = false;
        }
    }

    private async Task StopSessionAsync()
    {
        if (_session is null)
        {
            return;
        }

        var session = _session;
        _session = null;
        StartButton.IsEnabled = false;
        await session.DisposeAsync();
        StartButton.Content = "Avvia";
        StartButton.Icon = new SymbolIcon(SymbolRegular.Play24);
        MenuStartStop.Header = "Avvia";
        MenuStartStop.Icon = new SymbolIcon(SymbolRegular.Play24);
        StartButton.IsEnabled = true;
        SetStatus("In pausa");
        SyncMenus();
        UpdateTray("In pausa");
    }

    private void OnCueUpdated(Cue cue)
    {
        Dispatcher.Invoke(() =>
        {
            var index = -1;
            for (var i = 0; i < _cues.Count; i++)
            {
                if (_cues[i].Id == cue.Id)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                _cues[index] = cue;
            }
            else
            {
                _cues.Insert(0, cue);
            }

            while (_cues.Count > Math.Max(1, _settings.MaxCues))
            {
                _cues.RemoveAt(_cues.Count - 1);
            }

            _overlay?.Refresh();
        });
    }

    private void OnSystemToggled(object sender, RoutedEventArgs e)
    {
        _settings.SystemAudio = SystemToggle.IsChecked == true;
        _session?.SetSystemAudio(_settings.SystemAudio);
        SyncMenus();
        UpdateTray(StatusText.Text);
    }

    private void OnMicrophoneToggled(object sender, RoutedEventArgs e)
    {
        _settings.Microphone = MicrophoneToggle.IsChecked == true;
        _session?.SetMicrophone(_settings.Microphone);
        SyncMenus();
        UpdateTray(StatusText.Text);
    }

    private void OnDirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var source = DirectionBox.SelectedIndex == 0 ? "it" : "en";
        _settings.SourceLanguage = source;
        _session?.SetDirection(source, source == "it" ? "en" : "it");
        SyncMenus();
        UpdateTray(StatusText.Text);
    }

    private void OnMenuSystemClicked(object sender, RoutedEventArgs e)
    {
        SystemToggle.IsChecked = MenuSystem.IsChecked;
    }

    private void OnMenuMicrophoneClicked(object sender, RoutedEventArgs e)
    {
        MicrophoneToggle.IsChecked = MenuMicrophone.IsChecked;
    }

    private void OnMenuItEnClicked(object sender, RoutedEventArgs e)
    {
        DirectionBox.SelectedIndex = 0;
    }

    private void OnMenuEnItClicked(object sender, RoutedEventArgs e)
    {
        DirectionBox.SelectedIndex = 1;
    }

    private void OnHideToTrayClicked(object sender, RoutedEventArgs e)
    {
        Hide();
        UpdateTray(StatusText.Text);
        _tray?.ShowBalloon("Wisper Translator", "Il programma resta attivo nella barra delle applicazioni.");
    }

    private void OnExitClicked(object sender, RoutedEventArgs e) => ExitApplication();

    private void ExitApplication()
    {
        _exiting = true;
        Dispatcher.Invoke(Close);
    }

    /// <summary>
    /// Chiusura richiesta dal sistema (spegnimento, disinstallazione, aggiornamento):
    /// qui non si resta nella barra delle applicazioni, altrimenti i file restano bloccati.
    /// </summary>
    internal void ForceExit() => ExitApplication();

    private void OnCopyOriginalClicked(object sender, RoutedEventArgs e) => CopyCue(sender, original: true);

    private void OnCopyTranslationClicked(object sender, RoutedEventArgs e) => CopyCue(sender, original: false);

    private static void CopyCue(object sender, bool original)
    {
        if ((sender as FrameworkElement)?.DataContext is not Cue cue)
        {
            return;
        }

        var text = original ? cue.Original : cue.Translation;
        if (!string.IsNullOrWhiteSpace(text))
        {
            Clipboard.SetText(text);
        }
    }

    private void OnOverlayClicked(object sender, RoutedEventArgs e) => ToggleOverlay();

    private void ToggleOverlay()
    {
        if (_overlay is { IsVisible: true })
        {
            _overlay.Hide();
            _settings.OverlayEnabled = false;
        }
        else
        {
            ShowOverlay();
        }

        SyncMenus();
        UpdateTray(StatusText.Text);
        _settings.Save();
    }

    internal void ShowOverlay()
    {
        if (_overlay is null)
        {
            _overlay = new OverlayWindow(_cues, _settings);
            _overlay.Closed += (_, _) => _overlay = null;
        }

        _overlay.Show();
        _overlay.Refresh();
        _settings.OverlayEnabled = true;
        SyncMenus();
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => OpenSettings(0);

    private void OnHistoryMenuClicked(object sender, RoutedEventArgs e) => OpenSettings(2);

    private void OnModelsMenuClicked(object sender, RoutedEventArgs e) => OpenSettings(1);

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        UpdateTray(message);
    }

    private void UpdateTray(string status) =>
        _tray?.Update(
            IsVisible,
            _session is { IsRunning: true },
            SystemToggle.IsChecked == true,
            MicrophoneToggle.IsChecked == true,
            _overlay is { IsVisible: true },
            DirectionBox.SelectedIndex == 0 ? "it" : "en",
            status);

    /// <summary>Allinea i segni di spunta del menu allo stato reale.</summary>
    private void SyncMenus()
    {
        MenuSystem.IsChecked = SystemToggle.IsChecked == true;
        MenuMicrophone.IsChecked = MicrophoneToggle.IsChecked == true;
        MenuItEn.IsChecked = DirectionBox.SelectedIndex == 0;
        MenuEnIt.IsChecked = DirectionBox.SelectedIndex == 1;
        MenuOverlay.IsChecked = _overlay is { IsVisible: true };
        MenuStartStop.Header = _session is { IsRunning: true } ? "Ferma" : "Avvia";
    }

    /// <summary>Apre le impostazioni su una scheda specifica (0 aspetto, 1 modelli, 2 storico, 3 IA).</summary>
    internal void OpenSettings(int tab)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.SelectTab(tab);
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, ApplySettings, tab);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    /// <summary>Applica le preferenze cambiate nella finestra impostazioni.</summary>
    internal void ApplySettings()
    {
        FontSize = _settings.FontSize;
        Opacity = _settings.Opacity;
        Topmost = _settings.Topmost;

        while (_cues.Count > Math.Max(1, _settings.MaxCues))
        {
            _cues.RemoveAt(_cues.Count - 1);
        }

        if (_settings.StartWithWindows != StartupRegistration.IsEnabled())
        {
            StartupRegistration.Set(_settings.StartWithWindows);
        }

        _overlay?.Refresh();
        _settings.Save();
        SyncMenus();
        UpdateTray(StatusText.Text);
    }

    private static void OpenDataFolder()
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo { FileName = AppPaths.Root, UseShellExecute = true });
    }

    private void ShowAbout() =>
        System.Windows.MessageBox.Show(
            this,
            "Wisper Translator\n\nTrascrizione e traduzione in tempo reale, in locale.\n"
            + $"Versione {typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0"}\n\n"
            + $"Cartella dati: {AppPaths.Root}\nLicenza MIT.",
            "Informazioni",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);

    private void OnOpenDataFolderClicked(object sender, RoutedEventArgs e) => OpenDataFolder();

    private void OnAboutClicked(object sender, RoutedEventArgs e) => ShowAbout();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Con "chiudi nella barra delle applicazioni" la X nasconde soltanto.
        if (!_exiting && _settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            UpdateTray(StatusText.Text);
            _tray?.ShowBalloon("Wisper Translator", "Continua a funzionare in background. Doppio clic sull'icona per riaprirlo.");
            return;
        }

        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
        _settings.Topmost = Topmost;
        _settings.Save();

        foreach (var id in new[] { HotkeyShowHide, HotkeySystem, HotkeyMicrophone, HotkeySwap, HotkeyOverlay })
        {
            Win32.UnregisterHotKey(this, id);
        }

        _source?.RemoveHook(WndProc);
        _levelTimer?.Stop();
        _historyTimer?.Stop();

        // Il server di traduzione è un processo figlio: va chiuso prima di uscire.
        _session?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(4));
        _session = null;

        _tray?.Dispose();
        Application.Current.Shutdown();
    }
}
