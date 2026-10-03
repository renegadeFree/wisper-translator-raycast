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
/// Pannello di vetro fluttuante: la finestra è a strati, quindi la sua forma è l'alfa dei pixel
/// disegnati e l'acrilico di Windows riempie esattamente quella forma. Non possiede la sessione:
/// la comanda il pannello.
/// </summary>
public partial class BarWindow : Window, INotifyPropertyChanged
{
    private const double SnapDistance = 16;
    private const int WmSize = 0x0005;
    private const int WmNcHitTest = 0x0084;
    private const int WmExitSizeMove = 0x0232;
    private const int WmDpiChanged = 0x02E0;
    private const int HtTransparent = -1;

    private static readonly Color Coral = Color.FromRgb(0xFB, 0x92, 0x3C);
    private static readonly Color StopColor = Color.FromRgb(0xEF, 0x44, 0x44);
    private static readonly Color GreenActive = Color.FromRgb(0x10, 0xB9, 0x81);
    private static readonly Color RowColor = Color.FromArgb(0xF2, 0x18, 0x18, 0x22);
    private static readonly Brush CoralBrush = Freeze(new SolidColorBrush(Coral));
    private static readonly Brush StopBrush = Freeze(new SolidColorBrush(StopColor));
    private static readonly Brush GreenActiveBrush = Freeze(new SolidColorBrush(GreenActive));
    private static readonly Brush RowBrush = Freeze(new SolidColorBrush(RowColor));

