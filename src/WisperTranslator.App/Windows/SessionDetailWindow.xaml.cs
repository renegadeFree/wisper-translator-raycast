using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using WisperTranslator.Core;
using WisperTranslator.Core.Ai;
using WisperTranslator.Core.History;
using WisperTranslator.Core.Rendering;
using WisperTranslator.Core.Settings;
using WisperTranslator.Core.Templates;
using Wpf.Ui.Controls;

namespace WisperTranslator.App.Windows;

/// <summary>
/// Visualizzatore di una sessione: trascrizione completa a sinistra, contenuti IA a destra,
/// con esportazione in PDF, SRT, testo e JSON.
/// </summary>
public partial class SessionDetailWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private readonly long _sessionId;
    private readonly IReadOnlyList<HistoryCue> _cues;
    private readonly HistorySession _session;
    private ConceptMap? _map;
    private bool _busy;

    public SessionDetailWindow(AppSettings settings, HistorySession session, IReadOnlyList<HistoryCue> cues)
    {
        _settings = settings;
        _session = session;
        _sessionId = session.Id;
        _cues = cues;

        InitializeComponent();

        Title = $"Sessione #{session.Id}";
        SessionInfo.Text =
            $"{session.StartedAt:dd/MM/yyyy HH:mm} · {session.SourceLanguage} → {session.TargetLanguage} · "
            + $"{cues.Count} battute · {(session.EndedAt is null ? "in corso" : $"durata {session.EndedAt - session.StartedAt:hh\\:mm\\:ss}")}";

        CueList.ItemsSource = cues
            .Select(cue => new
            {
                Header = $"[{cue.AudioStart:mm\\:ss}]",
                Speaker = cue.HasSpeaker ? cue.SpeakerLabel : string.Empty,
                SpeakerColor = cue.SpeakerColor,
                HasSpeaker = cue.HasSpeaker,
                cue.Original,
                cue.Translation,
            })
            .ToList();

        LoadTemplates();
        LoadNote();
    }

    private sealed record LabelledOption<T>(T Value, string Label);

    /// <summary>Popola i selettori dei template e gli override di orientamento e palette.</summary>
    private void LoadTemplates()
    {
        var maps = TemplateStore.Maps();
        MapTemplateBox.ItemsSource = maps;
        MapTemplateBox.SelectedItem = maps.FirstOrDefault(template => template.Id == _settings.MapTemplateId)
                                      ?? maps.FirstOrDefault();

        MapOrientationBox.ItemsSource = Enum.GetValues<MapOrientation>()
            .Select(value => new LabelledOption<MapOrientation>(value, Describe(value)))
            .ToList();
        MapOrientationBox.SelectedIndex = 0;

        MapPaletteBox.ItemsSource = Enum.GetValues<MapPalette>()
            .Select(value => new LabelledOption<MapPalette>(value, Describe(value)))
            .ToList();
        MapPaletteBox.SelectedIndex = 0;

        var pdfs = TemplateStore.Pdfs();
        PdfTemplateBox.ItemsSource = pdfs;
        PdfTemplateBox.SelectedItem = pdfs.FirstOrDefault(template => template.Id == _settings.PdfTemplateId)
                                      ?? pdfs.FirstOrDefault();
    }

    /// <summary>Il template scelto, con gli override di orientamento e palette dell'utente.</summary>
    private MapTemplate SelectedMapTemplate()
    {
        var template = MapTemplateBox.SelectedItem as MapTemplate
                       ?? TemplateStore.MapOrDefault(_settings.MapTemplateId);
        var orientation = (MapOrientationBox.SelectedItem as LabelledOption<MapOrientation>)?.Value;
        var palette = (MapPaletteBox.SelectedItem as LabelledOption<MapPalette>)?.Value;
        var updated = template;
        if (orientation is { } chosenOrientation)
        {
            updated = updated with { Orientation = chosenOrientation };
        }

        if (palette is { } chosenPalette)
        {
            updated = updated with { Style = updated.Style with { Palette = chosenPalette } };
        }

        return updated;
    }

    private PdfTemplate SelectedPdfTemplate() =>
        PdfTemplateBox.SelectedItem as PdfTemplate ?? TemplateStore.PdfOrDefault(_settings.PdfTemplateId);

    private void OnMapTemplateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MapTemplateBox.SelectedItem is MapTemplate template)
        {
            _settings.MapTemplateId = template.Id;
            _settings.Save();
        }

        if (_map is { IsEmpty: false } && !_busy)
        {
            _ = ShowMapAsync();
        }
    }

    private void OnPdfTemplateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PdfTemplateBox.SelectedItem is PdfTemplate template)
        {
            _settings.PdfTemplateId = template.Id;
            _settings.Save();
        }
    }

    private static string Describe(MapOrientation orientation) => orientation switch
    {
        MapOrientation.TopBottom => "dall'alto in basso",
        MapOrientation.BottomTop => "dal basso in alto",
        MapOrientation.LeftRight => "da sinistra a destra",
        MapOrientation.RightLeft => "da destra a sinistra",
        MapOrientation.Radial => "radiale",
        MapOrientation.Timeline => "linea del tempo",
        MapOrientation.Matrix => "griglia",
        _ => "spina di pesce",
    };

    private static string Describe(MapPalette palette) => palette switch
    {
        MapPalette.ScuroCaldo => "scura calda",
        MapPalette.ScuroFreddo => "scura fredda",
        MapPalette.ChiaroProfessionale => "chiara professionale",
        MapPalette.Pastello => "pastello",
        MapPalette.StampaBiancoNero => "stampa in bianco e nero",
        _ => "alto contrasto",
    };

    private void LoadNote()
    {
        try
        {
            using var store = new SessionStore();
            var note = store.GetNote(_sessionId);
            if (note is null || note.IsEmpty)
            {
                SetStatus("Nessun contenuto IA salvato: usa i pulsanti qui sopra per generarlo.");
                return;
            }

            SummaryText.Text = note.Summary.Length > 0 ? note.Summary : "Nessun riassunto.";
            KeyPointsText.Text = note.KeyPoints.Length > 0 ? note.KeyPoints : "Nessun punto chiave.";

            if (note.ConceptMapJson.Length > 0)
            {
                _map = ConceptMap.Parse(note.ConceptMapJson);
                _ = ShowMapAsync();
            }

            SetStatus($"Contenuti generati con {note.Provider} il {note.UpdatedAt:dd/MM/yyyy HH:mm}.");
        }
        catch (Exception exception)
        {
            SetStatus($"Storico non disponibile: {exception.Message}");
        }
    }

    private string Transcript => string.Join(
        Environment.NewLine,
        _cues.Select(cue =>
        {
            // Il nome del parlante entra nel testo dato all'IA: così i riassunti distinguono chi
            // ha detto cosa senza inventare ruoli.
            var who = cue.HasSpeaker ? $"{cue.SpeakerLabel}: " : string.Empty;
            return cue.Translation.Trim().Length > 0
                ? $"{who}{cue.Original}{Environment.NewLine}{cue.Translation}"
                : who + cue.Original;
        }));

    private async void OnSummaryClicked(object sender, RoutedEventArgs e) =>
        await RunAiAsync(AiTask.ShortSummary, text => SummaryText.Text = text, "Riassunto");

    private async void OnDetailedSummaryClicked(object sender, RoutedEventArgs e) =>
        await RunAiAsync(AiTask.DetailedSummary, text => SummaryText.Text = text, "Riassunto dettagliato");

    private async void OnKeyPointsClicked(object sender, RoutedEventArgs e) =>
        await RunAiAsync(AiTask.KeyPoints, text => KeyPointsText.Text = text, "Punti chiave");

    private async void OnConceptMapClicked(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetButtonsEnabled(false);
        SetStatus($"Genero la mappa con {_settings.Ai.DisplayName} ({_settings.Ai.Model})…");

        try
        {
            using var assistant = new AiAssistant(_settings.Ai);
            var template = SelectedMapTemplate();
            SetStatus($"Genero «{template.Name}» con {_settings.Ai.DisplayName} ({_settings.Ai.Model})…");
            _map = await assistant.BuildConceptMapAsync(template, Transcript);
            if (_map.IsEmpty)
            {
                SetStatus("Il modello non ha restituito una mappa valida. Riprova o cambia modello.");
                return;
            }

            await ShowMapAsync();
            SaveNote(conceptMapJson: System.Text.Json.JsonSerializer.Serialize(
                _map,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            SetStatus($"Mappa generata con {assistant.ProviderName}: {_map.Nodes.Count} nodi, {_map.Edges.Count} relazioni.");
            AiTabs.SelectedIndex = 2;
        }
        catch (Exception exception)
        {
            SetStatus($"Errore IA: {exception.Message}");
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
        }
    }

    private async Task RunAiAsync(AiTask task, Action<string> apply, string description)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetButtonsEnabled(false);
        SetStatus($"{description} con {_settings.Ai.DisplayName} ({_settings.Ai.Model})…");

        try
        {
            using var assistant = new AiAssistant(_settings.Ai);
            var text = await assistant.RunAsync(task, Transcript);
            apply(text);
            AiTabs.SelectedIndex = task == AiTask.KeyPoints ? 1 : 0;

            SaveNote(
                summary: task is AiTask.ShortSummary or AiTask.DetailedSummary ? text : null,
                keyPoints: task == AiTask.KeyPoints ? text : null);

            SetStatus($"{description} generato con {assistant.ProviderName} ({_settings.Ai.Model}).");
        }
        catch (Exception exception)
        {
            SetStatus($"Errore IA: {exception.Message}");
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
        }
    }

    private void SaveNote(string? summary = null, string? keyPoints = null, string? conceptMapJson = null)
    {
        try
        {
            using var store = new SessionStore();
            store.SaveNote(_sessionId, $"{_settings.Ai.DisplayName} · {_settings.Ai.Model}", summary, keyPoints, conceptMapJson);
        }
        catch (Exception exception)
        {
            SetStatus($"Contenuto generato ma non salvato: {exception.Message}");
        }
    }

    private async Task ShowMapAsync()
    {
        if (_map is null || _map.IsEmpty)
        {
            return;
        }

        var template = SelectedMapTemplate();
        byte[]? png = null;
        if (!GraphvizRuntime.IsInstalled)
        {
            SetStatus("Scarico Graphviz per disegnare la mappa (9 MB)…");
            var download = new Progress<double>(value => SetStatus($"Scarico Graphviz… {value:P0}"));
            try
            {
                await GraphvizRuntime.EnsureAsync(download);
            }
            catch (Exception exception)
            {
                SetStatus($"Graphviz non disponibile ({exception.Message}): uso il disegno interno.");
            }
        }

        if (GraphvizRuntime.IsInstalled)
        {
            SetStatus($"Disegno «{template.Name}» con Graphviz…");
            png = await GraphvizRenderer.RenderPngAsync(_map, template);
        }

        png ??= GraphvizRenderer.RenderFallbackPng(_map);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = new MemoryStream(png);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        MapImage.Source = image;
        if (GraphvizRuntime.IsInstalled && png is not null)
        {
            SetStatus($"Mappa «{template.Name}»: {_map.Nodes.Count} nodi, {_map.Edges.Count} relazioni.");
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        SummaryButton.IsEnabled = enabled;
        IsEnabled = true;
    }

    private async void OnExportPdfClicked(object sender, RoutedEventArgs e)
    {
        var template = SelectedPdfTemplate();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Esporta il report PDF",
            Filter = "PDF|*.pdf",
            FileName = $"wisper-{template.Id}-{_sessionId}-{_session.StartedAt:yyyyMMdd}.pdf",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _busy = true;
        SetButtonsEnabled(false);
        try
        {
            SessionNote? note = null;
            using (var store = new SessionStore())
            {
                note = store.GetNote(_sessionId);
            }

            if (_map is null && note is { ConceptMapJson.Length: > 0 })
            {
                _map = ConceptMap.Parse(note.ConceptMapJson);
            }

            // Mappa vettoriale della stessa misura della pagina del report.
            string? mapPdf = null;
            if (_map is { IsEmpty: false })
            {
                var mapTemplate = SelectedMapTemplate();
                var page = template.Page.Landscape
                    ? new MapPageInches(11.69 - 1.3, 8.27 - 1.5, 11.69, 8.27)
                    : new MapPageInches(8.27 - 1.3, 11.69 - 1.5, 8.27, 11.69);
                var path = Path.Combine(AppPaths.EnsureSubdirectory("exports"), $"mappa-{_sessionId}-{mapTemplate.Id}.pdf");
                try
                {
                    if (await GraphvizRenderer.RenderPdfAsync(_map, mapTemplate, path, page))
                    {
                        mapPdf = path;
                    }
                }
                catch (Exception exception)
                {
                    SetStatus($"Mappa non vettoriale ({exception.Message}): uso l'immagine.");
                }
            }

            // Ogni sezione IA è una chiamata breve: il modello locale non si perde su testi lunghi.
            var generated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var aiSections = template.Sections.Where(section => section.Kind == PdfSectionKind.Ai).ToList();
            if (aiSections.Count > 0)
            {
                using var assistant = new AiAssistant(_settings.Ai);
                foreach (var section in aiSections)
                {
                    try
                    {
                        SetStatus($"Genero «{section.Title}» con {_settings.Ai.DisplayName}…");
                        generated[section.Id] = await assistant.RunTemplateAsync(section.Prompt, Transcript, section.MaxWords);
                    }
                    catch (Exception exception)
                    {
                        generated[section.Id] = string.Empty;
                        SetStatus($"Sezione «{section.Title}» saltata: {exception.Message}");
                    }
                }
            }

            SetStatus("Impagino il PDF…");
            PdfReportBuilder.Build(dialog.FileName, _session, _cues, note, _map, template, generated, mapPdf);

            var markdownPath = Path.ChangeExtension(dialog.FileName, ".md");
            await File.WriteAllTextAsync(
                markdownPath,
                PdfReportBuilder.BuildMarkdown(_session, _cues, note, template, generated));

            SetStatus($"PDF «{template.Name}» salvato in {dialog.FileName}"
                      + (mapPdf is null ? string.Empty : " (mappa vettoriale)")
                      + $" · Markdown: {markdownPath}");
        }
        catch (Exception exception)
        {
            SetStatus($"Errore nella creazione del PDF: {exception.Message}");
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
        }
    }

    private void OnExportSrtClicked(object sender, RoutedEventArgs e) =>
        Export("srt", "Sottotitoli SRT|*.srt", HistoryExporter.ToSrt(_cues));

    private void OnExportTextClicked(object sender, RoutedEventArgs e) =>
        Export("txt", "Testo|*.txt", HistoryExporter.ToText(_cues));

    private void OnExportJsonClicked(object sender, RoutedEventArgs e) =>
        Export("json", "JSON|*.json", HistoryExporter.ToJson(_session, _cues));

    private void Export(string extension, string filter, string content)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Esporta la sessione",
            Filter = filter,
            FileName = $"wisper-sessione-{_sessionId}.{extension}",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false));
        SetStatus($"Esportato in {dialog.FileName}");
    }

    private void OnCopyAllClicked(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(Transcript);
        SetStatus("Trascrizione copiata negli appunti.");
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo { FileName = AppPaths.Root, UseShellExecute = true });
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
