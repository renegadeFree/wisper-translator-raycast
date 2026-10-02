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
                cue.Original,
                cue.Translation,
            })
            .ToList();

        LoadNote();
    }

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
                ShowMap();
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
        _cues.Select(cue => cue.Translation.Trim().Length > 0
            ? $"{cue.Original}{Environment.NewLine}{cue.Translation}"
            : cue.Original));

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
            _map = await assistant.BuildConceptMapAsync(Transcript);
            if (_map.IsEmpty)
            {
                SetStatus("Il modello non ha restituito una mappa valida. Riprova o cambia modello.");
                return;
            }

            ShowMap();
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

    private void ShowMap()
    {
        if (_map is null || _map.IsEmpty)
        {
            return;
        }

        var png = ConceptMapRenderer.RenderPng(_map);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = new MemoryStream(png);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        MapImage.Source = image;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        SummaryButton.IsEnabled = enabled;
        IsEnabled = true;
    }

    private void OnExportPdfClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Esporta il report PDF",
            Filter = "PDF|*.pdf",
            FileName = $"wisper-sessione-{_sessionId}-{_session.StartedAt:yyyyMMdd}.pdf",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

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

            PdfReportBuilder.Build(dialog.FileName, _session, _cues, note, _map);
            SetStatus($"PDF salvato in {dialog.FileName}");
        }
        catch (Exception exception)
        {
            SetStatus($"Errore nella creazione del PDF: {exception.Message}");
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