    private readonly AppSettings _settings;
    private readonly ObservableCollection<CueView> _rows = [];
    private readonly DispatcherTimer _pulseTimer;
    private HwndSource? _source;
    private bool _shot;
    private bool _forceClose;
    private bool _mini;
    private bool? _shotMini;
    private bool _shellHover;
    private bool _dragCandidate;
    private bool _dragged;
    private Point _dragStart;
    private BarGeometry.Edges _resizing;
    private Point _resizeStartScreen;
    private double _resizeScale = 1;
    private (double Left, double Top, double Width, double Height) _resizeOrigin;
    private int _lastTopId = -1;
    private double _level;
    private bool _running;
    private bool _hasCues;
    private bool _acrylic;
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
    private static readonly Brush MutedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)));
    private static readonly Brush OffBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)));

    public double BarFontSize => _settings.BarFontSize;
    public double SecondaryFontSize => Math.Max(10, Math.Round(_settings.BarFontSize * 0.78));

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyFontSizeChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BarFontSize)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondaryFontSize)));
        foreach (var row in _rows) row.RefreshColors();
    }

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

        Loaded += (_, _) => { ApplyMaterial(); ApplyShellClip(); UpdatePulseTimer(); };
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

    /// <summary>Larghezza, altezza e raggio: tutto dipende dalle impostazioni e dai trascinamenti.</summary>
    public void ApplyLayout()
    {
        var mini = _shotMini ?? (_settings.BarMiniIdle && !_hasCues && !_running);
        _mini = mini;
        var center = IsVisible && ActualWidth > 1 ? (double?)(Left + (ActualWidth / 2)) : null;

        // Il tema impone un'altezza minima da finestra d'applicazione: qui la barra decide da sé.
        MinHeight = 0;
        MaxHeight = double.PositiveInfinity;
        MinWidth = 0;
        MaxWidth = double.PositiveInfinity;
        if (mini)
        {
            Scroller.Height = 0;
            MinWidth = MaxWidth = BarGeometry.MiniWidth;
            Width = BarGeometry.MiniWidth;
            Height = BarGeometry.MiniHeight;
            Shell.CornerRadius = new CornerRadius(BarGeometry.MiniRadius);
            Glass.CornerRadius = new CornerRadius(BarGeometry.MiniRadius - 1);
            HandleRow.Visibility = Visibility.Collapsed;
            TranscriptBody.Visibility = Visibility.Collapsed;
            Footer.Visibility = Visibility.Collapsed;
            HandleRowHeight.Height = new GridLength(1, GridUnitType.Star);
            BodyRowHeight.Height = new GridLength(0);
            FooterRowHeight.Height = new GridLength(0);
            MiniPanel.Visibility = Visibility.Visible;
        }
        else
        {
            var width = BarGeometry.ClampWidth(_settings.BarWidth);
            var height = MaxHeightForWorkArea(BarGeometry.ClampHeight(_settings.BarHeight));
            var radius = BarGeometry.CornerRadius(height);
            // Il corpo prende tutto lo spazio rimasto: le frasi che non ci stanno si scorrono.
            Scroller.Height = double.NaN;
            MinWidth = BarGeometry.MinWidth;
            MaxWidth = BarGeometry.MaxWidth;
            Width = width;
            Height = height;
            Shell.CornerRadius = new CornerRadius(radius);
            Glass.CornerRadius = new CornerRadius(Math.Max(0, radius - 1));
            HandleRow.Visibility = Visibility.Visible;
            TranscriptBody.Visibility = Visibility.Visible;
            Footer.Visibility = Visibility.Visible;
            HandleRowHeight.Height = new GridLength(BarGeometry.HandleHeight);
            BodyRowHeight.Height = new GridLength(1, GridUnitType.Star);
            FooterRowHeight.Height = new GridLength(BarGeometry.FooterHeight);
            MiniPanel.Visibility = Visibility.Collapsed;
        }

        MinHeight = MaxHeight = Height;
        MinWidth = MaxWidth = Width;
        if (center is { } anchor)
        {
            Left = anchor - (Width / 2);
        }

        ClampToWorkArea();
        UpdateMiniVisual();
        ApplyOpacity();
        ApplyShellClip();
        NotifyFontSizeChanged();
    }

    /// <summary>Altezza massima reale: il 70% dell'area di lavoro, mai sotto il minimo.</summary>
    private static double MaxHeightForWorkArea(double height) =>
        Math.Min(height, BarGeometry.MaxHeightFor(SystemParameters.WorkArea.Height));

    /// <summary>La tinta del vetro segue lo slider: dark obsidian quasi solido (95%-99%) per massimo contrasto.</summary>
    private void ApplyOpacity()
    {
        if (_shot) return;
        if (SystemParameters.HighContrast)
        {
            Shell.Background = SystemColors.WindowBrush;
            return;
        }

        // Vetro dark obsidian Raycast: quasi solido (95%-99% opacità) per massimo contrasto e zero trasparenza sbiadita
        var opacity = BarGeometry.ClampOpacity(_settings.BarOpacity);
        var alpha = (byte)Math.Clamp(Math.Round((0.94 + (opacity * 0.05)) * 255), 242, 255);
        Shell.Background = Freeze(new SolidColorBrush(Color.FromArgb(alpha, 0x11, 0x11, 0x16)));
    }

    private void ClampToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private void ApplyShellClip()
    {
        if (Shell.ActualWidth <= 1 || Shell.ActualHeight <= 1)
        {
            return;
        }

        var radius = _mini
            ? BarGeometry.MiniRadius
            : BarGeometry.CornerRadius(Shell.ActualHeight);
        Shell.Clip = new RectangleGeometry(
            new Rect(0, 0, Shell.ActualWidth, Shell.ActualHeight),
            radius,
            radius);
    }

    private void OnShellSizeChanged(object sender, SizeChangedEventArgs e) => ApplyShellClip();

    private void UpdateMiniVisual()
    {
        if (!_mini)
        {
            return;
        }

        MiniDots.Visibility = _shellHover ? Visibility.Collapsed : Visibility.Visible;
        MiniPrompt.Visibility = _shellHover ? Visibility.Visible : Visibility.Collapsed;
        MiniPromptText.Text = _running ? "In ascolto…" : "Premi Avvia per i sottotitoli";
        MiniPrimaryIcon.Symbol = _running ? SymbolRegular.Stop24 : SymbolRegular.Play24;
        MiniPrimaryButton.Background = _running ? StopBrush : CoralBrush;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyle = WindowStyle.None;
        Win32.MakeFloatingBar(this);
        _source = HwndSource.FromHwnd(Win32.Handle(this));
        if (_source?.CompositionTarget is not null)
        {
            _source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }

        _source?.AddHook(WndProc);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        WindowStyle = WindowStyle.None;
        Win32.MakeFloatingBar(this);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        WindowStyle = WindowStyle.None;
        Win32.MakeFloatingBar(this);
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

        _acrylic = transparent;

        if (SystemParameters.HighContrast) Shell.Background = SystemColors.WindowBrush;
        else ApplyOpacity();
        Glass.Opacity = SystemParameters.HighContrast ? 0 : 1;
        Resources["BarPrimary"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : TextBrush;
        Resources["BarSecondary"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : MutedBrush;
        Resources["BarMuted"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : OffBrush;
        foreach (var row in _rows) row.RefreshColors();
        Win32.MakeFloatingBar(this);
        ApplyShellClip();
    }

    /// <summary>
    /// Modalità screenshot: il corpo diventa opaco, così nella foto non può finire niente di
    /// quello che c'è dietro la finestra.
    /// </summary>
    public void EnableShotMode()
    {
        _shot = true;
        _pulseTimer.Stop();
        Shell.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x19));
        Glass.Opacity = 0;
        ControlsPanel.Opacity = 1;

        // Niente trasformazioni in corso: la foto deve mostrare il pannello a misura piena.
        Shell.RenderTransform = Transform.Identity;
        ControlsPanel.RenderTransform = Transform.Identity;
        CueList.RenderTransform = Transform.Identity;
    }

    /// <summary>Prepara la barra per una foto: pillola o card, con hover simulato se serve.</summary>
    public void PrepareShot(bool mini, bool hover)
    {
        _shotMini = mini;
        _shellHover = hover;
        ApplyLayout();
        UpdateMiniVisual();
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
        _running = state.Running;
        var wantMini = _shotMini ?? (_settings.BarMiniIdle && !hasCues && !_running);
        if (_hasCues != hasCues || _mini != wantMini)
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

        UpdatePulseTimer();
        UpdateMiniVisual();
        DirectionLabel.Text = state.SourceLanguage == "it" ? "IT → EN" : "EN → IT";
        ModeLabel.Text = _settings.Translate ? " · Traduzione" : " · Solo trascrizione";
        PrimaryButton.IsEnabled = !state.Status.StartsWith("Preparo", StringComparison.Ordinal)
            && !state.Status.StartsWith("Avvio", StringComparison.Ordinal)
            && !state.Status.StartsWith("Scarico", StringComparison.Ordinal);
        MiniPrimaryButton.IsEnabled = PrimaryButton.IsEnabled;
        StatusLabel.Text = state.Running
            ? "In ascolto"
            : state.Status.Length > 0 ? state.Status : "Pronto";
        SpeakerHintText.Text = state.SpeakerHint;
        IdlePanel.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IdleHint.Text = state.Running ? "In ascolto…" : "Premi Avvia per i sottotitoli";
        StatusDot.Fill = state.Running ? GreenActiveBrush : OffBrush;
        StatusDot.Opacity = state.Running ? 1.0 : 0.45;

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
        button.Foreground = SystemParameters.HighContrast
            ? SystemColors.WindowTextBrush
            : active ? TextBrush : OffBrush;
        button.Background = active ? RowBrush : Brushes.Transparent;
    }

    /// <summary>Tutto il contenuto della riga già pronto: il template non decide niente.</summary>
    private sealed class CueView : INotifyPropertyChanged
    {
        private static readonly Dictionary<int, (Brush Background, Brush Foreground, Brush Row)> SpeakerBrushes = [];
        private static readonly Brush RowEdgeBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)));
        private Brush _rowBackground = RowBrush;
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
        public Brush RowBackground => SystemParameters.HighContrast ? RowBrush : _rowBackground;
        public Brush RowEdge => SystemParameters.HighContrast ? Brushes.Transparent : RowEdgeBrush;
        public bool HasSpeaker => _cue?.HasSpeaker ?? false;
        public bool HasOriginal => Original.Length > 0;
        public bool HasMain => Main.Length > 0;

        public void RefreshColors()
        {
            MainBrush = MainBrushFor(_cue);
            if (!SystemParameters.HighContrast && _cue is { } cue && Palette(cue.Speaker) is { } brushes)
            {
                _rowBackground = brushes.Row;
            }

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
            MainBrush = MainBrushFor(cue);

            // Il colore dipende dalla palette (8 voci + Tu/sconosciuto), non dall'id illimitato.
            var brushes = Palette(cue.Speaker);
            BadgeBackground = brushes?.Background ?? Brushes.Transparent;
            BadgeForeground = brushes?.Foreground ?? Brushes.Gray;
            _rowBackground = brushes?.Row ?? RowBrush;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        /// <summary>
        /// Pennelli di un parlante, costruiti una volta sola: il testo usa il colore pieno, la
        /// targhetta una sua velatura e la riga una lastra di vetro con la stessa tinta.
        /// </summary>
        private static (Brush Background, Brush Foreground, Brush Row)? Palette(int speaker)
        {
            var key = speaker > 0 ? (speaker - 1) % 8 + 1 : speaker;
            if (SpeakerBrushes.TryGetValue(key, out var brushes))
            {
                return brushes;
            }

            var color = (Color)ColorConverter.ConvertFromString(Speakers.Color(key));
            brushes = (
                Freeze(new SolidColorBrush(Color.FromArgb(0x35, color.R, color.G, color.B))),
                Freeze(new SolidColorBrush(color)),
                Freeze(new SolidColorBrush(Color.FromArgb(0xF2, 0x18, 0x18, 0x22))));
            SpeakerBrushes[key] = brushes;
            return brushes;
        }

        /// <summary>
        /// Testo ancora senza traduzione = corallo; traduzione provvisoria della corsia rapida =
        /// grigio chiaro, così si vede arrivare; frase definitiva = bianco pieno.
        /// </summary>
        private static Brush MainBrushFor(Cue? cue) => SystemParameters.HighContrast
            ? SystemColors.WindowTextBrush
            : cue is { IsFinal: false } partial
                ? partial.Translation.Length == 0 ? CoralBrush : MutedBrush
                : TextBrush;
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

    private void OnShellMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractive(e.OriginalSource))
        {
            return;
        }

        var position = e.GetPosition(this);
        var edge = _mini
            ? BarGeometry.Edges.None
            : BarGeometry.EdgeAt(position.X, position.Y, Width, Height);
        if (edge != BarGeometry.Edges.None)
        {
            _resizing = edge;
            _resizeStartScreen = PointToScreen(position);
            _resizeScale = ScreenUnitScale();
            _resizeOrigin = (Left, Top, Width, Height);
            e.Handled = true;
            Shell.CaptureMouse();
            return;
        }

        _dragCandidate = true;
        _dragged = false;
        _dragStart = position;
        Shell.CaptureMouse();
    }

    private void OnShellMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizing != BarGeometry.Edges.None)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                FinishResize();
                return;
            }

            ResizeTo(e.GetPosition(this));
            return;
        }

        if (!_dragCandidate || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
        {
            return;
        }

        _dragCandidate = false;
        _dragged = true;
        Shell.ReleaseMouseCapture();
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // il trascinamento è già terminato
        }

        SnapToEdges();
        _settings.BarLeft = Left;
        _settings.BarTop = Top;
        if (!_shot)
        {
            _settings.Save();
        }
    }

    private void OnShellMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizing != BarGeometry.Edges.None)
        {
            FinishResize();
            return;
        }

        var click = _dragCandidate && !_dragged;
        _dragCandidate = false;
        _dragged = false;
        Shell.ReleaseMouseCapture();
        if (click && _mini)
        {
            StartStopRequested?.Invoke();
        }
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var delta = e.Delta > 0 ? 1 : -1;
            var newSize = Math.Clamp(_settings.BarFontSize + delta, 12, 28);
            if (Math.Abs(newSize - _settings.BarFontSize) > 0.1)
            {
                _settings.BarFontSize = newSize;
                if (!_shot) _settings.Save();
                NotifyFontSizeChanged();
            }

            e.Handled = true;
        }
    }

    /// <summary>
    /// Quanti pixel di schermo vale un punto della finestra: <c>PointToScreen</c> cambia unità
    /// con il DPI, e senza questa calibrazione il trascinamento andrebbe a metà velocità.
    /// </summary>
    private double ScreenUnitScale()
    {
        try
        {
            var unit = PointToScreen(new Point(100, 0)).X - PointToScreen(new Point(0, 0)).X;
            var scale = unit / 100.0;
            return scale > 0.01 ? scale : 1.0;
        }
        catch (InvalidOperationException)
        {
            return 1.0;
        }
    }

    /// <summary>Larghezza e altezza libere entro i limiti; il bordo opposto resta fermo.</summary>
    private void ResizeTo(Point position)
    {
        var current = PointToScreen(position);
        var dx = (current.X - _resizeStartScreen.X) / _resizeScale;
        var dy = (current.Y - _resizeStartScreen.Y) / _resizeScale;
        var area = SystemParameters.WorkArea;
        var (left, top, width, height) = BarGeometry.Resize(
            _resizeOrigin.Left, _resizeOrigin.Top, _resizeOrigin.Width, _resizeOrigin.Height,
            _resizing, dx, dy, area.Left, area.Top, area.Width, area.Height);

        MinWidth = 0;
        MaxWidth = double.PositiveInfinity;
        MinHeight = 0;
        MaxHeight = double.PositiveInfinity;
        Width = width;
        Height = height;
        Left = left;
        Top = top;
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;
    }

    /// <summary>Fine del trascinamento: la misura scelta diventa la misura salvata.</summary>
    private void FinishResize()
    {
        _resizing = BarGeometry.Edges.None;
        Shell.ReleaseMouseCapture();
        _settings.BarWidth = BarGeometry.ClampWidth(Width);
        _settings.BarHeight = Math.Min(BarGeometry.ClampHeight(Height), BarGeometry.MaxHeightFor(SystemParameters.WorkArea.Height));
        _settings.BarRows = BarGeometry.RowsForHeight(_settings.BarHeight);
        _settings.BarLeft = Left;
        _settings.BarTop = Top;
        if (!_shot)
        {
            _settings.Save();
        }

        ApplyLayout();
    }

    private void OnShellMouseEnter(object sender, MouseEventArgs e)
    {
        _shellHover = true;
        UpdateMiniVisual();
    }

    private void OnShellMouseLeave(object sender, MouseEventArgs e)
    {
        _shellHover = false;
        UpdateMiniVisual();
    }

    private static bool IsInteractive(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button)
            {
                return true;
            }

            // Il testo dei sottotitoli è fatto di Run, che sono ContentElement e non Visual:
            // VisualTreeHelper su di loro solleva un'eccezione (un clic sul testo falliva).
            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    // --- Animazioni ---

    private void PlayAppearance()
    {
        if (_shot || !_settings.BarAnimations || !SystemParameters.ClientAreaAnimation || !IsVisible)
        {
            return;
        }

        Shell.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.96, 0.96);
        Shell.RenderTransform = scale;

        var storyboard = new Storyboard();
        storyboard.Children.Add(Animation(Shell, UIElement.OpacityProperty, 0, 1, 0.20, spring: false));
        storyboard.Children.Add(Animation(scale, ScaleTransform.ScaleXProperty, 0.96, 1, 0.22, spring: true));
        storyboard.Children.Add(Animation(scale, ScaleTransform.ScaleYProperty, 0.96, 1, 0.22, spring: true));
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
        var transform = new TranslateTransform(0, 8);
        list.RenderTransform = transform;

        var storyboard = new Storyboard();
        storyboard.Children.Add(Animation(list, UIElement.OpacityProperty, 0.35, 1, 0.20, spring: false));
        storyboard.Children.Add(Animation(transform, TranslateTransform.YProperty, 8, 0, 0.22, spring: true));
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
            MiniDots.Opacity = 1;
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
        MiniDots.Opacity = _mini ? 0.35 + (0.65 * wave) : 1;
    }

    /// <summary>
    /// Ciclo dei messaggi: modalità discreta (i clic passano tranne che sulla maniglia),
    /// ridimensionamento orizzontale e riallineamento del ritaglio dopo ogni cambio di forma.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x001A) // WM_SETTINGCHANGE: trasparenza, contrasto elevato, animazioni.
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplyMaterial();
                Win32.MakeFloatingBar(this);
                ApplyShellClip();
                UpdatePulseTimer();
            });
            return IntPtr.Zero;
        }
        if (msg is WmSize or WmDpiChanged)
        {
            ApplyShellClip();
            return IntPtr.Zero;
        }

        if (msg == WmExitSizeMove)
        {
            if (!_mini)
            {
                _settings.BarLeft = Left;
                _settings.BarTop = Top;
                if (!_shot)
                {
                    _settings.Save();
                }
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
        $"finestra {Width:F0}x{Height:F0} · pannello {Shell.ActualWidth:F0}x{Shell.ActualHeight:F0} "
        + $"(riempie la finestra: {(ShellFillsWindow ? "sì" : "no")}) · "
        + $"elenco {Scroller.ActualWidth:F0}x{Scroller.ActualHeight:F0} · "
        + $"vetro {_settings.BarOpacity:P0} · in memoria {_rows.Count}";

    /// <summary>
    /// True quando il pannello di vetro riempie esattamente la finestra: se resta anche solo un
    /// pixel di finestra scoperto, l'acrilico lo dipinge come una fascia fuori posto.
    /// </summary>
    internal bool ShellFillsWindow
    {
        get
        {
            if (!Win32.TryGetBarBounds(this, out var bounds, out _)) return false;
            if (Shell.ActualWidth <= 1 || Shell.ActualHeight <= 1) return false;
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var width = (bounds.Right - bounds.Left) / dpi;
            var height = (bounds.Bottom - bounds.Top) / dpi;
            return Math.Abs(Shell.ActualWidth - width) <= 1.5
                   && Math.Abs(Shell.ActualHeight - height) <= 1.5;
        }
    }

    /// <summary>Elemento da fotografare: il pannello, senza il margine dell'ombra della finestra.</summary>
    internal FrameworkElement ShotTarget => Shell;

    internal bool HasWindowFrame => Win32.HasWindowFrame(this);

    internal bool HasShellClip => Shell.Clip is not null;

    /// <summary>True quando il vetro di Windows è attivo sulla barra.</summary>
    internal bool HasAcrylic => _acrylic;

    internal bool IsMini => _mini;

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
