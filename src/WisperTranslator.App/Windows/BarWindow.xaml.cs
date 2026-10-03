using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WisperTranslator.App.Interop;
using WisperTranslator.Core.Session;
using WisperTranslator.Core.Settings;
using Wpf.Ui.Controls;

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
/// Barra fluttuante in stile Apple: acrilico, trascinabile ovunque, due frasi visibili e le
/// altre raggiungibili con la rotellina. Non possiede la sessione: la comanda il pannello.
/// </summary>
public partial class BarWindow : FluentWindow
{
    private const double HandleHeight = 42;
    private const double RowHeight = 54;
    private const double SnapDistance = 16;
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;

    private readonly AppSettings _settings;
    private readonly ObservableCollection<CueView> _rows = [];
    private HwndSource? _source;
    private bool _dragging;
    private bool _forceClose;
    private int _lastTopId = -1;

    public BarWindow(AppSettings settings)
    {
        _settings = settings;

        InitializeComponent();
        CueList.ItemsSource = _rows;

        // Sopra tutto: è un widget, se finisse dietro al browser non servirebbe a niente.
        Topmost = settings.Topmost;
        ApplyLayout();
        RestorePlacement();

        Loaded += (_, _) =>
        {
            StartBreathing();
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

    /// <summary>Righe visibili e dimensione: dipendono dalle impostazioni, non dal contenuto.</summary>
    public void ApplyLayout()
    {
        var rows = Math.Clamp(_settings.BarRows, 1, 3);
        Scroller.Height = rows * RowHeight;
        Height = HandleHeight + (rows * RowHeight) + 8;
        Width = Math.Max(MinWidth, Width);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = HwndSource.FromHwnd(Win32.Handle(this));
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// Aggiorna frasi e controlli. L'animazione parte solo quando arriva una frase nuova,
    /// non a ogni ritocco della traduzione.
    /// </summary>
    public void Sync(IReadOnlyList<Cue> cues, BarState state)
    {
        var buffer = Math.Clamp(_settings.BarBuffer, 3, 8);
        var top = cues.Take(buffer).ToList();

        _rows.Clear();
        foreach (var cue in top)
        {
            _rows.Add(CueView.From(cue, _settings.BarText));
        }

        if (top.Count > 0 && top[0].Id != _lastTopId)
        {
            _lastTopId = top[0].Id;
            PlayArrival();
        }

        TitleText.Text = state.Running ? "Wisper · in ascolto" : "Wisper";
        HandleStatus.Text = state.Status;
        SpeakerHintText.Text = state.SpeakerHint;
        LiveDot.Fill = new SolidColorBrush(
            state.Running
                ? System.Windows.Media.Color.FromRgb(0x34, 0xC7, 0x59)
                : System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8E));

        PlayButton.Icon = new SymbolIcon(state.Running ? SymbolRegular.Stop24 : SymbolRegular.Play24);
        SystemButton.Opacity = state.SystemAudio ? 1 : 0.45;
        MicrophoneButton.Opacity = state.Microphone ? 1 : 0.45;
        ConversationButton.Opacity = state.Conversation ? 1 : 0.45;
        TranslateButton.Opacity = _settings.Translate ? 1 : 0.45;
        DiscreetButton.Opacity = _settings.BarDiscreet ? 1 : 0.45;
        TextModeButton.Opacity = _settings.BarText == BarTextMode.Entrambi ? 0.7 : 1;

        TextModeButton.ToolTip = _settings.BarText switch
        {
            BarTextMode.Originale => "Testo: solo originale",
            BarTextMode.Traduzione => "Testo: solo traduzione",
            _ => "Testo: originale e traduzione",
        };

        TranslateButton.ToolTip = _settings.Translate ? "Traduzione attiva (clic per solo trascrizione)" : "Solo trascrizione (clic per tradurre)";
        ConversationButton.ToolTip = _settings.ConversationMode
            ? "Conversazione: nomi dei parlanti"
            : "Conversazione spenta: clic per separare le voci";
        SystemButton.ToolTip = state.SystemAudio ? "Audio di sistema attivo" : "Audio di sistema spento";
        MicrophoneButton.ToolTip = state.Microphone ? "Microfono attivo" : "Microfono spento";

    }

    /// <summary>Testo grande e compatto: tutto ciò che si vede nella riga è già pronto.</summary>
    private sealed record CueView(string Original, string Translation, string SpeakerLabel, string Color, bool HasSpeaker, bool HasOriginal, bool HasTranslation)
    {
        public static CueView From(Cue cue, BarTextMode mode)
        {
            var showOriginal = mode != BarTextMode.Traduzione;
            var showTranslation = mode != BarTextMode.Originale;
            var original = showOriginal ? cue.DisplayOriginal : string.Empty;
            var translation = showTranslation ? cue.Translation : string.Empty;
            var label = cue.HasSpeaker ? $"{cue.SpeakerLabel} · " : string.Empty;

            return new CueView(
                original,
                translation,
                label,
                cue.SpeakerColor,
                cue.HasSpeaker,
                original.Length > 0,
                translation.Length > 0);
        }
    }

    private void RestorePlacement()
    {
        var area = SystemParameters.WorkArea;
        Left = _settings.BarLeft is { } left && left > area.Left - Width && left < area.Right
            ? left
            : area.Right - Width - 24;
        Top = _settings.BarTop is { } top && top > area.Top - Height && top < area.Bottom
            ? top
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
            _settings.Save();
        }
    }

    private void PlayArrival()
    {
        if (!_settings.BarAnimations)
        {
            return;
        }

        var list = CueList;
        var transform = new TranslateTransform(0, -8);
        list.RenderTransform = transform;

        var storyboard = new Storyboard();
        storyboard.Children.Add(Animation(list, UIElement.OpacityProperty, 0.45, 1, 0.22));
        storyboard.Children.Add(Animation(transform, TranslateTransform.YProperty, -8, 0, 0.24));
        storyboard.Begin();
    }

    private static DoubleAnimation Animation(DependencyObject target, DependencyProperty property, double from, double to, double seconds)
    {
        var animation = new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(seconds)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        return animation;
    }

    /// <summary>L'alone che respira: dice "sto ascoltando" senza occupare spazio.</summary>
    private void StartBreathing()
    {
        if (!_settings.BarAnimations)
        {
            return;
        }

        var animation = new DoubleAnimation(1, 1.35, new Duration(TimeSpan.FromSeconds(1.4)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        LiveDotScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        LiveDotScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /// <summary>
    /// Modalità discreta: i clic passano attraverso la finestra tranne che sulla maniglia,
    /// così la barra non ruba il mouse mentre si lavora su altro.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest || !_settings.BarDiscreet)
        {
            return IntPtr.Zero;
        }

        var x = unchecked((short)(long)lParam);
        var y = unchecked((short)((long)lParam >> 16));
        var local = PointFromScreen(new System.Windows.Point(x, y));

        if (local.Y > HandleHeight && local.X > 0 && local.X < Width)
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
        Close();
    }

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
