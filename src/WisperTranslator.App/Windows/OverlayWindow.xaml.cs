using System.Collections.ObjectModel;
using System.Windows;
using WisperTranslator.App.Interop;
using WisperTranslator.Core.Session;
using WisperTranslator.Core.Settings;

namespace WisperTranslator.App.Windows;

/// <summary>
/// Sottotitoli a schermo intero: finestra trasparente, click-through e esclusa dalle
/// registrazioni, così non disturba il gioco o il video sottostante.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly ObservableCollection<Cue> _source;
    private readonly AppSettings _settings;

    public OverlayWindow(ObservableCollection<Cue> source, AppSettings settings)
    {
        _source = source;
        _settings = settings;

        InitializeComponent();

        FontSize = settings.OverlayFontSize;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += (_, _) => Win32.MakeOverlay(this, settings.OverlayHiddenFromCapture);
    }

    /// <summary>Riallinea l'elenco quando arrivano o cambiano battute.</summary>
    public void Refresh()
    {
        var count = Math.Max(1, _settings.OverlayMaxCues);
        CueList.ItemsSource = _source.Take(count).Reverse().ToList();
    }
}
