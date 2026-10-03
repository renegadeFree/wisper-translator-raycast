using System.IO;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using WisperTranslator.Core.Ai;
using WisperTranslator.Core.History;
using WisperTranslator.Core.Session;
using WisperTranslator.Core.Templates;

namespace WisperTranslator.Core.Rendering;

/// <summary>
/// Costruisce il PDF di una sessione seguendo il template scelto: copertina, sezioni IA,
/// mappa (vettoriale quando Graphviz è disponibile) e trascrizione bilingue.
/// </summary>
public static class PdfReportBuilder
{
    public static void Build(
        string outputPath,
        HistorySession session,
        IReadOnlyList<HistoryCue> cues,
        SessionNote? note,
        ConceptMap? map) =>
        Build(outputPath, session, cues, note, map, TemplateStore.PdfOrDefault(null));

    public static void Build(
        string outputPath,
        HistorySession session,
        IReadOnlyList<HistoryCue> cues,
        SessionNote? note,
        ConceptMap? map,
        PdfTemplate template,
        IReadOnlyDictionary<string, string>? generatedSections = null,
        string? mapPdfPath = null)
    {
        WindowsFontResolver.EnsureRegistered();

        using var document = new PdfDocument();
        document.Info.Title = $"{template.Name} — sessione {session.Id}";
        document.Info.Author = "Wisper Translator";

        var writer = new Writer(document, template, note);
        if (template.Style.Cover)
        {
            writer.Cover(session, cues);
        }

        if (template.Style.TableOfContents)
        {
            writer.TableOfContents(template);
        }

        foreach (var section in template.Sections)
        {
            switch (section.Kind)
            {
                case PdfSectionKind.Map:
                    if (map is { IsEmpty: false })
                    {
                        writer.MapPage(section, map, mapPdfPath);
                    }

                    break;

                case PdfSectionKind.Transcript:
                    writer.Heading(section.Title);
                    writer.Transcript(cues, section.ShowOriginal, section.ShowTimestamps);
                    break;

                case PdfSectionKind.Speakers:
                    writer.Speakers(section.Title, cues);
                    break;

                case PdfSectionKind.Summary:
                    if (note is { Summary.Length: > 0 })
                    {
                        writer.Heading(section.Title);
                        writer.Text(note.Summary);
                    }

                    break;

                case PdfSectionKind.KeyPoints:
                    if (note is { KeyPoints.Length: > 0 })
                    {
                        writer.Heading(section.Title);
                        writer.Text(note.KeyPoints);
                    }

                    break;

                default:
                    if (generatedSections is not null
                        && generatedSections.TryGetValue(section.Id, out var text)
                        && !string.IsNullOrWhiteSpace(text))
                    {
                        writer.Heading(section.Title);
                        writer.Text(text);
                    }

                    break;
            }
        }

        writer.Finish(session, template);
        document.Save(outputPath);
    }

    /// <summary>Versione Markdown modificabile, salvata accanto al PDF.</summary>
    public static string BuildMarkdown(
        HistorySession session,
        IReadOnlyList<HistoryCue> cues,
        SessionNote? note,
        PdfTemplate template,
        IReadOnlyDictionary<string, string>? generatedSections = null)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {template.Name} — sessione {session.Id}");
        text.AppendLine();
        text.AppendLine($"- Data: {session.StartedAt:dd/MM/yyyy HH:mm}");
        text.AppendLine($"- Lingue: {session.SourceLanguage} → {session.TargetLanguage}");
        text.AppendLine($"- Battute: {cues.Count}");
        text.AppendLine();

