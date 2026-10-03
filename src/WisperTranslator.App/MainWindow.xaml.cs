using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WisperTranslator.App.Interop;
using WisperTranslator.App.Windows;
using WisperTranslator.Core;
using WisperTranslator.Core.Asr;
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
    private const int HotkeyBar = 6;
    private const int WmHotkey = 0x0312;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ObservableCollection<Cue> _cues = [];
    private TranscriptionSession? _session;
    private OverlayWindow? _overlay;
    private BarWindow? _bar;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _tray;
    private HwndSource? _source;
    private DispatcherTimer? _historyTimer;
    private DispatcherTimer? _levelTimer;
    private readonly DispatcherTimer _barRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(66) };
    private readonly Dictionary<int, Cue> _pendingCues = [];
    private readonly object _pendingCueGate = new();
    private int _cueRefreshScheduled;
    private bool _starting;
    private bool _stopping;
    private bool _exiting;
    private bool _shotMode;
    private bool _shotRunning;

    public MainWindow()
    {
        InitializeComponent();

        CueList.ItemsSource = _cues;
        DirectionBox.ItemsSource = new[] { "IT → EN", "EN → IT" };
        DirectionBox.SelectedIndex = _settings.SourceLanguage == "it" ? 0 : 1;
        SystemToggle.IsChecked = _settings.SystemAudio;
        MicrophoneToggle.IsChecked = _settings.Microphone;
        ConversationToggle.IsChecked = _settings.ConversationMode;
        TranslateToggle.IsChecked = _settings.Translate;
        Topmost = _settings.Topmost;
        FontSize = _settings.FontSize;
        Opacity = _settings.Opacity;

        RestorePlacement();
        CreateTrayIcon();

        _barRefreshTimer.Tick += (_, _) =>
        {
            _barRefreshTimer.Stop();
            FlushCueUpdates();
            RefreshBarNow();
        };

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    internal AppSettings Settings => _settings;

    internal IReadOnlyList<Cue> Cues => _cues;

    /// <summary>Vero quando la barra fluttuante è a schermo.</summary>
    internal bool IsBarVisible => _bar is { IsVisible: true };

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
        _tray.BarToggleRequested += () => Dispatcher.Invoke(ToggleBar);
        _tray.HistoryRequested += () => Dispatcher.Invoke(() => OpenSettings(SettingsTabs.Storico));
        _tray.ModelsRequested += () => Dispatcher.Invoke(() => OpenSettings(SettingsTabs.Modelli));
        _tray.SettingsRequested += () => Dispatcher.Invoke(() => OpenSettings(SettingsTabs.Aspetto));
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

        if (_settings.StartWithBar)
        {
            ShowBar();
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
        var peak = CurrentLevel();
        if (peak <= 0 && _session is null)
        {
            LevelText.Text = string.Empty;
            return;
        }

        var bars = (int)Math.Round(Math.Clamp(peak * 12, 0, 6));
        LevelText.Text = new string('●', bars) + new string('○', 6 - bars);
    }

    /// <summary>Livello audio più alto fra le sorgenti attive: alimenta la spia e l'equalizzatore.</summary>
    private float CurrentLevel() => _session?.CurrentLevel ?? 0;

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

        if (!Win32.TryRegisterHotKey(this, HotkeyBar, modifiers, 0x42))
        {
            failures.Add("Ctrl+Alt+B");
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

            case HotkeyBar:
                ToggleBar();
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

    private async void OnStartStopClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await ToggleSessionAsync();
        }
        catch (Exception exception)
        {
            // Un errore di sessione non deve mai chiudere l'applicazione (era il caso dello stop).
            SetStatus($"Errore: {exception.Message}");
        }
    }

    internal async Task ToggleSessionAsync()
    {
        if (_starting || _stopping)
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
        lock (_pendingCueGate) { _pendingCues.Clear(); }

        _settings.SystemAudio = SystemToggle.IsChecked == true;
        _settings.Microphone = MicrophoneToggle.IsChecked == true;
        _settings.SourceLanguage = DirectionBox.SelectedIndex == 0 ? "it" : "en";

        var session = new TranscriptionSession(_settings.ToSessionOptions());
        session.CueUpdated += cue =>
        {
            if (ReferenceEquals(_session, session)) OnCueUpdated(cue);
        };
        session.StatusChanged += message => Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_session, session) || _exiting) return;
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

        _stopping = true;
        var session = _session;
        _session = null;
        lock (_pendingCueGate) { _pendingCues.Clear(); }
        StartButton.IsEnabled = false;
        SetStatus("Arresto…");

        try
        {
            // Le decodifiche in corso vengono abbandonate: la UI non deve mai restare appesa.
            var cleanup = session.DisposeAsync().AsTask();
            if (await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(6))) == cleanup)
                await cleanup;
        }
        catch (Exception exception)
        {
            SetStatus($"Errore in arresto: {exception.Message}");
        }
        finally
        {
            StartButton.Content = "Avvia";
            StartButton.Icon = new SymbolIcon(SymbolRegular.Play24);
            MenuStartStop.Header = "Avvia";
            MenuStartStop.Icon = new SymbolIcon(SymbolRegular.Play24);
            StartButton.IsEnabled = true;
            SetStatus("In pausa");
            SyncMenus();
            UpdateTray("In pausa");
            _stopping = false;
        }
    }

    private void OnCueUpdated(Cue cue)
    {
        if (_exiting) return;
        lock (_pendingCueGate)
        {
            if (_pendingCues.TryGetValue(cue.Id, out var previous) && previous.IsFinal && !cue.IsFinal) return;
            _pendingCues[cue.Id] = cue;
        }

        // Il thread audio non aspetta la UI; ogni tick applica soltanto l'ultima versione.
        if (Interlocked.Exchange(ref _cueRefreshScheduled, 1) == 0)
            Dispatcher.BeginInvoke(() => _barRefreshTimer.Start(), DispatcherPriority.Background);
    }

    private void FlushCueUpdates()
    {
        Interlocked.Exchange(ref _cueRefreshScheduled, 0);
        Cue[] updates;
        lock (_pendingCueGate)
        {
            updates = _pendingCues.Values.OrderBy(cue => cue.Id).ToArray();
            _pendingCues.Clear();
        }

        foreach (var cue in updates)
        {
            var index = -1;
            for (var i = 0; i < _cues.Count; i++)
            {
                if (_cues[i].Id == cue.Id) { index = i; break; }
            }

            if (index >= 0)
            {
                if (!_cues[index].IsFinal || cue.IsFinal) _cues[index] = cue;
            }
            else _cues.Insert(0, cue);
        }

        while (_cues.Count > Math.Max(1, _settings.MaxCues)) _cues.RemoveAt(_cues.Count - 1);
        if (updates.Length > 0 && _overlay is { IsVisible: true }) _overlay.Refresh();
    }

    // --- Barra fluttuante ---

    private void ShowBar()
    {
        if (_bar is null)
        {
            try
            {
            _bar = new BarWindow(_settings);
            _bar.LevelProvider = () => _shotMode ? 0.62f : CurrentLevel();
            }
            catch (Exception exception)
            {
                // Un errore di XAML non deve sparire nel nulla: senza questo la barra
                // semplicemente non comparirebbe e sembrerebbe un problema di impostazioni.
                SetStatus($"Barra non disponibile: {exception.Message}");
                return;
            }

            _bar.StartStopRequested += () => _ = ToggleSessionAsync();
            _bar.SystemRequested += () => SystemToggle.IsChecked = SystemToggle.IsChecked != true;
            _bar.MicrophoneRequested += () => MicrophoneToggle.IsChecked = MicrophoneToggle.IsChecked != true;
            _bar.SwapRequested += () => DirectionBox.SelectedIndex = DirectionBox.SelectedIndex == 0 ? 1 : 0;
            _bar.TextModeRequested += CycleBarText;
            _bar.ConversationRequested += () => ConversationToggle.IsChecked = ConversationToggle.IsChecked != true;
            _bar.TranslateRequested += () => TranslateToggle.IsChecked = TranslateToggle.IsChecked != true;
            _bar.DiscreetRequested += ToggleDiscreet;
            _bar.OverlayRequested += ToggleOverlay;
            _bar.PanelRequested += () =>
            {
                Show();
                Activate();
            };
            _bar.SettingsRequested += () => OpenSettings(SettingsTabs.Aspetto);
            _bar.HideRequested += HideBar;
            _bar.Closed += (_, _) => _bar = null;
        }

        _bar.Show();
        _bar.ApplyLayout();
        RefreshBar();
        SyncMenus();
        if (!_shotMode)
        {
            _settings.Save();
        }
    }

    private void HideBar()
    {
        _bar?.Hide();
        SyncMenus();
        if (!_shotMode)
        {
            _settings.Save();
        }
    }

    private void ToggleBar()
    {
        if (_bar is { IsVisible: true })
        {
            HideBar();
        }
        else
        {
            ShowBar();
        }
    }

    private void OnBarToggleChanged(object sender, RoutedEventArgs e)
    {
        if (BarToggle.IsChecked == true)
        {
            ShowBar();
        }
        else
        {
            HideBar();
        }
    }

    /// <summary>Passa i dati alla barra: frasi e stato dei controlli in un colpo solo.</summary>
    private void RefreshBar()
    {
        if (_bar is not { IsVisible: true })
        {
            return;
        }

        // I parziali possono arrivare più volte al secondo: si disegna al massimo ~15 volte
        // al secondo, senza perdere l'ultimo stato.
        if (!_barRefreshTimer.IsEnabled)
        {
            _barRefreshTimer.Start();
        }
    }

    private void RefreshBarNow()
    {
        if (_bar is not { IsVisible: true })
        {
            return;
        }

        var running = _shotRunning || _session is { IsRunning: true };
        _bar.Sync(
            _cues,
            new BarState(
                running,
                SystemToggle.IsChecked == true,
                MicrophoneToggle.IsChecked == true,
                _overlay is { IsVisible: true },
                ConversationToggle.IsChecked == true,
                DirectionBox.SelectedIndex == 0 ? "it" : "en",
                StatusText.Text,
                SpeakerHint()));
    }

    /// <summary>Quante voci si sono sentite: è l'informazione che si vuole avere sotto mano.</summary>
    private string SpeakerHint()
    {
        if (ConversationToggle.IsChecked != true)
        {
            return string.Empty;
        }

        var speakers = _cues.Select(cue => cue.Speaker).Where(speaker => speaker != Speakers.Unknown).Distinct().Count();
        return speakers switch
        {
            0 => "una voce",
            1 => "1 voce",
            _ => $"{speakers} voci",
        };
    }

    private void CycleBarText()
    {
        _settings.BarText = _settings.BarText switch
        {
            BarTextMode.Entrambi => BarTextMode.Traduzione,
            BarTextMode.Traduzione => BarTextMode.Originale,
            _ => BarTextMode.Entrambi,
        };

        _settings.Save();
        SyncMenus();
        RefreshBar();
    }

    private void ToggleDiscreet()
    {
        _settings.BarDiscreet = !_settings.BarDiscreet;
        _settings.Save();
        RefreshBar();
        SetStatus(_settings.BarDiscreet
            ? "Modalità discreta: la barra lascia passare i clic (maniglia esclusa)."
            : "Modalità normale: la barra accetta i clic.");
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
        RefreshBar();
    }

    private void OnConversationToggled(object sender, RoutedEventArgs e)
    {
        var enabled = ConversationToggle.IsChecked == true;
        if (enabled && _session is { IsRunning: true })
        {
            SetStatus("Modalità conversazione: si applica alla prossima sessione.");
        }

        _settings.ConversationMode = enabled;
        _settings.Save();
        SyncMenus();
        UpdateTray(StatusText.Text);
        RefreshBar();
    }

    private void OnTranslateToggled(object sender, RoutedEventArgs e)
    {
        _settings.Translate = TranslateToggle.IsChecked == true;
        if (_session is { IsRunning: true })
        {
            SetStatus("Traduzione: si applica alla prossima sessione.");
        }

        _settings.Save();
        SyncMenus();
        RefreshBar();
    }

    private void OnMenuBarClicked(object sender, RoutedEventArgs e)
    {
        if (MenuBar.IsChecked)
        {
            ShowBar();
        }
        else
        {
            HideBar();
        }

        SyncMenus();
    }

    private void OnMenuConversationClicked(object sender, RoutedEventArgs e) =>
        ConversationToggle.IsChecked = MenuConversation.IsChecked;

    private void OnMenuTranslateClicked(object sender, RoutedEventArgs e) =>
        TranslateToggle.IsChecked = MenuTranslate.IsChecked;

    private void OnBarTextBothClicked(object sender, RoutedEventArgs e) => SetBarText(BarTextMode.Entrambi);

    private void OnBarTextOriginalClicked(object sender, RoutedEventArgs e) => SetBarText(BarTextMode.Originale);

    private void OnBarTextTranslationClicked(object sender, RoutedEventArgs e) => SetBarText(BarTextMode.Traduzione);

    private void SetBarText(BarTextMode mode)
    {
        _settings.BarText = mode;
        _settings.Save();
        SyncMenus();
        RefreshBar();
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

    /// <summary>
    /// Collaudo automatico dello stop: la sessione parte all'avvio e si ferma dopo N secondi,
    /// misurando il tempo di arresto. Serve a verificare che "Ferma" non chiuda il programma.
    /// </summary>
    internal async Task AutostopAsync(int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, seconds)));
        var watch = Stopwatch.StartNew();
        await ToggleSessionAsync();
        watch.Stop();

        try
        {
            var path = Path.Combine(Core.AppPaths.EnsureSubdirectory("logs"), "autostop.txt");
            File.AppendAllText(
                path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} stop in {watch.Elapsed.TotalMilliseconds:F0} ms · stato: {StatusText.Text}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // il collaudo non deve mai far fallire la chiusura
        }

        await Task.Delay(TimeSpan.FromMilliseconds(800));
        ExitApplication();
    }

    /// <summary>
    /// Screenshot per la documentazione: dati d'esempio, corpo della capsula opaco e cattura del
    /// solo rettangolo delle finestre. Nessun pixel dello schermo dell'utente finisce nel file.
    /// </summary>
    internal async Task ShotAsync(string directory)
    {
        _shotMode = true;
        Directory.CreateDirectory(directory);
        var background = System.Windows.Media.Color.FromRgb(0x1B, 0x1B, 0x22);

        // Barra in ascolto, con parlanti diversi e una frase ancora provvisoria.
        _shotRunning = true;
        _cues.Clear();
        foreach (var cue in BarSamples.Cues())
        {
            _cues.Add(cue);
        }

        ShowBar();
        if (_bar is null)
        {
            SetStatus("Screenshot non riuscito: la barra non è disponibile.");
            ExitApplication();
            return;
        }

        _bar.EnableShotMode();
        _bar.ApplyLayout();
        SetStatus(string.Empty);
        RefreshBar();
        await Task.Delay(900);
        ShotLog($"misure barra: {_bar.Describe()}");
        Capture(_bar.ShotTarget, Path.Combine(directory, "barra-ascolto.png"), background, BarRadius());

        // Barra a riposo: suggerimento al centro, nessuna frase.
        _shotRunning = false;
        _cues.Clear();
        SetStatus(string.Empty);
        RefreshBar();
        await Task.Delay(700);
        Capture(_bar.ShotTarget, Path.Combine(directory, "barra-riposo.png"), background, BarRadius());

        // Pannello esteso con le stesse frasi d'esempio.
        foreach (var cue in BarSamples.Cues())
        {
            _cues.Add(cue);
        }

        RefreshBar();
        Background = new System.Windows.Media.SolidColorBrush(background);
        Show();
        Width = 700;
        Height = 520;
        Left = SystemParameters.WorkArea.Left + 60;
        Top = SystemParameters.WorkArea.Top + 60;
        SetStatus("In ascolto");
        await Task.Delay(700);
        Capture(Content as FrameworkElement ?? this, Path.Combine(directory, "pannello.png"), background, 10);

        // Schede Conversazione e Barra.
        _settingsWindow = new SettingsWindow(_settings, () => { }, SettingsTabs.Barra) { Owner = this };
        _settingsWindow.Show();
        await Task.Delay(900);
        Capture(_settingsWindow.Content as FrameworkElement ?? _settingsWindow, Path.Combine(directory, "impostazioni-barra.png"), background, 10);
        _settingsWindow.SelectTab(SettingsTabs.Conversazione);
        await Task.Delay(700);
        Capture(_settingsWindow.Content as FrameworkElement ?? _settingsWindow, Path.Combine(directory, "impostazioni-conversazione.png"), background, 10);
        _settingsWindow.Close();

        await Task.Delay(300);
        ExitApplication();
    }

    /// <summary>
    /// Autotest della barra: verifica che la finestra abbia davvero una regione non
    /// rettangolare (COMPLEXREGION) invece del vecchio HWND squadrato con l'acrilico.
    /// </summary>
    internal async Task BarSelfTestAsync()
    {
        _shotMode = true;
        ShowBar();
        if (_bar is null)
        {
            SetStatus("Barra non disponibile.");
            ExitApplication();
            return;
        }

        _bar.EnableShotMode();
        await Task.Delay(700);
        var region = _bar.RegionType;
        var message = region == 3
            ? $"PASS: regione finestra COMPLEXREGION (angoli arrotondati reali) · {_bar.Describe()}"
            : $"FAIL: regione finestra {region} (attesa 3 = COMPLEXREGION) · {_bar.Describe()}";

        try
        {
            var path = Path.Combine(AppPaths.EnsureSubdirectory("logs"), "bar-selftest.txt");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // il log non deve far fallire l'autotest
        }

        Console.WriteLine(message);
        ExitApplication();
    }

    private double BarRadius() =>
        _bar is null ? 20 : Core.Settings.BarGeometry.CornerRadius(_bar.Height);

    private static void Capture(FrameworkElement element, string path, System.Windows.Media.Color background, double cornerRadius)
    {
        try
        {
            WindowShot.Capture(element, path, background, cornerRadius);
        }
        catch (Exception exception)
        {
            ShotLog($"{path}: {exception.Message}");
        }
    }

    private static void ShotLog(string message)
    {
        try
        {
            var path = Path.Combine(AppPaths.EnsureSubdirectory("logs"), "screenshot.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // il log non deve far fallire gli screenshot
        }
    }

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

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => OpenSettings(SettingsTabs.Aspetto);

    private void OnHistoryMenuClicked(object sender, RoutedEventArgs e) => OpenSettings(SettingsTabs.Storico);

    private void OnModelsMenuClicked(object sender, RoutedEventArgs e) => OpenSettings(SettingsTabs.Modelli);

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        UpdateTray(message);
        RefreshBar();
    }

    private void UpdateTray(string status) =>
        _tray?.Update(
            IsVisible,
            _session is { IsRunning: true },
            SystemToggle.IsChecked == true,
            MicrophoneToggle.IsChecked == true,
            _overlay is { IsVisible: true },
            _bar is { IsVisible: true },
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
        MenuBar.IsChecked = _bar is { IsVisible: true };
        MenuConversation.IsChecked = ConversationToggle.IsChecked == true;
        MenuTranslate.IsChecked = TranslateToggle.IsChecked == true;
        MenuBarBoth.IsChecked = _settings.BarText == BarTextMode.Entrambi;
        MenuBarOriginal.IsChecked = _settings.BarText == BarTextMode.Originale;
        MenuBarTranslation.IsChecked = _settings.BarText == BarTextMode.Traduzione;
        BarToggle.IsChecked = _bar is { IsVisible: true };
    }

    /// <summary>
    /// Apre le impostazioni su una scheda specifica:
    /// 0 aspetto, 1 prestazioni, 2 modelli, 3 template, 4 storico, 5 IA.
    /// </summary>
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
        ConversationToggle.IsChecked = _settings.ConversationMode;
        TranslateToggle.IsChecked = _settings.Translate;

        while (_cues.Count > Math.Max(1, _settings.MaxCues))
        {
            _cues.RemoveAt(_cues.Count - 1);
        }

        if (_settings.StartWithWindows != StartupRegistration.IsEnabled())
        {
            StartupRegistration.Set(_settings.StartWithWindows);
        }

        _overlay?.Refresh();
        if (_bar is not null) { _bar.Topmost = _settings.Topmost; _bar.ApplyLayout(); }
        RefreshBar();
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
        if (!_exiting && _settings.CloseToTray && !_shotMode)
        {
            e.Cancel = true;
            Hide();
            UpdateTray(StatusText.Text);
            _tray?.ShowBalloon("Wisper Translator", "Continua a funzionare in background. Doppio clic sull'icona per riaprirlo.");
            return;
        }

        _exiting = true;

        // In modalità screenshot le preferenze non si toccano: le finestre sono spostate a mano.
        if (!_shotMode)
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.Topmost = Topmost;
            _settings.BarLeft = _bar?.Left ?? _settings.BarLeft;
            _settings.BarTop = _bar?.Top ?? _settings.BarTop;
            _settings.Save();
        }

        _bar?.ForceClose();

        foreach (var id in new[] { HotkeyShowHide, HotkeySystem, HotkeyMicrophone, HotkeySwap, HotkeyOverlay, HotkeyBar })
        {
            Win32.UnregisterHotKey(this, id);
        }

        _source?.RemoveHook(WndProc);
        _barRefreshTimer.Stop();
        _levelTimer?.Stop();
        _historyTimer?.Stop();

        // Il server di traduzione è un processo figlio: va chiuso prima di uscire.
        _session?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(4));
        _session = null;
        AsrEnginePool.Clear();
        NeMoSpeechHost.Shutdown();

        _tray?.Dispose();
        Application.Current.Shutdown();
    }
}
