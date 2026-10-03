using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using WisperTranslator.App.Interop;
using WisperTranslator.Core.Session;
using WisperTranslator.Core.Settings;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

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
/// Capsula fluttuante in stile Apple: acrilico, trascinabile ovunque, larga quanto basta a una
/// frase intera su due righe. Non possiede la sessione: la comanda il pannello.
/// </summary>
public partial class BarWindow : FluentWindow
{
    private const double SnapDistance = 16;
    private const int WmNcHitTest = 0x0084;
    private const int WmSizing = 0x0214;
    private const int WmExitSizeMove = 0x0232;
    private const int HtTransparent = -1;
    private const int WmszLeft = 1;
    private const int WmszRight = 2;
    private const int WmszTop = 3;
    private const int WmszTopLeft = 4;
    private const int WmszTopRight = 5;
    private const int WmszBottom = 6;
    private const int WmszBottomLeft = 7;
    private const int WmszBottomRight = 8;

    private static readonly Color Coral = Color.FromRgb(0xFF, 0x7A, 0x59);
    private static readonly Color StopColor = Color.FromRgb(0xFF, 0x5A, 0x5A);

    private readonly AppSettings _settings;
    private readonly ObservableCollection<CueView> _rows = [];
    private readonly HashSet<int> _finalSeen = [];
    private readonly Rectangle[] _bars;
    private readonly DispatcherTimer _equalizerTimer;
    private HwndSource? _source;
    private bool _dragging;
    private bool _shot;
    private bool _forceClose;
    private int _lastTopId = -1;
    private double _level;
    private bool _running;

    public BarWindow(AppSettings settings)
    {
        _settings = settings;

        InitializeComponent();
        CueList.ItemsSource = _rows;
        _bars = [Eq0, Eq1, Eq2, Eq3, Eq4];

        // Sopra tutto: è un widget, se finisse dietro al browser non servirebbe a niente.
        Topmost = settings.Topmost;
        ApplyLayout();
        RestorePlacement();

        _equalizerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        _equalizerTimer.Tick += (_, _) => TickEqualizer();

        Loaded += (_, _) =>
        {
            PlayAppearance();
            _equalizerTimer.Start();
        };

        LocationChanged += (_, _) =>
        {
            if (!_dragging)
            {
                return;
            }

            SnapToEdges();
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
        var rows = Math.Clamp(_settings.BarRows, 1, 3);
        var width = BarGeometry.ClampWidth(_settings.BarWidth);
        var height = BarGeometry.WindowHeight(rows);
        var radius = BarGeometry.CornerRadius(height);

        Scroller.Height = BarGeometry.ViewportHeight(rows);
        // Il tema impone un'altezza minima da finestra d'applicazione: qui la barra decide da sé.
        MinHeight = height;
        MaxHeight = height;
        MinWidth = BarGeometry.MinWidth;
        MaxWidth = BarGeometry.MaxWidth;
        Width = width;
        Height = height;
        Shell.CornerRadius = new CornerRadius(radius);
        Veil.RadiusX = Veil.RadiusY = Math.Max(0, radius - 1);
        Glass.RadiusX = Glass.RadiusY = Math.Max(0, radius - 1);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = HwndSource.FromHwnd(Win32.Handle(this));
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// Modalità screenshot: il corpo diventa opaco, così nella foto non può finire niente di
    /// quello che c'è dietro la finestra.
    /// </summary>
    public void EnableShotMode()
    {
        _shot = true;
        Veil.Opacity = 1;
        Veil.Fill = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x12));
        Glass.Fill = Brushes.Transparent;
        ControlsPanel.Opacity = 1;

        // Niente trasformazioni in corso: la foto deve mostrare la capsula a misura piena.
        Shell.RenderTransform = Transform.Identity;
        ControlsPanel.RenderTransform = Transform.Identity;
        CueList.RenderTransform = Transform.Identity;
    }

