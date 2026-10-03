using System.Diagnostics;
using System.IO;
using System.Windows;
using WisperTranslator.Core;
using WisperTranslator.Core.Ai;
using WisperTranslator.Core.Asr;
using WisperTranslator.Core.Audio;
using WisperTranslator.Core.Hardware;
using WisperTranslator.Core.History;
using WisperTranslator.Core.Models;
using WisperTranslator.Core.Settings;
using WisperTranslator.Core.Translation;
using WisperTranslator.Core.Rendering;
using WisperTranslator.Core.Templates;
using Wpf.Ui.Controls;

namespace WisperTranslator.App.Windows;

/// <summary>
/// Indici delle schede delle impostazioni: un solo posto da aggiornare quando l'ordine cambia,
/// così le scorciatoie <c>--settings=n</c> e i menu restano allineati.
/// </summary>
public static class SettingsTabs
{
    public const int Aspetto = 0;
    public const int Prestazioni = 1;
    public const int Conversazione = 2;
    public const int Barra = 3;
    public const int Modelli = 4;
    public const int Template = 5;
    public const int Storico = 6;
    public const int Ia = 7;
}

public partial class SettingsWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private readonly Action _onChanged;
    private bool _loading = true;
    private PerformancePreset _appliedPreset = PerformancePreset.Auto;

    public SettingsWindow(AppSettings settings, Action onChanged, int initialTab = 0)
    {
        _settings = settings;
        _onChanged = onChanged;

        InitializeComponent();
        LoadControls();
        LoadAiControls();
        LoadConversationControls();
        LoadBarControls();
        _loading = false;
        RefreshModels();
        RefreshHistory();
        RefreshTemplates();

        if (initialTab > 0)
        {
            SelectTab(initialTab);
        }
    }

    /// <summary>Seleziona una scheda (vedi <see cref="SettingsTabs"/>).</summary>
    public void SelectTab(int index) => Tabs.SelectedIndex = Math.Clamp(index, 0, Tabs.Items.Count - 1);

    // --- Conversazione (diarizzazione) ---

    private void LoadConversationControls()
    {
        ConversationCheck.IsChecked = _settings.ConversationMode;
        DiarizerBox.ItemsSource = NeMoModels.Diarizers
            .Select(entry => new DiarizerOption(entry.Id, $"{entry.DisplayName} · {entry.License}"))
            .ToList();
        DiarizerBox.SelectedIndex = Math.Max(
            0,
            ((List<DiarizerOption>)DiarizerBox.ItemsSource)
            .FindIndex(option => option.Id == _settings.DiarizerModelId));
        RefreshDiarizerHint();
    }

    private void RefreshDiarizerHint()
    {
        var entry = NeMoModels.DiarizerEntry(_settings.DiarizerModelId);
        var installed = NeMoModels.IsDiarizerInstalled(_settings.DiarizerModelId);
        var size = NeMoModels.DiarizerSize(_settings.DiarizerModelId);
        DiarizerHint.Text = installed
            ? $"{entry.About} Installato: {InstalledModel.FormatSize(size)}."
            : $"{entry.About} Da scaricare: {InstalledModel.FormatSize(entry.ExpectedSizeBytes)} circa.";
        DiarizerDownloadButton.IsEnabled = !installed;
    }

    private void OnConversationChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.ConversationMode = ConversationCheck.IsChecked == true;
        _onChanged();
    }

    private void OnDiarizerChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || DiarizerBox.SelectedItem is not DiarizerOption option)
        {
            return;
        }

        _settings.DiarizerModelId = option.Id;
        RefreshDiarizerHint();
        _onChanged();
    }

    private async void OnDownloadDiarizerClicked(object sender, RoutedEventArgs e)
    {
        var entry = NeMoModels.DiarizerEntry(_settings.DiarizerModelId);
        DiarizerDownloadButton.IsEnabled = false;
        DiarizerProgress.Visibility = Visibility.Visible;
        DiarizerProgress.Value = 0;

        try
        {
            var progress = new Progress<long>(done =>
            {
                DiarizerProgress.Value = entry.ExpectedSizeBytes > 0
                    ? Math.Min(100, done * 100.0 / entry.ExpectedSizeBytes)
                    : 0;
                DiarizerStatusText.Text = $"Scarico {entry.DisplayName}: {InstalledModel.FormatSize(done)}";
            });

            DiarizerStatusText.Text = $"Scarico {entry.DisplayName}…";
            await NeMoModels.EnsureDiarizerAsync(_settings.DiarizerModelId, progress);
            DiarizerStatusText.Text = "Diarizzatore installato e verificato.";
        }
        catch (Exception exception)
        {
            DiarizerStatusText.Text = $"Download non riuscito: {exception.Message}";
        }
        finally
        {
            DiarizerProgress.Visibility = Visibility.Collapsed;
            RefreshDiarizerHint();
            RefreshModels();
        }
    }

    private async void OnVerifyDiarizerClicked(object sender, RoutedEventArgs e)
    {
        var entry = NeMoModels.DiarizerEntry(_settings.DiarizerModelId);
        DiarizerStatusText.Text = "Verifico…";
        var (ok, message) = await ModelStore.VerifyAsync(entry);
        DiarizerStatusText.Text = $"{entry.DisplayName}: {message}" + (ok ? string.Empty : " — riscaricalo da qui.");
    }

    private sealed record DiarizerOption(string Id, string Label);

    // --- Barra fluttuante ---

    private void LoadBarControls()
    {
        BarEnabledCheck.IsChecked = _settings.StartWithBar;
        BarAnimationsCheck.IsChecked = _settings.BarAnimations;
        BarDiscreetCheck.IsChecked = _settings.BarDiscreet;
        BarRowsRow.Value = _settings.BarRows;
        BarBufferRow.Value = _settings.BarBuffer;

        BarTextBox.ItemsSource = new[] { "Originale e traduzione", "Solo originale", "Solo traduzione" };
        BarTextBox.SelectedIndex = (int)_settings.BarText;
        RefreshBarPreview();
    }

    private void OnBarChanged(object sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.StartWithBar = BarEnabledCheck.IsChecked == true;
        _settings.BarAnimations = BarAnimationsCheck.IsChecked == true;
        _settings.BarDiscreet = BarDiscreetCheck.IsChecked == true;
        _settings.BarRows = (int)Math.Round(BarRowsRow.Value);
        _settings.BarBuffer = Math.Max((int)Math.Round(BarBufferRow.Value), _settings.BarRows);
        _settings.BarText = (BarTextMode)Math.Clamp(BarTextBox.SelectedIndex, 0, 2);

        RefreshBarPreview();
        _onChanged();
    }

    private void RefreshBarPreview()
    {
        BarPreviewText.Text =
            $"La barra mostra {_settings.BarRows} frasi per volta su {_settings.BarBuffer} in memoria: "
            + $"le altre {Math.Max(0, _settings.BarBuffer - _settings.BarRows)} restano sotto, raggiungibili con la rotellina."
            + (_settings.BarDiscreet ? " Modalità discreta attiva." : string.Empty);
    }

    private void LoadControls()
    {
        FontRow.Value = _settings.FontSize;
        OpacityRow.Value = _settings.Opacity;
        MaxCuesRow.Value = _settings.MaxCues;
        OverlayCuesRow.Value = _settings.OverlayMaxCues;
        OverlayFontRow.Value = _settings.OverlayFontSize;
        RetentionRow.Value = Math.Clamp(_settings.RetentionDays, 0, 90);
        TopmostCheck.IsChecked = _settings.Topmost;
        ShowOriginalCheck.IsChecked = _settings.ShowOriginal;
        TranslateCheck.IsChecked = _settings.Translate;
        OverlayCaptureCheck.IsChecked = _settings.OverlayHiddenFromCapture;
        CloseToTrayCheck.IsChecked = _settings.CloseToTray;
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;

        PartialModelBox.ItemsSource = AsrModels.All;
        PartialModelBox.SelectedItem = AsrModels.All.First(model => model.Id == _settings.PartialModelId);
        FinalModelBox.ItemsSource = AsrModels.All;
        FinalModelBox.SelectedItem = AsrModels.All.First(model => model.Id == _settings.FinalModelId);
        ModelHint.Text = "Suggerimento: modello veloce per i parziali, accurato per le frasi finali. "
                         + "Su PC senza GPU conviene 'whisper-base-q5_1' per entrambi.";

        PresetBox.ItemsSource = new[]
        {
            new PresetOption(PerformancePreset.Auto, "Automatico (segue il PC)"),
            new PresetOption(PerformancePreset.Reattivo, "Reattivo — PC minimi"),
            new PresetOption(PerformancePreset.Equilibrato, "Equilibrato"),
            new PresetOption(PerformancePreset.Qualita, "Qualità — PC potenti"),
        };
        PresetBox.SelectedItem = ((IEnumerable<PresetOption>)PresetBox.ItemsSource)
            .First(option => option.Value == _settings.Preset);
        _appliedPreset = _settings.Preset;

        LiveBackendBox.ItemsSource = BackendOptions();
        LiveBackendBox.SelectedItem = ((IEnumerable<BackendOption>)LiveBackendBox.ItemsSource)
            .First(option => option.Value == _settings.LiveBackend);
        FinalBackendBox.ItemsSource = BackendOptions();
        FinalBackendBox.SelectedItem = ((IEnumerable<BackendOption>)FinalBackendBox.ItemsSource)
            .First(option => option.Value == _settings.FinalBackend);
        UpdatePresetHint();

        GpuBox.ItemsSource = new[]
        {
            new GpuOption(GpuRuntime.Nessuno, "Solo CPU (consigliato)"),
            new GpuOption(GpuRuntime.Vulkan, "Vulkan (sperimentale)"),
            new GpuOption(GpuRuntime.Cuda, "CUDA (sperimentale)"),
        };
        GpuBox.SelectedItem = ((IEnumerable<GpuOption>)GpuBox.ItemsSource)
            .First(option => option.Value == _settings.Gpu);

        SystemDeviceBox.ItemsSource = AudioDevices.List(SourceKind.System);
        SystemDeviceBox.SelectedItem = SystemDeviceBox.Items
            .Cast<AudioDeviceInfo>()
            .FirstOrDefault(device => device.Id == _settings.SystemDeviceId);
        SystemDeviceBox.SelectedIndex = SystemDeviceBox.SelectedIndex < 0 ? 0 : SystemDeviceBox.SelectedIndex;

        MicrophoneDeviceBox.ItemsSource = AudioDevices.List(SourceKind.Microphone);
        MicrophoneDeviceBox.SelectedItem = MicrophoneDeviceBox.Items
            .Cast<AudioDeviceInfo>()
            .FirstOrDefault(device => device.Id == _settings.MicrophoneDeviceId);
        MicrophoneDeviceBox.SelectedIndex = MicrophoneDeviceBox.SelectedIndex < 0 ? 0 : MicrophoneDeviceBox.SelectedIndex;
    }

    private void OnAppearanceChanged(object sender, RoutedEventArgs e) => ApplyAppearance();

    private void OnAppearanceChanged(object sender, EventArgs e) => ApplyAppearance();

    private static IReadOnlyList<BackendOption> BackendOptions() =>
    [
        new BackendOption(null, "Dal profilo"),
        new BackendOption(AsrBackend.WhisperCpu, "Whisper (CPU)"),
        new BackendOption(AsrBackend.Vosk, "Vosk (leggero)"),
        new BackendOption(AsrBackend.NeMoSpeech, "NeMo-Speech"),
    ];

    private sealed record PresetOption(PerformancePreset Value, string Label);

    private sealed record BackendOption(AsrBackend? Value, string Label);

    private void UpdatePresetHint()
    {
        var profile = _settings.ResolveProfile();
        PresetHint.Text = $"{profile.Label}: {profile.Note}";
        ProfileSummaryText.Text = DescribeProfile(profile);
    }

    /// <summary>Riepilogo leggibile: motore, modello e stato di installazione per ogni corsia.</summary>
    private string DescribeProfile(PerformanceProfile profile)
    {
        return $"Testo immediato: {DescribeEngine(profile.Live, live: true)}{Environment.NewLine}"
               + $"Frase definitiva: {DescribeEngine(profile.Final, live: false)}";
    }

    private string DescribeEngine(AsrBackend backend, bool live)
    {
        switch (backend)
        {
            case AsrBackend.Vosk:
                var installed = VoskModels.IsInstalled(_settings.SourceLanguage);
                var size = VoskModels.InstalledSize(_settings.SourceLanguage);
                return $"Vosk {VoskModels.ModelId(_settings.SourceLanguage)} · "
                       + (installed ? $"installato ({InstalledModel.FormatSize(size)})" : "da scaricare (~50 MB)");

            case AsrBackend.NeMoSpeech:
                return "NeMo Nemotron 3.5 streaming · "
                       + (NeMoModels.IsInstalled
                           ? $"installato ({InstalledModel.FormatSize(NeMoModels.InstalledSize)})"
                           : "da scaricare (~708 MB)")
                       + (NeMoSpeechServer.FindExecutable() is null ? " · runtime mancante" : string.Empty);

            default:
                var id = live ? _settings.PartialModelId : _settings.FinalModelId;
                var entry = ModelCatalog.ById(id);
                var present = ModelStore.IsInstalled(entry);
                return $"Whisper {id} · "
                       + (present
                           ? $"installato ({InstalledModel.FormatSize(ModelStore.InstalledSize(entry))})"
                           : "da scaricare");
        }
    }

    private sealed record GpuOption(GpuRuntime Value, string Label);

    private void OnGpuChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.Gpu = (GpuBox.SelectedItem as GpuOption)?.Value ?? GpuRuntime.Nessuno;
        ProfileStatusText.Text = _settings.Gpu == GpuRuntime.Nessuno
            ? "Accelerazione GPU disattivata: si usa la CPU."
            : "Runtime GPU non ancora verificato in questa versione: si continua a usare la CPU. "
              + "Il percorso verrà attivato solo dopo una misura che ne dimostri il guadagno.";
        _onChanged();
    }

    private void OnApplyProfileClicked(object sender, RoutedEventArgs e)
    {
        var profile = _settings.ResolveProfile();
        _settings.PartialModelId = profile.LiveWhisperModelId;
        _settings.FinalModelId = profile.FinalWhisperModelId;
        _loading = true;
        PartialModelBox.SelectedItem = AsrModels.All.FirstOrDefault(model => model.Id == _settings.PartialModelId);
        FinalModelBox.SelectedItem = AsrModels.All.FirstOrDefault(model => model.Id == _settings.FinalModelId);
        _loading = false;
        UpdatePresetHint();
        _onChanged();
        RefreshModels();
        ProfileStatusText.Text = $"Profilo \"{profile.Label}\" applicato.";
    }

    private async void OnVerifyProfileClicked(object sender, RoutedEventArgs e)
    {
        var profile = _settings.ResolveProfile();
        var lines = new List<string>();
        foreach (var id in new[] { profile.LiveWhisperModelId, profile.FinalWhisperModelId }.Distinct())
        {
            var (ok, message) = await ModelStore.VerifyAsync(ModelCatalog.ById(id));
            lines.Add($"Whisper {id}: {(ok ? "ok" : "PROBLEMA")} — {message}");
        }

        if (profile.Live == AsrBackend.Vosk || profile.Final == AsrBackend.Vosk)
        {
            lines.Add($"Vosk: {(VoskModels.IsInstalled(_settings.SourceLanguage) ? "installato" : "non installato")}");
        }

        if (profile.Live == AsrBackend.NeMoSpeech || profile.Final == AsrBackend.NeMoSpeech)
        {
            var (ok, message) = await ModelStore.VerifyAsync(ModelCatalog.NeMoRuntime);
            lines.Add($"Runtime NeMo: {(ok ? "ok" : "PROBLEMA")} — {message}");
            lines.Add($"Nemotron: {(NeMoModels.IsInstalled ? "installato" : "non installato")}");
        }

        lines.Add($"Graphviz (mappe): {(GraphvizRuntime.IsInstalled ? "installato" : "non installato")}");
        ProfileStatusText.Text = string.Join(Environment.NewLine, lines);
    }

    private void OnPresetChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var preset = (PresetBox.SelectedItem as PresetOption)?.Value ?? PerformancePreset.Auto;
        var presetChanged = preset != _appliedPreset;
        _appliedPreset = preset;
        _settings.Preset = preset;
        _settings.LiveBackend = (LiveBackendBox.SelectedItem as BackendOption)?.Value;
        _settings.FinalBackend = (FinalBackendBox.SelectedItem as BackendOption)?.Value;

        if (presetChanged)
        {
            var profile = _settings.ResolveProfile();
            _settings.PartialModelId = profile.LiveWhisperModelId;
            _settings.FinalModelId = profile.FinalWhisperModelId;
            _loading = true;
            PartialModelBox.SelectedItem = AsrModels.All.FirstOrDefault(model => model.Id == _settings.PartialModelId);
            FinalModelBox.SelectedItem = AsrModels.All.FirstOrDefault(model => model.Id == _settings.FinalModelId);
            _loading = false;
        }

        UpdatePresetHint();
        _onChanged();
        RefreshModels();
    }

    private async void OnDownloadProfileModelsClicked(object sender, RoutedEventArgs e)
    {
        var profile = _settings.ResolveProfile();
        try
        {
            foreach (var id in new[] { profile.LiveWhisperModelId, profile.FinalWhisperModelId }.Distinct())
            {
                ModelStatusText.Text = $"Preparo {id}…";
                await ModelStore.EnsureAsrModelAsync(AsrModels.FromId(id));
            }

            if (profile.Live == AsrBackend.Vosk || profile.Final == AsrBackend.Vosk)
            {
                var progress = new Progress<double>(value => ModelStatusText.Text = $"Scarico Vosk… {value:P0}");
                ModelStatusText.Text = "Scarico il modello Vosk…";
                await VoskModels.EnsureAsync(_settings.SourceLanguage, progress);
            }

            if (profile.Live == AsrBackend.NeMoSpeech || profile.Final == AsrBackend.NeMoSpeech)
            {
                ModelStatusText.Text = "Scarico il runtime NeMo-Speech…";
                await NeMoSpeechServer.EnsureExecutableAsync();
                if (!NeMoModels.IsInstalled)
                {
                    var progress = new Progress<double>(value =>
                        ModelStatusText.Text = $"Scarico Nemotron 3.5… {value:P0} (circa 708 MB)");
                    await NeMoModels.EnsureAsync(progress);
                }
            }

            if (!GraphvizRuntime.IsInstalled)
            {
                var progress = new Progress<double>(value => ModelStatusText.Text = $"Scarico Graphviz… {value:P0}");
                ModelStatusText.Text = "Scarico Graphviz per le mappe…";
                await GraphvizRuntime.EnsureAsync(progress);
            }

            ModelStatusText.Text = "Componenti del profilo pronti.";
        }
        catch (Exception exception)
        {
            ModelStatusText.Text = $"Download non riuscito: {exception.Message}";
        }

        RefreshModels();
    }

    private void OnAppearanceChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyAppearance();

    // ---------- Template ----------

    private sealed record TemplateRow(string Name, string Kind, string Summary, string Source, object Template);

    private void OnRefreshTemplatesClicked(object sender, RoutedEventArgs e) => RefreshTemplates();

    /// <summary>Aprendo la scheda Template mostra subito l'anteprima del primo template di mappa.</summary>
    private void OnTabChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading || TemplatePreview.Source is not null)
        {
            return;
        }

        if (Tabs.SelectedIndex != SettingsTabs.Template || TemplatesList.Items.Count == 0)
        {
            return;
        }

        if (TemplatesList.Items[0] is TemplateRow row)
        {
            _ = PreviewTemplateAsync(row);
        }
    }

    private void RefreshTemplates()
    {
        var rows = new List<TemplateRow>();
        rows.AddRange(TemplateStore.Maps().Select(template => new TemplateRow(
            template.Name,
            "Mappa",
            template.Summary,
            DescribeSource(template.SourceUrl, template.Author, template.License),
            template)));
        rows.AddRange(TemplateStore.Pdfs().Select(template => new TemplateRow(
            template.Name,
            "PDF",
            template.Summary,
            DescribeSource(template.SourceUrl, template.Author, template.License),
            template)));

        TemplatesList.ItemsSource = rows;
        TemplateStatusText.Text = $"Mappe: {TemplateStore.Maps().Count} · Report PDF: {TemplateStore.Pdfs().Count} · "
                                  + $"Graphviz: {(GraphvizRuntime.IsInstalled ? "installato" : "non installato")}";
    }

    private static string DescribeSource(string? url, string? author, string license)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(author))
        {
            parts.Add($"di {author}");
        }

        parts.Add(license);
        if (!string.IsNullOrWhiteSpace(url))
        {
            parts.Add(url);
        }

        return string.Join(" · ", parts);
    }

    private async void OnPreviewTemplateClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TemplateRow row)
        {
            return;
        }

        await PreviewTemplateAsync(row);
    }

    private async Task PreviewTemplateAsync(TemplateRow row)
    {
        if (row.Template is MapTemplate map)
        {
            try
            {
                if (!GraphvizRuntime.IsInstalled)
                {
                    TemplateStatusText.Text = "Scarico Graphviz (9 MB)…";
                    var download = new Progress<double>(value => TemplateStatusText.Text = $"Scarico Graphviz… {value:P0}");
                    await GraphvizRuntime.EnsureAsync(download);
                }

                TemplateStatusText.Text = $"Disegno «{map.Name}»…";
                var png = await GraphvizRenderer.RenderPngAsync(SampleContent.Map(), map);
                if (png is null)
                {
                    png = GraphvizRenderer.RenderFallbackPng(SampleContent.Map());
                    TemplateStatusText.Text = $"Anteprima con il disegno interno ({GraphvizRuntime.LastError})";
                }
                else
                {
                    TemplateStatusText.Text = $"Anteprima di «{map.Name}» generata con Graphviz.";
                }

                var image = new System.Windows.Media.Imaging.BitmapImage();
                using (var stream = new MemoryStream(png))
                {
                    image.BeginInit();
                    image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                }

                TemplatePreview.Source = image;
            }
            catch (Exception exception)
            {
                TemplateStatusText.Text = $"Anteprima non riuscita: {exception.Message}";
            }

            return;
        }

        if (row.Template is PdfTemplate pdf)
        {
            try
            {
                var directory = AppPaths.EnsureSubdirectory("exports");
                var path = Path.Combine(directory, $"anteprima-{pdf.Id}.pdf");
                var session = new HistorySession(0, DateTime.Now, DateTime.Now, "en", "it", 0);
                PdfReportBuilder.Build(path, session, [], null, null, pdf);
                TemplateStatusText.Text = $"Anteprima PDF salvata in {path}";
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception exception)
            {
                TemplateStatusText.Text = $"Anteprima non riuscita: {exception.Message}";
            }
        }
    }

    private void OnExportTemplateClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TemplateRow row)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Esporta template",
            FileName = $"{row.Name}.json",
            Filter = "Template (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (row.Template is MapTemplate map)
        {
            TemplateStore.Export(map, dialog.FileName);
        }
        else if (row.Template is PdfTemplate pdf)
        {
            TemplateStore.Export(pdf, dialog.FileName);
        }

        TemplateStatusText.Text = $"Esportato in {dialog.FileName}";
    }

    private void OnDeleteTemplateClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TemplateRow row)
        {
            return;
        }

        if (row.Template is MapTemplate map)
        {
            TemplateStore.Delete(map);
        }
        else if (row.Template is PdfTemplate pdf)
        {
            TemplateStore.Delete(pdf);
        }

        TemplateStatusText.Text = $"«{row.Name}» eliminato (i template inclusi si possono ripristinare "
                                  + "reimportando il file JSON).";
        RefreshTemplates();
    }

    private void OnImportTemplateClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importa un template o uno skill",
            Filter = "Template (*.json;*.zip)|*.json;*.zip|Tutti i file (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var id = TemplateStore.Import(dialog.FileName);
            TemplateStatusText.Text = $"Importato «{id}»." + (TemplateStore.LastWarning is { } warning
                ? $" {warning}"
                : string.Empty);
            RefreshTemplates();
        }
        catch (Exception exception)
        {
            TemplateStatusText.Text = $"Importazione non riuscita: {exception.Message}";
        }
    }

    private void OnOpenTemplatesFolderClicked(object sender, RoutedEventArgs e)
    {
        TemplateStore.EnsureCreated();
        Process.Start(new ProcessStartInfo { FileName = TemplateStore.Root, UseShellExecute = true });
    }

    private void ApplyAppearance()
    {
        if (_loading)
        {
            return;
        }

        _settings.FontSize = FontRow.Value;
        _settings.Opacity = OpacityRow.Value;
        _settings.MaxCues = (int)MaxCuesRow.Value;
        _settings.OverlayMaxCues = (int)OverlayCuesRow.Value;
        _settings.OverlayFontSize = OverlayFontRow.Value;
        _settings.Topmost = TopmostCheck.IsChecked == true;
        _settings.ShowOriginal = ShowOriginalCheck.IsChecked == true;
        _settings.Translate = TranslateCheck.IsChecked == true;
        _settings.OverlayHiddenFromCapture = OverlayCaptureCheck.IsChecked == true;
        _settings.CloseToTray = CloseToTrayCheck.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.RetentionDays = (int)RetentionRow.Value;
        _onChanged();
    }

    private void LoadAiControls()
    {
        AiProviderBox.ItemsSource = Enum.GetValues<AiProviderKind>()
            .Select(kind => new AiProviderOption(kind, AiSettings.DefaultsFor(kind).Model.Length > 0
                ? Describe(kind)
                : kind.ToString()))
            .ToList();
        AiProviderBox.SelectedItem = AiProviderBox.Items
            .Cast<AiProviderOption>()
            .First(option => option.Kind == _settings.Ai.Provider);

        AiEndpointBox.Text = _settings.Ai.Endpoint;
        AiModelBox.Text = _settings.Ai.Model;
        AiKeyBox.Password = _settings.Ai.ApiKey;
        AiLanguageBox.SelectedIndex = _settings.Ai.OutputLanguage == "en" ? 1 : 0;
        AiTemperatureRow.Value = Math.Clamp(_settings.Ai.Temperature, 0, 1);
        AiMaxTokensBox.Text = _settings.Ai.MaxTokens.ToString();
        AiTimeoutBox.Text = _settings.Ai.TimeoutSeconds.ToString();
        AiSkipReasoningCheck.IsChecked = _settings.Ai.DisableReasoning;
        AiStatusText.Text = $"Provider: {_settings.Ai.DisplayName} · modello {_settings.Ai.Model}";
    }

    private static string Describe(AiProviderKind kind) => kind switch
    {
        AiProviderKind.Ollama => "Ollama (locale)",
        AiProviderKind.OpenAiCompatible => "Compatibile OpenAI (LM Studio, vLLM…)",
        AiProviderKind.OpenAi => "OpenAI",
        AiProviderKind.DeepSeek => "DeepSeek",
        AiProviderKind.OpenRouter => "OpenRouter",
        AiProviderKind.Groq => "Groq",
        AiProviderKind.Mistral => "Mistral",
        AiProviderKind.Gemini => "Google Gemini",
        AiProviderKind.Anthropic => "Anthropic Claude",
        _ => kind.ToString(),
    };

    private void OnAiProviderChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || AiProviderBox.SelectedItem is not AiProviderOption option)
        {
            return;
        }

        _settings.Ai.Provider = option.Kind;
        var (endpoint, model) = AiSettings.DefaultsFor(option.Kind);
        _settings.Ai.Endpoint = endpoint;
        _settings.Ai.Model = model;

        _loading = true;
        AiEndpointBox.Text = endpoint;
        AiModelBox.Text = model;
        _loading = false;

        AiStatusText.Text = $"Provider impostato su {_settings.Ai.DisplayName}.";
        _onChanged();
        AiTabsHint();
    }

    private void AiTabsHint()
    {
        // segnaposto per future note contestuali sul provider
    }

    private void OnAiTextChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.Ai.Endpoint = AiEndpointBox.Text.Trim();
        _settings.Ai.Model = AiModelBox.Text.Trim();
        _settings.Ai.OutputLanguage = AiLanguageBox.SelectedIndex == 1 ? "en" : "it";
        _settings.Ai.MaxTokens = int.TryParse(AiMaxTokensBox.Text, out var tokens) ? Math.Clamp(tokens, 128, 32_000) : _settings.Ai.MaxTokens;
        _settings.Ai.TimeoutSeconds = int.TryParse(AiTimeoutBox.Text, out var timeout) ? Math.Clamp(timeout, 10, 900) : _settings.Ai.TimeoutSeconds;
        _onChanged();
    }

    private void OnAiCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.Ai.DisableReasoning = AiSkipReasoningCheck.IsChecked == true;
        _onChanged();
    }

    private async void OnDetectModelsClicked(object sender, RoutedEventArgs e)
    {
        AiStatusText.Text = "Cerco i modelli installati…";

        try
        {
            var models = await OllamaDiscovery.ListModelsAsync(_settings.Ai.Endpoint);
            if (models.Count == 0)
            {
                AiStatusText.Text = "Nessun modello trovato: controlla che Ollama o LM Studio siano avviati.";
                return;
            }

            AiModelBox.ItemsSource = models;
            AiModelBox.Text = _settings.Ai.Model;
            AiStatusText.Text = $"{models.Count} modelli disponibili: {string.Join(", ", models.Take(6))}"
                                + (models.Count > 6 ? "…" : string.Empty);
        }
        catch (Exception exception)
        {
            AiStatusText.Text = $"Ricerca non riuscita: {exception.Message}";
        }
    }

    private void OnAiSliderChanged(object sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.Ai.Temperature = AiTemperatureRow.Value;
        _onChanged();
    }

    private void OnAiSecretChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.Ai.ApiKey = AiKeyBox.Password;
        _onChanged();
    }

    private async void OnTestAiClicked(object sender, RoutedEventArgs e)
    {
        AiTestButton.IsEnabled = false;
        AiStatusText.Text = $"Provo {_settings.Ai.DisplayName} ({_settings.Ai.Model})…";

        try
        {
            using var assistant = new AiAssistant(_settings.Ai);
            var answer = await assistant.TestAsync();
            AiStatusText.Text = $"Funziona: {assistant.ProviderName} ha risposto \"{answer}\".";
        }
        catch (Exception exception)
        {
            AiStatusText.Text = $"Non funziona: {exception.Message}";
        }
        finally
        {
            AiTestButton.IsEnabled = true;
        }
    }

    private void OnAiPresetsClicked(object sender, RoutedEventArgs e)
    {
        var text = string.Join(
            Environment.NewLine,
            Enum.GetValues<AiProviderKind>().Select(kind =>
            {
                var (endpoint, model) = AiSettings.DefaultsFor(kind);
                return $"{Describe(kind)}\n    {endpoint}\n    modello: {model}";
            }));

        System.Windows.MessageBox.Show(
            this,
            text,
            "Provider supportati",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private void OnModelChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || PartialModelBox.SelectedItem is not AsrModelSpec partial
            || FinalModelBox.SelectedItem is not AsrModelSpec final)
        {
            return;
        }

        _settings.PartialModelId = partial.Id;
        _settings.FinalModelId = final.Id;
        _onChanged();
    }

    private void OnDeviceChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.SystemDeviceId = (SystemDeviceBox.SelectedItem as AudioDeviceInfo)?.Id;
        _settings.MicrophoneDeviceId = (MicrophoneDeviceBox.SelectedItem as AudioDeviceInfo)?.Id;
        _onChanged();
    }

    private void RefreshModels()
    {
        var profile = HardwareDetector.Detect();
        HardwareText.Text = profile.Summary;

        ModelsList.ItemsSource = ModelCatalog.All
            .Select(entry =>
            {
                var installed = ModelStore.IsInstalled(entry);
                var size = ModelStore.InstalledSize(entry);
                return new ModelRow(
                    entry,
                    entry.DisplayName,
                    size > 0 ? InstalledModel.FormatSize(size) : "non installato",
                    $"{entry.License} · tier {entry.Tier}"
                    + (entry.Packaging == ModelPackaging.SingleFile ? string.Empty : " · pacchetto")
                    + (installed ? " · installato" : string.Empty)
                    + (entry.ManagedByServer ? " · gestito dal server" : string.Empty),
                    entry.About);
            })
            .ToList();

        ModelFolderText.Text = $"Cartella modelli: {AppPaths.ModelsDirectory} "
                               + $"({ModelStore.DirectorySize(AppPaths.ModelsDirectory) / (1024.0 * 1024):F0} MB in totale)";
    }

    private void OnRefreshModelsClicked(object sender, RoutedEventArgs e) => RefreshModels();

    private void OnApplyRecommendedClicked(object sender, RoutedEventArgs e)
    {
        var (partial, final) = HardwareDetector.Detect().RecommendedModels;
        _settings.PartialModelId = partial;
        _settings.FinalModelId = final;
        _loading = true;
        PartialModelBox.SelectedItem = AsrModels.All.FirstOrDefault(model => model.Id == partial);
        FinalModelBox.SelectedItem = AsrModels.All.FirstOrDefault(model => model.Id == final);
        _loading = false;
        _onChanged();
        ModelStatusText.Text = $"Modelli impostati su {partial} (parziali) e {final} (finali).";
    }

    private async void OnModelDownloadClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModelRow row)
        {
            return;
        }

        await RunModelOperationAsync(
            row.Entry,
            $"Download di {row.Name}...",
            async progress =>
            {
                if (row.Entry.ManagedByServer)
                {
                    var executable = await TranslationServer.EnsureExecutableAsync();
                    using var server = new TranslationServer(executable);
                    foreach (var pair in ModelStore.TranslationPairs)
                    {
                        await server.DownloadModelsAsync([pair]);
                    }

                    return;
                }

                if (row.Entry.Packaging == ModelPackaging.SingleFile)
                {
                    await ModelStore.EnsureAsync(row.Entry, progress);
                }
                else
                {
                    var packProgress = new Progress<double>(value =>
                        progress.Report((long)(value * Math.Max(1, row.Entry.ExpectedSizeBytes))));
                    await ModelStore.EnsurePackAsync(row.Entry, packProgress);
                }
            });
    }

    private async void OnModelVerifyClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModelRow row)
        {
            return;
        }

        await RunModelOperationAsync(
            row.Entry,
            $"Verifica di {row.Name}...",
            async _ =>
            {
                var (ok, message) = await ModelStore.VerifyAsync(row.Entry);
                ModelStatusText.Text = $"{row.Name}: {(ok ? "integro" : "PROBLEMA")} — {message}";
            });
    }

    private void OnModelDeleteClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModelRow row)
        {
            return;
        }

        ModelStore.Delete(row.Entry);
        ModelStatusText.Text = $"{row.Name}: eliminato.";
        RefreshModels();
    }

    private async void OnImportModelClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importa un modello locale",
            Filter = "Modelli (*.bin;*.onnx)|*.bin;*.onnx|Tutti i file (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var entry = ModelCatalog.Asr.First();
        try
        {
            var destination = await ModelStore.ImportAsync(dialog.FileName, entry);
            ModelStatusText.Text = $"Importato in {destination}.";
        }
        catch (Exception exception)
        {
            ModelStatusText.Text = $"Import non riuscito: {exception.Message}";
        }
        finally
        {
            RefreshModels();
        }
    }

    private async Task RunModelOperationAsync(
        ModelCatalogEntry entry,
        string description,
        Func<IProgress<long>, Task> operation)
    {
        ModelProgress.Visibility = Visibility.Visible;
        ModelProgress.Value = 0;
        ModelStatusText.Text = description;

        var progress = new Progress<long>(bytes =>
        {
            var expected = entry.ExpectedSizeBytes;
            ModelProgress.Value = expected > 0 ? Math.Min(100, bytes * 100.0 / expected) : 0;
            ModelStatusText.Text = $"{description} {bytes / (1024.0 * 1024):F1} MB";
        });

        try
        {
            await operation(progress);
            ModelStatusText.Text = "Operazione completata.";
        }
        catch (Exception exception)
        {
            ModelStatusText.Text = $"Errore: {exception.Message}";
        }
        finally
        {
            ModelProgress.Visibility = Visibility.Collapsed;
            RefreshModels();
        }
    }

    private sealed record ModelRow(
        ModelCatalogEntry Entry,
        string Name,
        string Size,
        string Detail,
        string About);

    private void RefreshHistory()
    {
        try
        {
            using var store = new SessionStore();
            var sessions = store.Sessions(40);

            HistoryList.ItemsSource = sessions.Select(session =>
            {
                var cues = store.Cues(session.Id).Where(cue => cue.IsFinal).Take(3).ToList();
                var preview = cues.Count == 0
                    ? "(nessuna battuta finale)"
                    : string.Join(" · ", cues.Select(cue => Truncate(
                        cue.Translation.Length > 0 ? cue.Translation : cue.Original, 60)));
                var state = session.EndedAt is null ? " · in corso" : string.Empty;
                return new HistoryRow(
                    session,
                    $"{session.StartedAt:dd/MM/yyyy HH:mm} · {session.SourceLanguage}→{session.TargetLanguage} · "
                    + $"{session.CueCount} battute{state}",
                    preview);
            }).ToList();

            HistorySummary.Text = sessions.Count == 0
                ? "Nessuna sessione registrata finora."
                : $"{sessions.Count} sessioni · retention {store.RetentionDays} giorni · "
                  + $"{store.TotalSizeBytes() / (1024.0 * 1024):F1} MB";
        }
        catch (Exception exception)
        {
            HistorySummary.Text = $"Storico non disponibile: {exception.Message}";
            HistoryList.ItemsSource = null;
        }
    }

    private void OnRefreshHistoryClicked(object sender, RoutedEventArgs e) => RefreshHistory();

    private void OnPurgeHistoryClicked(object sender, RoutedEventArgs e)
    {
        using var store = new SessionStore();
        var removed = store.PurgeExpired();
        ModelStatusText.Text = removed > 0
            ? $"Rimosse {removed} sessioni più vecchie di {store.RetentionDays} giorni."
            : "Nessuna sessione scaduta da rimuovere.";
        RefreshHistory();
    }

    private void OnExportSrtClicked(object sender, RoutedEventArgs e) =>
        ExportHistory("srt", "Sottotitoli SRT|*.srt", sender);

    private void OnExportTextClicked(object sender, RoutedEventArgs e) =>
        ExportHistory("txt", "Testo|*.txt", sender);

    private void OnExportJsonClicked(object sender, RoutedEventArgs e) =>
        ExportHistory("json", "JSON|*.json", sender);

    private void ExportHistory(string format, string filter, object? sender = null)
    {
        if ((sender as FrameworkElement)?.DataContext is not HistoryRow row)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Esporta la sessione",
            Filter = filter,
            FileName = $"wisper-{row.Session.StartedAt:yyyyMMdd-HHmm}.{format}",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        using var store = new SessionStore();
        var cues = store.Cues(row.Session.Id);
        var content = format switch
        {
            "srt" => HistoryExporter.ToSrt(cues),
            "json" => HistoryExporter.ToJson(row.Session, cues),
            _ => HistoryExporter.ToText(cues),
        };

        File.WriteAllText(dialog.FileName, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        ModelStatusText.Text = $"Esportato in {dialog.FileName}";
    }

    private static string Truncate(string text, int length) =>
        text.Length <= length ? text : text[..length] + "…";

    private sealed record HistoryRow(HistorySession Session, string Header, string Preview);

    private sealed record AiProviderOption(AiProviderKind Kind, string Name);

    private void OnOpenSessionClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HistoryRow row)
        {
            return;
        }

        using var store = new SessionStore();
        var cues = store.Cues(row.Session.Id);
        if (cues.Count == 0)
        {
            ModelStatusText.Text = "La sessione non contiene battute salvate.";
            return;
        }

        var window = new SessionDetailWindow(_settings, row.Session, cues)
        {
            Owner = this,
        };
        window.Show();
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.Root,
            UseShellExecute = true,
        });
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
