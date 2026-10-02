using System.Diagnostics;
using System.IO;
using System.Windows;
using WisperTranslator.Core;
using WisperTranslator.Core.Ai;
using WisperTranslator.Core.Audio;
using WisperTranslator.Core.Hardware;
using WisperTranslator.Core.History;
using WisperTranslator.Core.Models;
using WisperTranslator.Core.Settings;
using WisperTranslator.Core.Translation;
using Wpf.Ui.Controls;

namespace WisperTranslator.App.Windows;

public partial class SettingsWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private readonly Action _onChanged;
    private bool _loading = true;

    public SettingsWindow(AppSettings settings, Action onChanged, int initialTab = 0)
    {
        _settings = settings;
        _onChanged = onChanged;

        InitializeComponent();
        LoadControls();
        LoadAiControls();
        _loading = false;
        RefreshModels();
        RefreshHistory();

        if (initialTab > 0)
        {
            SelectTab(initialTab);
        }
    }

    /// <summary>Seleziona una scheda (0 aspetto, 1 modelli, 2 storico, 3 IA).</summary>
    public void SelectTab(int index) => Tabs.SelectedIndex = Math.Clamp(index, 0, Tabs.Items.Count - 1);

    private void LoadControls()
    {
        FontSlider.Value = _settings.FontSize;
        OpacitySlider.Value = _settings.Opacity;
        MaxCuesSlider.Value = _settings.MaxCues;
        OverlayCuesSlider.Value = _settings.OverlayMaxCues;
        OverlayFontSlider.Value = _settings.OverlayFontSize;
        TopmostCheck.IsChecked = _settings.Topmost;
        ShowOriginalCheck.IsChecked = _settings.ShowOriginal;
        TranslateCheck.IsChecked = _settings.Translate;
        OverlayCaptureCheck.IsChecked = _settings.OverlayHiddenFromCapture;
        CloseToTrayCheck.IsChecked = _settings.CloseToTray;
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        RetentionSlider.Value = Math.Clamp(_settings.RetentionDays, 0, 90);

        PartialModelBox.ItemsSource = AsrModels.All;
        PartialModelBox.SelectedItem = AsrModels.All.First(model => model.Id == _settings.PartialModelId);
        FinalModelBox.ItemsSource = AsrModels.All;
        FinalModelBox.SelectedItem = AsrModels.All.First(model => model.Id == _settings.FinalModelId);
        ModelHint.Text = "Suggerimento: modello veloce per i parziali, accurato per le frasi finali. "
                         + "Su PC senza GPU conviene 'whisper-base-q5_1' per entrambi.";

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

    private void OnAppearanceChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyAppearance();

    private void ApplyAppearance()
    {
        if (_loading)
        {
            return;
        }

        _settings.FontSize = FontSlider.Value;
        _settings.Opacity = OpacitySlider.Value;
        _settings.MaxCues = (int)MaxCuesSlider.Value;
        _settings.OverlayMaxCues = (int)OverlayCuesSlider.Value;
        _settings.OverlayFontSize = OverlayFontSlider.Value;
        _settings.Topmost = TopmostCheck.IsChecked == true;
        _settings.ShowOriginal = ShowOriginalCheck.IsChecked == true;
        _settings.Translate = TranslateCheck.IsChecked == true;
        _settings.OverlayHiddenFromCapture = OverlayCaptureCheck.IsChecked == true;
        _settings.CloseToTray = CloseToTrayCheck.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.RetentionDays = (int)RetentionSlider.Value;
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
        AiTemperatureSlider.Value = Math.Clamp(_settings.Ai.Temperature, 0, 1);
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

    private void OnAiSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        _settings.Ai.Temperature = AiTemperatureSlider.Value;
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

                await ModelStore.EnsureAsync(row.Entry, progress);
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
