using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WisperTranslator.App.Interop;
using WisperTranslator.Core.Session;
using WisperTranslator.Core.Settings;
using Wpf.Ui.Controls;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using ColorConverter = System.Windows.Media.ColorConverter;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using SystemColors = System.Windows.SystemColors;

namespace WisperTranslator.App.Windows;

/// <summary>Stato dei controlli della barra, calcolato dalla finestra principale.</summary>
public sealed record BarState(
    bool Running,
    bool SystemAudio,
    bool Microphone,
    bool Overlay,
    bool Conversation,
    string SourceLanguage,
    string Status,
    string SpeakerHint);

/// <summary>
/// Pannello di vetro fluttuante: la finestra è ritagliata ad angoli arrotondati e l'acrilico
/// riempie esattamente quella forma. Non possiede la sessione: la comanda il pannello.
/// </summary>
public partial class BarWindow : FluentWindow
{
    private const double SnapDistance = 16;
    private const int WmSize = 0x0005;
    private const int WmNcHitTest = 0x0084;
    private const int WmSizing = 0x0214;
    private const int WmExitSizeMove = 0x0232;
    private const int WmDpiChanged = 0x02E0;
    private const int HtTransparent = -1;
    private const int WmszLeft = 1;
    private const int WmszTopLeft = 4;
    private const int WmszBottomLeft = 7;

