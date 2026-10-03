using System.Drawing;
using System.Windows.Forms;

namespace WisperTranslator.App;

/// <summary>
/// Icona nell'area di notifica: permette di usare il programma in background, con menu e
/// sottomenu per sorgenti, direzione, sessioni e impostazioni.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _visibilityItem = new("Nascondi widget");
    private readonly ToolStripMenuItem _sessionItem = new("Avvia sessione");
    private readonly ToolStripMenuItem _systemItem = new("Audio di sistema") { CheckOnClick = false };
    private readonly ToolStripMenuItem _microphoneItem = new("Microfono") { CheckOnClick = false };
    private readonly ToolStripMenuItem _itToEnItem = new("Italiano → Inglese") { CheckOnClick = false };
    private readonly ToolStripMenuItem _enToItItem = new("Inglese → Italiano") { CheckOnClick = false };
    private readonly ToolStripMenuItem _overlayItem = new("Sottotitoli a schermo intero") { CheckOnClick = false };
    private readonly ToolStripMenuItem _barItem = new("Barra fluttuante") { CheckOnClick = false };
    private bool _disposed;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };

        _visibilityItem.Click += (_, _) => VisibilityToggleRequested?.Invoke();
        _sessionItem.Click += (_, _) => SessionToggleRequested?.Invoke();
        _systemItem.Click += (_, _) => SystemAudioToggleRequested?.Invoke();
        _microphoneItem.Click += (_, _) => MicrophoneToggleRequested?.Invoke();
        _itToEnItem.Click += (_, _) => DirectionChangeRequested?.Invoke("it");
        _enToItItem.Click += (_, _) => DirectionChangeRequested?.Invoke("en");
        _overlayItem.Click += (_, _) => OverlayToggleRequested?.Invoke();
        _barItem.Click += (_, _) => BarToggleRequested?.Invoke();

        var sources = new ToolStripMenuItem("Sorgenti", null, _systemItem, _microphoneItem);
        var direction = new ToolStripMenuItem("Direzione", null, _itToEnItem, _enToItItem);
        var sessions = new ToolStripMenuItem("Sessioni e IA", null,
            Item("Apri storico e sessioni…", () => HistoryRequested?.Invoke()),
            Item("Modelli…", () => ModelsRequested?.Invoke()));

        menu.Items.AddRange(
        [
            _visibilityItem,
            _sessionItem,
            new ToolStripSeparator(),
            sources,
            direction,
            _barItem,
            _overlayItem,
            new ToolStripSeparator(),
            sessions,
            Item("Impostazioni…", () => SettingsRequested?.Invoke()),
            Item("Apri cartella dati", () => OpenDataFolderRequested?.Invoke()),
            new ToolStripSeparator(),
            Item("Informazioni", () => AboutRequested?.Invoke()),
            Item("Esci", () => ExitRequested?.Invoke()),
        ]);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Wisper Translator",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => VisibilityToggleRequested?.Invoke();
    }

    public event Action? VisibilityToggleRequested;

    public event Action? SessionToggleRequested;

    public event Action? SystemAudioToggleRequested;

    public event Action? MicrophoneToggleRequested;

    public event Action<string>? DirectionChangeRequested;

    public event Action? OverlayToggleRequested;

    public event Action? BarToggleRequested;

    public event Action? HistoryRequested;

    public event Action? ModelsRequested;

    public event Action? SettingsRequested;

    public event Action? OpenDataFolderRequested;

    public event Action? AboutRequested;

    public event Action? ExitRequested;

    public void Update(
        bool windowVisible,
        bool sessionRunning,
        bool systemAudio,
        bool microphone,
        bool overlay,
        bool bar,
        string sourceLanguage,
        string status)
    {
        _visibilityItem.Text = windowVisible ? "Nascondi widget" : "Mostra widget";
        _sessionItem.Text = sessionRunning ? "Ferma sessione" : "Avvia sessione";
        _systemItem.Checked = systemAudio;
        _microphoneItem.Checked = microphone;
        _itToEnItem.Checked = sourceLanguage == "it";
        _enToItItem.Checked = sourceLanguage != "it";
        _overlayItem.Checked = overlay;
        _barItem.Checked = bar;

        var text = $"Wisper Translator — {status}";
        _notifyIcon.Text = text.Length <= 63 ? text : text[..60] + "…";
    }

    public void ShowBalloon(string title, string message, bool warning = false)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private static ToolStripMenuItem Item(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch (Exception)
        {
            // si ripiega sull'icona di sistema
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