    /// <summary>
    /// Aggiorna frasi e controlli. L'animazione parte solo quando arriva una frase nuova,
    /// non a ogni ritocco della traduzione.
    /// </summary>
    public void Sync(IReadOnlyList<Cue> cues, BarState state)
    {
        var buffer = Math.Clamp(_settings.BarBuffer, 3, 8);
        var top = cues.Take(buffer).ToList();
        var primary = TryFindResource("TextFillColorPrimaryBrush") is SolidColorBrush brush
            ? brush.Color
            : Colors.White;

        if (top.Count == 0)
        {
            _finalSeen.Clear();
        }

        _rows.Clear();
        foreach (var cue in top)
        {
            var flash = cue.IsFinal && _finalSeen.Add(cue.Id);
            _rows.Add(CueView.From(cue, _settings.BarText, primary, flash));
        }

        if (top.Count > 0 && top[0].Id != _lastTopId)
        {
            _lastTopId = top[0].Id;
            PlayArrival();
        }

        _running = state.Running;
        TitleText.Text = state.Running ? "Wisper · in ascolto" : "Wisper";
        HandleStatus.Text = state.Status;
        SpeakerHintText.Text = state.SpeakerHint;
        IdleHint.Opacity = top.Count == 0 ? 0.75 : 0;
        IdleHint.Text = state.Running ? "In ascolto…" : "Premi Avvia per i sottotitoli";

        PrimaryIcon.Symbol = state.Running ? SymbolRegular.Stop24 : SymbolRegular.Play24;
        if (PrimaryButton.Template.FindName("Circle", PrimaryButton) is Border circle)
        {
            circle.Background = new SolidColorBrush(state.Running ? StopColor : Coral);
        }

        SetControlState(SystemButton, state.SystemAudio);
        SetControlState(MicrophoneButton, state.Microphone);
        SetControlState(ConversationButton, state.Conversation);
        SetControlState(TranslateButton, _settings.Translate);
        SetControlState(DiscreetButton, _settings.BarDiscreet);
        TextModeButton.Opacity = _settings.BarText == BarTextMode.Entrambi ? 0.7 : 1;

        TextModeButton.ToolTip = _settings.BarText switch
        {
            BarTextMode.Originale => "Testo: solo originale",
            BarTextMode.Traduzione => "Testo: solo traduzione",
            _ => "Testo: originale e traduzione",
        };

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

    private static void SetControlState(Button button, bool active) =>
        button.Opacity = active ? 1 : 0.42;

    /// <summary>Tutto il contenuto della riga già pronto: il template non decide niente.</summary>
    private sealed record CueView(
        string Original,
        string Main,
        string MainColor,
        string Trail,
        string SpeakerLabel,
        string BadgeBackground,
        string BadgeForeground,
        string RowBackground,
        bool HasSpeaker,
        bool HasOriginal,
        bool HasMain,
        bool Flash)
    {
        public static CueView From(Cue cue, BarTextMode mode, Color primary, bool flash)
        {
            var originalLine = mode == BarTextMode.Originale ? string.Empty : cue.Original;
            var mainLine = mode == BarTextMode.Originale ? cue.Original : cue.Translation;
            var trail = string.Empty;
            var provisional = !cue.IsFinal && cue.ProvisionalTail.Length > 0;

            if (mainLine.Length == 0)
            {
                mainLine = originalLine.Length > 0 ? originalLine : cue.DisplayOriginal;
                originalLine = string.Empty;
            }

            var mainIsProvisional = false;
            if (provisional)
            {
                if (cue.Translation.Length > 0 && mode != BarTextMode.Originale)
                {
                    trail = cue.ProvisionalTail;
                }
                else
                {
                    mainLine = cue.ProvisionalTail;
                    mainIsProvisional = true;
                }
            }

            var speaker = cue.HasSpeaker ? cue.SpeakerColor.TrimStart('#') : string.Empty;
            return new CueView(
                originalLine,
                mainLine,
                ToHex(mainIsProvisional ? Coral : primary),
                trail,
                cue.SpeakerLabel,
                speaker.Length == 6 ? $"#33{speaker}" : "#00000000",
                cue.SpeakerColor,
                flash ? "#26FF7A59" : "#12FFFFFF",
                cue.HasSpeaker,
                originalLine.Length > 0,
                mainLine.Length > 0,
                flash);
        }

        private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void RestorePlacement()
    {
        var area = SystemParameters.WorkArea;
        Left = _settings.BarLeft is { } left && left > area.Left - Width && left < area.Right
            ? Math.Min(left, area.Right - Width)
            : area.Right - Width - 24;
        Top = _settings.BarTop is { } top && top > area.Top - Height && top < area.Bottom
            ? Math.Min(top, area.Bottom - Height)
            : area.Top + 60;
    }

    /// <summary>Aggancia la barra al bordo più vicino: è quello che ci si aspetta da un widget.</summary>
    private void SnapToEdges()
    {
        var area = SystemParameters.WorkArea;
        if (Math.Abs(Left - area.Left) < SnapDistance)
        {
            Left = area.Left;
        }
        else if (Math.Abs(area.Right - (Left + Width)) < SnapDistance)
        {
            Left = area.Right - Width;
        }

        if (Math.Abs(Top - area.Top) < SnapDistance)
        {
            Top = area.Top;
        }
        else if (Math.Abs(area.Bottom - (Top + Height)) < SnapDistance)
        {
            Top = area.Bottom - Height;
        }
    }

    private void OnShellDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        _dragging = true;
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
            _dragging = false;
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
        if (_shot || !_settings.BarAnimations)
        {
            return;
        }

        Shell.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.94, 0.94);
        Shell.RenderTransform = scale;

        var storyboard = new Storyboard();
        storyboard.Children.Add(Animation(Shell, UIElement.OpacityProperty, 0, 1, 0.24, spring: false));
        storyboard.Children.Add(Animation(scale, ScaleTransform.ScaleXProperty, 0.94, 1, 0.34, spring: true));
        storyboard.Children.Add(Animation(scale, ScaleTransform.ScaleYProperty, 0.94, 1, 0.34, spring: true));
        storyboard.Begin();
    }