    private static readonly Color Coral = Color.FromRgb(0xFF, 0x7A, 0x59);
    private static readonly Color StopColor = Color.FromRgb(0xFF, 0x5A, 0x5A);
    private static readonly Color RowColor = Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF);
    private static readonly Brush CoralBrush = Freeze(new SolidColorBrush(Coral));
    private static readonly Brush StopBrush = Freeze(new SolidColorBrush(StopColor));
    private static readonly Brush RowBrush = Freeze(new SolidColorBrush(RowColor));

    private readonly AppSettings _settings;
    private readonly ObservableCollection<CueView> _rows = [];
    private readonly DispatcherTimer _pulseTimer;
    private HwndSource? _source;
    private bool _shot;
    private bool _forceClose;
    private int _lastTopId = -1;
    private double _level;
    private bool _running;
    private bool _hasCues;
    private (int Width, int Height, int Diameter) _region;
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF6)));

    public BarWindow(AppSettings settings)
    {
        _settings = settings;

        InitializeComponent();
        CueList.ItemsSource = _rows;

        // Sopra tutto: è un widget, se finisse dietro al browser non servirebbe a niente.
        Topmost = settings.Topmost;
        ApplyLayout();
        RestorePlacement();

        StatusDot.RenderTransformOrigin = new Point(0.5, 0.5);
        StatusDot.RenderTransform = new ScaleTransform(1, 1);

        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _pulseTimer.Tick += (_, _) => TickPulse();

        Loaded += (_, _) => { ApplyMaterial(); ApplyWindowRegion(); UpdatePulseTimer(); };
        IsVisibleChanged += (_, _) =>
        {
            UpdatePulseTimer();
            if (IsVisible) PlayAppearance();
        };
        Closed += (_, _) =>
        {
            _pulseTimer.Stop();
            _source?.RemoveHook(WndProc);
        };

        Closing += OnClosing;
    }

    /// <summary>Livello audio corrente (0–1): lo fornisce la finestra principale.</summary>
    public Func<float>? LevelProvider { get; set; }

    public event Action? StartStopRequested;

    public event Action? SystemRequested;

    public event Action? MicrophoneRequested;

    public event Action? SwapRequested;

    public event Action? TextModeRequested;

    public event Action? ConversationRequested;

    public event Action? TranslateRequested;

    public event Action? DiscreetRequested;

    public event Action? OverlayRequested;

    public event Action? PanelRequested;

    public event Action? SettingsRequested;

    public event Action? HideRequested;

    /// <summary>Righe visibili, larghezza, altezza e raggio: tutto dipende dalle impostazioni.</summary>
    public void ApplyLayout()
    {
        var rows = Math.Clamp(_settings.BarRows, 1, BarGeometry.MaxRows);
        var width = BarGeometry.ClampWidth(_settings.BarWidth);
        var height = _hasCues ? BarGeometry.WindowHeight(rows)
            : BarGeometry.HandleHeight + BarGeometry.IdleHeight + BarGeometry.FooterHeight + BarGeometry.BottomPadding;
        var radius = BarGeometry.CornerRadius(height);

        Scroller.Height = _hasCues ? BarGeometry.ViewportHeight(rows) : BarGeometry.IdleHeight;
        // Il tema impone un'altezza minima da finestra d'applicazione: qui la barra decide da sé.
        MinHeight = 0;
        MaxHeight = double.PositiveInfinity;
        MinWidth = BarGeometry.MinWidth;
        MaxWidth = BarGeometry.MaxWidth;
        Width = width;
        Height = height;
        MinHeight = MaxHeight = height;
        Shell.CornerRadius = new CornerRadius(radius);
        Glass.CornerRadius = new CornerRadius(Math.Max(0, radius - 1));
        ApplyWindowRegion();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // FluentWindow ripristina SingleBorderWindow durante l'inizializzazione.
        WindowStyle = WindowStyle.None;
        Win32.MakeFloatingBar(this);
        _source = HwndSource.FromHwnd(Win32.Handle(this));
        _source?.AddHook(WndProc);
        ApplyWindowRegion();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Win32.MakeFloatingBar(this);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        Win32.MakeFloatingBar(this);
    }

    protected override void OnBackdropTypeChanged(WindowBackdropType oldValue, WindowBackdropType newValue)
    {
        // Il tema globale applica Mica alle finestre: questa barra conserva il suo Acrylic.
        if (IsLoaded) ApplyMaterial();
    }

    private void ApplyMaterial()
    {
        if (_shot) return;
        var transparent = !SystemParameters.HighContrast;
        try
        {
            using var personalize = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            transparent &= personalize?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (System.Security.SecurityException) { transparent = false; }

        WindowBackdrop.RemoveBackdrop(this);
        WindowBackdrop.RemoveBackground(this);
        WindowBackdrop.RemoveTitlebarBackground(this);
        Win32.SetLegacyAcrylic(this, false);
        var acrylic = transparent && (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)
            ? WindowBackdrop.ApplyBackdrop(Win32.Handle(this), WindowBackdropType.Acrylic)
            : Win32.SetLegacyAcrylic(this, true));
        Shell.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush
            : Freeze(new SolidColorBrush(Color.FromArgb(acrylic ? (byte)0xA6 : (byte)0xFF, 0x18, 0x18, 0x21)));
        Glass.Opacity = SystemParameters.HighContrast ? 0 : 1;
        Resources["BarPrimary"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : TextBrush;
        Resources["BarSecondary"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush
            : Freeze(new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xC2)));
        Resources["BarMuted"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush
            : Freeze(new SolidColorBrush(Color.FromRgb(0x92, 0x92, 0xA3)));
        foreach (var row in _rows) row.RefreshColors();
        Win32.MakeFloatingBar(this);
    }

    /// <summary>
    /// Il trucco che elimina il rettangolo: la regione dell'HWND è un rettangolo arrotondato,
    /// quindi l'acrilico non può disegnare i quattro angoli fuori dalla forma.
    /// </summary>
    private void ApplyWindowRegion()
    {
        if (_source is null && Win32.Handle(this) == IntPtr.Zero)
        {
            return;
        }

        var dpi = 1.0;
        try
        {
            dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        }
        catch (Exception)
        {
            // prima della presentazione si usa il fattore standard
        }

        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : Height;
        var radius = BarGeometry.CornerRadius(height);
        var diameter = BarGeometry.RegionDiameter(radius, dpi);
        var region = ((int)Math.Round(width * dpi), (int)Math.Round(height * dpi), diameter);
        if (_region == region) return;
        if (Win32.ApplyRoundedRegion(this, region.Item1, region.Item2, diameter)) _region = region;
    }

    /// <summary>
    /// Modalità screenshot: il corpo diventa opaco, così nella foto non può finire niente di
    /// quello che c'è dietro la finestra.
    /// </summary>
    public void EnableShotMode()
    {
        _shot = true;
        _pulseTimer.Stop();
        Shell.Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x12));
        Glass.Opacity = 0;
        ControlsPanel.Opacity = 1;

        // Niente trasformazioni in corso: la foto deve mostrare il pannello a misura piena.
        Shell.RenderTransform = Transform.Identity;
        ControlsPanel.RenderTransform = Transform.Identity;
        CueList.RenderTransform = Transform.Identity;
    }

    /// <summary>
    /// Aggiorna frasi e controlli. L'elenco viene aggiornato in place: con i parziali rapidi
    /// svuotarlo a ogni tick faceva lampeggiare e scattare la barra.
    /// </summary>
    public void Sync(IReadOnlyList<Cue> cues, BarState state)
    {
        var buffer = Math.Clamp(_settings.BarBuffer, 3, 8);
        var count = Math.Min(buffer, cues.Count);
        var hasCues = count > 0;
        if (_hasCues != hasCues)
        {
            _hasCues = hasCues;
            ApplyLayout();
        }

        for (var index = 0; index < count; index++)
        {
            var cue = cues[index];
            if (index < _rows.Count) _rows[index].Update(cue, _settings.BarText);
            else _rows.Add(new CueView(cue, _settings.BarText));
        }

        while (_rows.Count > count)
        {
            _rows.RemoveAt(_rows.Count - 1);
        }

        if (count > 0 && cues[0].Id != _lastTopId)
        {
            _lastTopId = cues[0].Id;
            PlayArrival();
        }
        else if (count == 0)
        {
            _lastTopId = -1;
        }

        _running = state.Running;
        UpdatePulseTimer();
        DirectionLabel.Text = state.SourceLanguage == "it" ? "IT → EN" : "EN → IT";
        ModeLabel.Text = _settings.Translate ? " · Traduzione" : " · Solo trascrizione";
        PrimaryButton.IsEnabled = !state.Status.StartsWith("Preparo", StringComparison.Ordinal)
            && !state.Status.StartsWith("Avvio", StringComparison.Ordinal)
            && !state.Status.StartsWith("Scarico", StringComparison.Ordinal);
        StatusLabel.Text = state.Running
            ? "In ascolto"
            : state.Status.Length > 0 ? state.Status : "Pronto";
        SpeakerHintText.Text = state.SpeakerHint;
        IdlePanel.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IdleHint.Text = state.Running ? "In ascolto…" : "Premi Avvia per i sottotitoli";
        StatusDot.Opacity = state.Running ? 1 : 0.45;

        PrimaryIcon.Symbol = state.Running ? SymbolRegular.Stop24 : SymbolRegular.Play24;
        PrimaryButton.Background = state.Running ? StopBrush : CoralBrush;
        System.Windows.Automation.AutomationProperties.SetName(PrimaryButton,
            state.Running ? "Ferma la trascrizione" : "Avvia la trascrizione");

        SetControlState(SystemButton, state.SystemAudio);
        SetControlState(MicrophoneButton, state.Microphone);
        SetControlState(ConversationButton, state.Conversation);
        SetControlState(TranslateButton, _settings.Translate);

        TranslateButton.ToolTip = _settings.Translate
            ? "Traduzione attiva (clic per solo trascrizione)"
            : "Solo trascrizione (clic per tradurre)";
        ConversationButton.ToolTip = _settings.ConversationMode
            ? "Conversazione: nomi dei parlanti"
            : "Conversazione spenta: clic per separare le voci";
        SystemButton.ToolTip = state.SystemAudio ? "Audio di sistema attivo" : "Audio di sistema spento";
        MicrophoneButton.ToolTip = state.Microphone ? "Microfono attivo" : "Microfono spento";
        PrimaryButton.ToolTip = state.Running ? "Ferma la sessione" : "Avvia la sessione";
    }

    private static void SetControlState(Button button, bool active)
    {
        button.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : active ? TextBrush : Brushes.Gray;
        button.Background = active ? RowBrush : Brushes.Transparent;
    }

    /// <summary>Tutto il contenuto della riga già pronto: il template non decide niente.</summary>
    private sealed class CueView : INotifyPropertyChanged
    {
        private static readonly Dictionary<int, (Brush Background, Brush Foreground)> SpeakerBrushes = [];
        private Cue? _cue;
        private BarTextMode _mode;

        public CueView(Cue cue, BarTextMode mode) => Update(cue, mode);

        public event PropertyChangedEventHandler? PropertyChanged;
        public string Original { get; private set; } = string.Empty;
        public string Main { get; private set; } = string.Empty;
        public string Trail { get; private set; } = string.Empty;
        public Brush MainBrush { get; private set; } = TextBrush;
        public Brush TrailBrush => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : CoralBrush;
        public string SpeakerLabel => _cue?.SpeakerLabel ?? string.Empty;
        public Brush BadgeBackground { get; private set; } = Brushes.Transparent;
        public Brush BadgeForeground { get; private set; } = Brushes.Gray;
        public Brush RowBackground => RowBrush;
        public bool HasSpeaker => _cue?.HasSpeaker ?? false;
        public bool HasOriginal => Original.Length > 0;
        public bool HasMain => Main.Length > 0;

        public void RefreshColors()
        {
            MainBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush
                : _cue is { IsFinal: false, Translation.Length: 0 } ? CoralBrush : TextBrush;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        public void Update(Cue cue, BarTextMode mode)
        {
            if (_cue == cue && _mode == mode) return;
            _cue = cue;
            _mode = mode;
            // Il provvisorio è l'ipotesi completa: mostrare solo la coda perdeva le parole stabili.
            var original = !cue.IsFinal && cue.Provisional.Length > 0 ? cue.Provisional : cue.Original;
            Original = mode == BarTextMode.Entrambi && cue.Translation.Length > 0 ? original : string.Empty;
            Main = mode != BarTextMode.Originale && cue.Translation.Length > 0 ? cue.Translation : original;
            Trail = mode != BarTextMode.Originale && cue.Translation.Length > 0 && !cue.IsFinal
                ? " " + cue.ProvisionalTail : string.Empty;
            MainBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush
                : !cue.IsFinal && cue.Translation.Length == 0 ? CoralBrush : TextBrush;

            // Il colore dipende dalla palette (8 voci + Tu/sconosciuto), non dall'id illimitato.
            var paletteKey = cue.Speaker > 0 ? (cue.Speaker - 1) % 8 + 1 : cue.Speaker;
            if (!SpeakerBrushes.TryGetValue(paletteKey, out var brushes))
            {
                var color = (Color)ColorConverter.ConvertFromString(Speakers.Color(paletteKey));
                brushes = (Freeze(new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B))),
                    Freeze(new SolidColorBrush(color)));
                SpeakerBrushes[paletteKey] = brushes;
            }

            BadgeBackground = brushes.Background;
            BadgeForeground = brushes.Foreground;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    private void RestorePlacement()
    {
        var area = SystemParameters.WorkArea;
        Left = _settings.BarLeft is { } left && left > area.Left - Width && left < area.Right
            ? Math.Min(left, area.Right - Width)
            : area.Left + (area.Width - Width) / 2;
        Top = _settings.BarTop is { } top && top > area.Top - Height && top < area.Bottom
            ? Math.Min(top, area.Bottom - Height)
            : area.Top + area.Height * 0.2;
    }

    /// <summary>Aggancia la barra al bordo più vicino: è quello che ci si aspetta da un widget.</summary>
    private void SnapToEdges()
    {
        if (!Win32.TryGetBarBounds(this, out var bounds, out var work)) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var distance = SnapDistance * dpi;
        if (Math.Abs(bounds.Left - work.Left) < distance) Left += (work.Left - bounds.Left) / dpi;
        else if (Math.Abs(work.Right - bounds.Right) < distance) Left += (work.Right - bounds.Right) / dpi;
        if (Math.Abs(bounds.Top - work.Top) < distance) Top += (work.Top - bounds.Top) / dpi;
        else if (Math.Abs(work.Bottom - bounds.Bottom) < distance) Top += (work.Bottom - bounds.Bottom) / dpi;
    }

    private void OnShellDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // il trascinamento è già terminato
        }
        finally
        {
            SnapToEdges();
            _settings.BarLeft = Left;
            _settings.BarTop = Top;
            if (!_shot)
            {
                _settings.Save();
            }
        }
    }

    // --- Animazioni ---

    private void PlayAppearance()
    {
        if (_shot || !_settings.BarAnimations || !SystemParameters.ClientAreaAnimation || !IsVisible)
        {
            return;
        }

        Shell.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.985, 0.985);
        Shell.RenderTransform = scale;

        var storyboard = new Storyboard();
        storyboard.Children.Add(Animation(Shell, UIElement.OpacityProperty, 0, 1, 0.24, spring: false));
        storyboard.Children.Add(Animation(scale, ScaleTransform.ScaleXProperty, 0.985, 1, 0.2, spring: true));
        storyboard.Children.Add(Animation(scale, ScaleTransform.ScaleYProperty, 0.985, 1, 0.2, spring: true));
        storyboard.Begin();
    }

    private void PlayArrival()
    {
        if (_shot || !_settings.BarAnimations || !SystemParameters.ClientAreaAnimation || !IsVisible)
        {
            return;
        }

        Scroller.ScrollToTop();
        var list = CueList;
        var transform = new TranslateTransform(0, -4);
        list.RenderTransform = transform;

        var storyboard = new Storyboard();
        storyboard.Children.Add(Animation(list, UIElement.OpacityProperty, 0.4, 1, 0.22, spring: false));
        storyboard.Children.Add(Animation(transform, TranslateTransform.YProperty, -4, 0, 0.18, spring: true));
        storyboard.Begin();
    }

    private static DoubleAnimation Animation(
        DependencyObject target,
        DependencyProperty property,
        double from,
        double to,
        double seconds,
        bool spring)
    {
        var animation = new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(seconds)))
        {
            EasingFunction = spring
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        return animation;
    }

    private void UpdatePulseTimer()
    {
        if (!_shot && IsVisible && _running && _settings.BarAnimations && SystemParameters.ClientAreaAnimation)
            _pulseTimer.Start();
        else
        {
            _pulseTimer.Stop();
            if (StatusDot.RenderTransform is ScaleTransform scale) scale.ScaleX = scale.ScaleY = 1;
        }
    }

    private void OnMoreClicked(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = MoreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    /// <summary>Punto di stato: pulsa con il livello audio reale, respira piano a riposo.</summary>
    private void TickPulse()
    {
        if (_shot || StatusDot.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        if (!_settings.BarAnimations || !SystemParameters.ClientAreaAnimation)
        {
            scale.ScaleX = scale.ScaleY = 1;
            return;
        }

        var level = _running ? Math.Clamp(LevelProvider?.Invoke() ?? 0f, 0f, 1f) : 0f;
        _level = (_level * 0.62) + (level * 0.38);
        var time = Environment.TickCount64 / 1000.0;
        var wave = _running
            ? 0.5 + (0.5 * Math.Abs(Math.Sin(time * 3.2)))
            : 0.12 + (0.08 * Math.Sin(time * 1.4));
        var value = 1 + (0.5 * wave * (0.35 + (0.65 * _level)));
        scale.ScaleX = scale.ScaleY = value;
    }

    /// <summary>
    /// Ciclo dei messaggi: modalità discreta (i clic passano tranne che sulla maniglia),
    /// ridimensionamento orizzontale e riapplicazione della regione dopo ogni cambio di forma.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x001A) // WM_SETTINGCHANGE: trasparenza, contrasto elevato, animazioni.
        {
            Dispatcher.BeginInvoke(() => { ApplyMaterial(); UpdatePulseTimer(); });
            return IntPtr.Zero;
        }
        if (msg is WmSize or WmDpiChanged)
        {
            ApplyWindowRegion();
            return IntPtr.Zero;
        }

        if (msg == WmSizing)
        {
            handled = true;
            return ResizeHorizontally(wParam.ToInt32(), lParam);
        }

        if (msg == WmExitSizeMove)
        {
            _settings.BarWidth = BarGeometry.ClampWidth(Width);
            ApplyLayout();
            if (!_shot)
            {
                _settings.Save();
            }

            return IntPtr.Zero;
        }

        if (msg != WmNcHitTest || !_settings.BarDiscreet)
        {
            return IntPtr.Zero;
        }

        var x = unchecked((short)(long)lParam);
        var y = unchecked((short)((long)lParam >> 16));
        var local = PointFromScreen(new Point(x, y));

        if (local.Y > BarGeometry.HandleHeight && local.Y < Height - BarGeometry.FooterHeight
            && local.X > 0 && local.X < Width)
        {
            handled = true;
            return new IntPtr(HtTransparent);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Larghezza libera, altezza fissa: il pannello cambia solo in orizzontale.
    /// </summary>
    private IntPtr ResizeHorizontally(int edge, IntPtr lParam)
    {
        var rect = Marshal.PtrToStructure<Win32.NativeRect>(lParam);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var min = (int)Math.Round(BarGeometry.MinWidth * dpi);
        var max = (int)Math.Round(BarGeometry.MaxWidth * dpi);
        if (!Win32.TryGetBarBounds(this, out var current, out var area)) return IntPtr.Zero;

        var width = Math.Clamp(rect.Right - rect.Left, min, max);
        width = Math.Min(width, area.Right - area.Left);

        var draggingLeft = edge is WmszLeft or WmszTopLeft or WmszBottomLeft;
        var left = draggingLeft ? current.Right - width : current.Left;
        if (left < area.Left)
        {
            left = area.Left;
        }

        if (left + width > area.Right)
        {
            left = area.Right - width;
        }

        Marshal.StructureToPtr(
            new Win32.NativeRect { Left = left, Top = current.Top, Right = left + width, Bottom = current.Bottom },
            lParam,
            fDeleteOld: false);
        return new IntPtr(1);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose || !_settings.CloseToTray)
        {
            return;
        }

        // La barra non si chiude mai da sola: si nasconde, come il pannello.
        e.Cancel = true;
        Hide();
    }

    /// <summary>Chiusura vera, all'uscita del programma: la X normale nasconde soltanto.</summary>
    public void ForceClose()
    {
        _forceClose = true;
        _pulseTimer.Stop();
        Close();
    }

    /// <summary>Descrizione delle misure reali: serve a capire subito un ritaglio sbagliato.</summary>
    internal string Describe() =>
        $"finestra {Width:F0}x{Height:F0} (min {MinHeight:F0}/{MinWidth:F0}) · "
        + $"pannello {Shell.ActualWidth:F0}x{Shell.ActualHeight:F0} · "
        + $"elenco {Scroller.ActualWidth:F0}x{Scroller.ActualHeight:F0} (chiesto {Scroller.Height:F0}) · "
        + $"righe 1–2 visibili, in memoria {_rows.Count}";

    /// <summary>Elemento da fotografare: il pannello, senza il margine dell'ombra della finestra.</summary>
    internal FrameworkElement ShotTarget => Shell;

    /// <summary>3 = COMPLEXREGION: la finestra non è più un semplice rettangolo.</summary>
    internal int RegionType => Win32.WindowRegionType(this);

    private void OnStartStopClicked(object sender, RoutedEventArgs e) => StartStopRequested?.Invoke();

    private void OnSystemClicked(object sender, RoutedEventArgs e) => SystemRequested?.Invoke();

    private void OnMicrophoneClicked(object sender, RoutedEventArgs e) => MicrophoneRequested?.Invoke();

    private void OnSwapClicked(object sender, RoutedEventArgs e) => SwapRequested?.Invoke();

    private void OnTextModeClicked(object sender, RoutedEventArgs e) => TextModeRequested?.Invoke();

    private void OnConversationClicked(object sender, RoutedEventArgs e) => ConversationRequested?.Invoke();

    private void OnTranslateClicked(object sender, RoutedEventArgs e) => TranslateRequested?.Invoke();

    private void OnDiscreetClicked(object sender, RoutedEventArgs e) => DiscreetRequested?.Invoke();

    private void OnOverlayClicked(object sender, RoutedEventArgs e) => OverlayRequested?.Invoke();

    private void OnPanelClicked(object sender, RoutedEventArgs e) => PanelRequested?.Invoke();

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnHideClicked(object sender, RoutedEventArgs e) => HideRequested?.Invoke();

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