        foreach (var section in template.Sections)
        {
            switch (section.Kind)
            {
                case PdfSectionKind.Summary when note is { Summary.Length: > 0 }:
                    text.AppendLine($"## {section.Title}").AppendLine().AppendLine(note.Summary).AppendLine();
                    break;

                case PdfSectionKind.KeyPoints when note is { KeyPoints.Length: > 0 }:
                    text.AppendLine($"## {section.Title}").AppendLine().AppendLine(note.KeyPoints).AppendLine();
                    break;

                case PdfSectionKind.Ai when generatedSections?.GetValueOrDefault(section.Id) is { Length: > 0 } ai:
                    text.AppendLine($"## {section.Title}").AppendLine().AppendLine(ai).AppendLine();
                    break;

                case PdfSectionKind.Map:
                    text.AppendLine($"## {section.Title}").AppendLine()
                        .AppendLine("_La mappa è nel PDF._").AppendLine();
                    break;

                case PdfSectionKind.Transcript:
                    text.AppendLine($"## {section.Title}").AppendLine();
                    foreach (var cue in cues)
                    {
                        var stamp = section.ShowTimestamps ? $"[{cue.AudioStart:mm\\:ss}] " : string.Empty;
                        var who = cue.HasSpeaker ? $"**{cue.SpeakerLabel}:** " : string.Empty;
                        if (section.ShowOriginal)
                        {
                            text.AppendLine($"{stamp}{who}_{cue.Original}_");
                        }

                        if (cue.Translation.Length > 0)
                        {
                            text.AppendLine(who + cue.Translation);
                        }

                        text.AppendLine();
                    }

                    break;

                case PdfSectionKind.Speakers:
                    var totals = SpeakerTotals(cues);
                    if (totals.Count == 0)
                    {
                        break;
                    }

                    text.AppendLine($"## {section.Title}").AppendLine();
                    foreach (var speaker in totals)
                    {
                        text.AppendLine(
                            $"- {speaker.Label}: {speaker.Cues} battute, {speaker.Share:P0} del tempo di parola");
                    }

                    text.AppendLine();
                    break;
            }
        }

        if (note is { IsEmpty: false })
        {
            text.AppendLine("---").AppendLine($"Generato con {note.Provider} il {note.UpdatedAt:dd/MM/yyyy HH:mm}");
        }