    private void PlayArrival()
    {
        if (_shot || !_settings.BarAnimations)
        {
            return;
        }

        var list = CueList;
        var transform = new TranslateTransform(0, -10);
        list.RenderTransform = transform;

        var storyboard = new Storyboard();
        storyboard.Children.Add(Animation(list, UIElement.OpacityProperty, 0.4, 1, 0.22, spring: false));
        storyboard.Children.Add(Animation(transform, TranslateTransform.YProperty, -10, 0, 0.26, spring: true));
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

    /// <summary>
    /// Equalizzatore: quando ascolta segue il livello audio reale, a riposo respira piano.
    /// Ogni barretta ha una fase diversa, così il movimento non sembra un blocco unico.
    /// </summary>
    private void TickEqualizer()
    {
        if (!_settings.BarAnimations)
        {
            foreach (var bar in _bars)
            {
                bar.Height = 6;
            }

            return;
        }

        var level = _running ? Math.Clamp(LevelProvider?.Invoke() ?? 0f, 0f, 1f) : 0f;
        _level = (_level * 0.62) + (level * 0.38);

        var time = Environment.TickCount64 / 1000.0;
        for (var index = 0; index < _bars.Length; index++)
        {
            var wave = _running
                ? 0.3 + (0.7 * Math.Abs(Math.Sin((time * 3.4) + (index * 0.85))))
                : 0.16 + (0.1 * Math.Sin((time * 1.3) + (index * 0.7)));
            var height = 4 + (18 * wave * (0.4 + (0.6 * _level)));
            _bars[index].Height = _shot ? 4 + (14 * wave) : height;
        }
    }

    /// <summary>
    /// Ciclo dei messaggi: modalità discreta (i clic passano tranne che sulla maniglia) e
    /// ridimensionamento orizzontale con l'altezza bloccata.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
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
        var local = PointFromScreen(new System.Windows.Point(x, y));

        if (local.Y > BarGeometry.HandleHeight && local.X > 0 && local.X < Width)
        {
            handled = true;
            return new IntPtr(HtTransparent);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Larghezza libera, altezza fissa: la capsula cambia solo in orizzontale.
    /// </summary>
    private IntPtr ResizeHorizontally(int edge, IntPtr lParam)
    {
        var rect = Marshal.PtrToStructure<NativeRect>(lParam);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var min = (int)Math.Round(BarGeometry.MinWidth * dpi);
        var max = (int)Math.Round(BarGeometry.MaxWidth * dpi);
        var area = SystemParameters.WorkArea;
        var current = new NativeRect
        {
            Left = (int)Math.Round(Left * dpi),
            Top = (int)Math.Round(Top * dpi),
            Right = (int)Math.Round((Left + Width) * dpi),
            Bottom = (int)Math.Round((Top + Height) * dpi),
        };

        var width = Math.Clamp(rect.Right - rect.Left, min, max);
        width = Math.Min(width, (int)Math.Round(area.Width * dpi));

        var draggingLeft = edge is WmszLeft or WmszTopLeft or WmszBottomLeft;
        var left = draggingLeft ? current.Right - width : current.Left;
        if (left < Math.Round(area.Left * dpi))
        {
            left = (int)Math.Round(area.Left * dpi);
        }

        if (left + width > Math.Round(area.Right * dpi))
        {
            left = (int)Math.Round(area.Right * dpi) - width;
        }

        Marshal.StructureToPtr(
            new NativeRect { Left = left, Top = current.Top, Right = left + width, Bottom = current.Bottom },
            lParam,
            fDeleteOld: false);
        return new IntPtr(1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
        _equalizerTimer.Stop();
        Close();
    }

    /// <summary>Descrizione delle misure reali: serve a capire subito un ritaglio sbagliato.</summary>
    internal string Describe() =>
        $"finestra {Width:F0}x{Height:F0} (min {MinHeight:F0}/{MinWidth:F0}) · "
        + $"capsula {Shell.ActualWidth:F0}x{Shell.ActualHeight:F0} · "
        + $"elenco {Scroller.ActualWidth:F0}x{Scroller.ActualHeight:F0} (chiesto {Scroller.Height:F0}) · "
        + $"righe 1–3 visibili, in memoria {_rows.Count}";

    /// <summary>Elemento da fotografare: la capsula, senza il margine dell'ombra della finestra.</summary>
    internal FrameworkElement ShotTarget => Shell;

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
}