        return text.ToString();
    }

    /// <summary>
    /// Conta le battute per parlante sommando le durate: è tutto calcolato in locale, senza IA.
    /// </summary>
    public static IReadOnlyList<SpeakerTotal> SpeakerTotals(IReadOnlyList<HistoryCue> cues)
    {
        var total = cues.Where(cue => cue.HasSpeaker).Sum(cue => cue.Duration.TotalSeconds);
        return
        [
            .. cues
                .Where(cue => cue.HasSpeaker)
                .GroupBy(cue => cue.Speaker)
                .Select(group => new SpeakerTotal(
                    group.Key,
                    Speakers.Label(group.Key, "Partecipanti"),
                    group.Count(),
                    group.Sum(cue => cue.Duration.TotalSeconds),
                    total > 0 ? group.Sum(cue => cue.Duration.TotalSeconds) / total : 0))
                .OrderByDescending(speaker => speaker.Seconds),
        ];
    }

    /// <summary>Impaginazione: stato della pagina corrente, font e colori del template.</summary>
    private sealed class Writer
    {
        private static readonly (double Width, double Height) A4Size = (595.28, 841.89);
        private static readonly (double Width, double Height) LetterSize = (612, 792);

        private readonly PdfDocument _document;
        private readonly SessionNote? _note;
        private readonly double _margin;
        private readonly (double Width, double Height) _size;
        private readonly XFont _title = new("Segoe UI", 22, XFontStyleEx.Bold);
        private readonly XFont _heading;
        private readonly XFont _body;
        private readonly XFont _italic;
        private readonly XFont _original;
        private readonly XFont _small = new("Segoe UI", 8, XFontStyleEx.Italic);
        private readonly XBrush _titleBrush;
        private readonly XBrush _headingBrush;
        private readonly XBrush _text = XBrushes.Black;
        private readonly XBrush _subtle;
        private readonly XColor _lineColor;

        private PdfPage? _page;
        private XGraphics? _graphics;
        private double _cursor;

        public Writer(PdfDocument document, PdfTemplate template, SessionNote? note)
        {
            _document = document;
            _note = note;
            _margin = template.Page.Margin;
            var size = template.Page.Size.Equals("Letter", StringComparison.OrdinalIgnoreCase) ? LetterSize : A4Size;
            _size = template.Page.Landscape ? (size.Height, size.Width) : size;

            var (title, heading, subtle) = Palette(template.Style.Palette);
            _titleBrush = new XSolidBrush(title);
            _headingBrush = new XSolidBrush(heading);
            _subtle = new XSolidBrush(subtle);
            _lineColor = subtle;
            _heading = new XFont("Segoe UI", template.Style.FontSize + 3.5, XFontStyleEx.Bold);
            _body = new XFont("Segoe UI", template.Style.FontSize);
            _italic = new XFont("Segoe UI", template.Style.FontSize - 0.5, XFontStyleEx.Italic);
            _original = new XFont("Segoe UI", template.Style.FontSize - 1);

            _page = AddPage();
            _graphics = XGraphics.FromPdfPage(_page);
            _cursor = _margin;
        }

        private double UsableWidth => _size.Width - (_margin * 2);

        private PdfPage AddPage()
        {
            var page = _document.AddPage();
            page.Width = XUnit.FromPoint(_size.Width);
            page.Height = XUnit.FromPoint(_size.Height);
            return page;
        }

        private static (XColor Title, XColor Heading, XColor Subtle) Palette(string name) =>
            name.ToLowerInvariant() switch
            {
                "chiaro" => (XColor.FromArgb(31, 78, 121), XColor.FromArgb(31, 78, 121), XColor.FromArgb(120, 120, 130)),
                "pastello" => (XColor.FromArgb(201, 138, 60), XColor.FromArgb(120, 90, 60), XColor.FromArgb(150, 130, 110)),
                "scuro" => (XColor.FromArgb(255, 156, 70), XColor.FromArgb(210, 100, 30), XColor.FromArgb(120, 120, 130)),
                _ => (XColor.FromArgb(210, 100, 30), XColor.FromArgb(31, 78, 121), XColor.FromArgb(110, 110, 120)),
            };

        private void EnsureSpace(double needed)
        {
            EnsurePage();
            if (_cursor + needed <= _size.Height - _margin)
            {
                return;
            }

            _graphics!.Dispose();
            _page = AddPage();
            _graphics = XGraphics.FromPdfPage(_page);
            _cursor = _margin;
        }

        /// <summary>Crea la pagina di flusso solo quando serve (dopo una mappa a pagina intera).</summary>
        private void EnsurePage()
        {
            if (_graphics is not null)
            {
                return;
            }

            _page = AddPage();
            _graphics = XGraphics.FromPdfPage(_page);
            _cursor = _margin;
        }

        public void Cover(HistorySession session, IReadOnlyList<HistoryCue> cues)
        {
            _cursor = _size.Height / 4;
            Draw("Wisper Translator", _small, _subtle, 0, 18);
            Draw(_note?.Provider is { Length: > 0 } ? "Report della sessione" : "Report della sessione", _title, _titleBrush, 0, 18);
            Draw($"Sessione #{session.Id} · {session.StartedAt:dd/MM/yyyy HH:mm}", _body, _text, 0, 8);
            var duration = cues.Count > 0 ? cues[^1].AudioStart + cues[^1].Duration : TimeSpan.Zero;
            Draw(
                $"{session.SourceLanguage} → {session.TargetLanguage} · {cues.Count} battute · "
                + $"{duration:hh\\:mm\\:ss} di audio",
                _body,
                _text,
                0,
                8);
            if (_note is { IsEmpty: false })
            {
                Draw($"Contenuti generati con {_note.Provider}", _italic, _subtle);
            }

            _graphics!.Dispose();
            _page = AddPage();
            _graphics = XGraphics.FromPdfPage(_page);
            _cursor = _margin;
        }

        public void TableOfContents(PdfTemplate template)
        {
            Heading("Indice");
            foreach (var section in template.Sections)
            {
                Draw($"• {section.Title}", _body, _text, 8, 3);
            }

            _cursor += 10;
        }

        public void Heading(string text)
        {
            EnsureSpace(40);
            _graphics!.DrawString(
                text,
                _heading,
                _headingBrush,
                new XRect(_margin, _cursor, UsableWidth, 24),
                XStringFormats.TopLeft);
            _cursor += 26;
        }

        public void Text(string text)
        {
            foreach (var paragraph in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                foreach (var line in Wrap(paragraph, _body, UsableWidth))
                {
                    EnsureSpace(14);
                    _graphics!.DrawString(line, _body, _text, new XRect(_margin, _cursor, UsableWidth, 14), XStringFormats.TopLeft);
                    _cursor += 14;
                }
            }

            _cursor += 8;
        }

        public void Transcript(IReadOnlyList<HistoryCue> cues, bool showOriginal, bool showTimestamps)
        {
            foreach (var cue in cues)
            {
                var stamp = showTimestamps ? $"[{cue.AudioStart:mm\\:ss}] " : string.Empty;
                var who = cue.HasSpeaker ? $"{cue.SpeakerLabel}: " : string.Empty;
                if (showOriginal && cue.Original.Length > 0)
                {
                    foreach (var line in Wrap(stamp + who + cue.Original, _original, UsableWidth))
                    {
                        EnsureSpace(13);
                        _graphics!.DrawString(line, _original, _subtle, new XRect(_margin, _cursor, UsableWidth, 13), XStringFormats.TopLeft);
                        _cursor += 13;
                    }
                }

                if (cue.Translation.Trim().Length > 0)
                {
                    foreach (var line in Wrap(who + cue.Translation, _body, UsableWidth - 12))
                    {
                        EnsureSpace(14);
                        _graphics!.DrawString(line, _body, _text, new XRect(_margin + 12, _cursor, UsableWidth - 12, 14), XStringFormats.TopLeft);
                        _cursor += 14;
                    }
                }

                _cursor += 6;
            }
        }

        /// <summary>Elenco dei partecipanti: chi ha parlato, quanto e per quante battute.</summary>
        public void Speakers(string title, IReadOnlyList<HistoryCue> cues)
        {
            var totals = SpeakerTotals(cues);
            if (totals.Count == 0)
            {
                return;
            }

            Heading(title);
            foreach (var speaker in totals)
            {
                var minutes = TimeSpan.FromSeconds(speaker.Seconds);
                Draw(
                    $"• {speaker.Label} — {speaker.Cues} battute, {speaker.Share:P0} del tempo di parola ({minutes:mm\\:ss})",
                    _body,
                    _text,
                    16,
                    3);
            }

            _cursor += 8;
        }

        /// <summary>
        /// Mappa: la pagina PDF vettoriale di Graphviz viene importata come pagina intera
        /// (nitida a qualsiasi zoom), altrimenti si disegna l'immagine nel flusso del testo.
        /// </summary>
        public void MapPage(PdfSection section, ConceptMap map, string? mapPdfPath)
        {
            if (mapPdfPath is not null && File.Exists(mapPdfPath))
            {
                try
                {
                    using var reader = PdfReader.Open(mapPdfPath, PdfDocumentOpenMode.Import);
                    if (reader.PageCount > 0)
                    {
                        // La mappa diventa una pagina intera: il titolo va su quella pagina e
                        // il testo successivo riprende su una pagina nuova.
                        _graphics?.Dispose();
                        _graphics = null;
                        var imported = _document.AddPage(reader.Pages[0]);
                        _page = imported;
                        using (var graphics = XGraphics.FromPdfPage(_page, XGraphicsPdfPageOptions.Append))
                        {
                            graphics.DrawString(
                                section.Title,
                                _heading,
                                _headingBrush,
                                new XRect(_margin, 34, UsableWidth, 24),
                                XStringFormats.TopLeft);
                        }

                        _cursor = _margin;
                        return;
                    }
                }
                catch (Exception)
                {
                    // se l'import non riesce si ripiega sull'immagine
                }
            }

            Heading(section.Title);
            try
            {
                using var stream = new MemoryStream(ConceptMapRenderer.RenderPng(map));
                using var image = XImage.FromStream(stream);
                var width = UsableWidth;
                var height = width * image.PixelHeight / image.PixelWidth;
                if (height > 430)
                {
                    height = 430;
                    width = height * image.PixelWidth / image.PixelHeight;
                }

                EnsureSpace(height + 10);
                _graphics!.DrawImage(image, _margin, _cursor, width, height);
                _cursor += height + 14;
            }
            catch (Exception)
            {
                Draw("(immagine della mappa non disponibile)", _italic, _subtle);
            }
        }

        /// <summary>Intestazione, piè di pagina e numeri di pagina su tutte le pagine.</summary>
        public void Finish(HistorySession session, PdfTemplate template)
        {
            _graphics?.Dispose();
            _graphics = null;

            for (var index = 0; index < _document.PageCount; index++)
            {
                var page = _document.Pages[index];
                if (template.Style.Cover && index == 0)
                {
                    continue;
                }

                using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                var width = page.Width.Point;
                if (template.Style.Header)
                {
                    graphics.DrawString(
                        $"{template.Name} · sessione #{session.Id}",
                        _small,
                        _subtle,
                        new XRect(_margin, 18, width - (_margin * 2), 12),
                        XStringFormats.TopLeft);
                    graphics.DrawLine(new XPen(_lineColor, 0.4), _margin, 32, width - _margin, 32);
                }

                if (template.Style.Footer)
                {
                    graphics.DrawString(
                        "Wisper Translator",
                        _small,
                        _subtle,
                        new XRect(_margin, page.Height.Point - 30, width - (_margin * 2), 12),
                        XStringFormats.TopLeft);
                }

                if (template.Style.PageNumbers)
                {
                    graphics.DrawString(
                        $"Pagina {index + 1} di {_document.PageCount}",
                        _small,
                        _subtle,
                        new XRect(_margin, page.Height.Point - 30, width - (_margin * 2), 12),
                        XStringFormats.TopRight);
                }
            }
        }

        private void Draw(string text, XFont font, XBrush brush, double indent = 0, double spacing = 6)
        {
            foreach (var paragraph in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                foreach (var line in Wrap(paragraph, font, UsableWidth - indent))
                {
                    EnsureSpace(16);
                    _graphics!.DrawString(
                        line,
                        font,
                        brush,
                        new XRect(_margin + indent, _cursor, UsableWidth - indent, 16),
                        XStringFormats.TopLeft);
                    _cursor += 15.5;
                }
            }

            _cursor += spacing;
        }

        private IEnumerable<string> Wrap(string text, XFont font, double maxWidth)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                yield return string.Empty;
                yield break;
            }

            var builder = new StringBuilder();
            EnsurePage();
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = builder.Length == 0 ? word : $"{builder} {word}";
                if (_graphics!.MeasureString(candidate, font).Width <= maxWidth || builder.Length == 0)
                {
                    builder.Clear();
                    builder.Append(candidate);
                    continue;
                }

                yield return builder.ToString();
                builder.Clear();
                builder.Append(word);
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString();
            }
        }
    }
}

/// <summary>Quanto ha parlato un partecipante: battute, secondi e quota sul totale.</summary>
public sealed record SpeakerTotal(int Speaker, string Label, int Cues, double Seconds, double Share);
