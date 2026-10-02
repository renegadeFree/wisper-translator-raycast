using System.Text;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using WisperTranslator.Core.Ai;
using WisperTranslator.Core.History;

namespace WisperTranslator.Core.Rendering;

/// <summary>
/// Costruisce il PDF di una sessione: intestazione, riassunto, punti chiave, mappa concettuale
/// e trascrizione bilingue. Impagina a mano per mantenere il controllo su interruzioni e margini.
/// </summary>
public static class PdfReportBuilder
{
    private const double Margin = 46;
    private const double LineHeight = 15.5;

    public static void Build(
        string outputPath,
        HistorySession session,
        IReadOnlyList<HistoryCue> cues,
        SessionNote? note,
        ConceptMap? map)
    {
        WindowsFontResolver.EnsureRegistered();

        using var document = new PdfDocument();
        document.Info.Title = $"Wisper Translator — sessione {session.Id}";
        document.Info.Author = "Wisper Translator";

        var titleFont = new XFont("Segoe UI", 20, XFontStyleEx.Bold);
        var headingFont = new XFont("Segoe UI", 13, XFontStyleEx.Bold);
        var bodyFont = new XFont("Segoe UI", 10.5);
        var italicFont = new XFont("Segoe UI", 9.5, XFontStyleEx.Italic);
        var originalFont = new XFont("Segoe UI", 9, XFontStyleEx.Italic);
        var titleBrush = new XSolidBrush(XColor.FromArgb(255, 210, 100, 30));
        var textBrush = XBrushes.Black;
        var subtleBrush = new XSolidBrush(XColor.FromArgb(255, 110, 110, 120));

        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        var graphics = XGraphics.FromPdfPage(page);
        var cursor = Margin;
        var usableWidth = page.Width.Point - Margin * 2;

        void NewPageIfNeeded(double needed)
        {
            if (cursor + needed <= page.Height.Point - Margin)
            {
                return;
            }

            graphics.Dispose();
            page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            cursor = Margin;
        }

        void Write(string text, XFont font, XBrush brush, double indent = 0, double spacing = 2)
        {
            foreach (var paragraph in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                foreach (var line in Wrap(graphics, paragraph, font, usableWidth - indent))
                {
                    NewPageIfNeeded(LineHeight);
                    graphics.DrawString(line, font, brush, new XRect(Margin + indent, cursor, usableWidth - indent, LineHeight + 4), XStringFormats.TopLeft);
                    cursor += LineHeight;
                }
            }

            cursor += spacing;
        }

        // --- intestazione
        graphics.DrawString("Wisper Translator", titleFont, titleBrush, new XRect(Margin, cursor, usableWidth, 30), XStringFormats.TopLeft);
        cursor += 30;
        graphics.DrawString(
            $"Sessione #{session.Id} · {session.StartedAt:dd/MM/yyyy HH:mm} · {session.SourceLanguage} → {session.TargetLanguage} · {cues.Count} battute",
            italicFont,
            subtleBrush,
            new XRect(Margin, cursor, usableWidth, 16),
            XStringFormats.TopLeft);
        cursor += 26;

        // --- mappa concettuale
        if (map is { IsEmpty: false })
        {
            Write("Mappa concettuale", headingFont, textBrush);
            try
            {
                var png = new MemoryStream(ConceptMapRenderer.RenderPng(map));
                using var image = XImage.FromStream(png);
                var width = usableWidth;
                var height = width * image.PixelHeight / image.PixelWidth;
                if (height > 420)
                {
                    height = 420;
                    width = height * image.PixelWidth / image.PixelHeight;
                }

                NewPageIfNeeded(height + 10);
                graphics.DrawImage(image, Margin, cursor, width, height);
                cursor += height + 14;
            }
            catch (Exception)
            {
                Write("(immagine della mappa non disponibile)", italicFont, subtleBrush);
            }
        }

        // --- riassunto
        if (note is { Summary.Length: > 0 })
        {
            Write("Riassunto", headingFont, textBrush);
            Write(note.Summary, bodyFont, textBrush);
        }

        if (note is { KeyPoints.Length: > 0 })
        {
            Write("Punti chiave", headingFont, textBrush);
            Write(note.KeyPoints, bodyFont, textBrush);
        }

        if (note is { IsEmpty: false })
        {
            Write($"Generato con {note.Provider} il {note.UpdatedAt:dd/MM/yyyy HH:mm}", italicFont, subtleBrush, spacing: 10);
        }

        // --- trascrizione
        Write("Trascrizione", headingFont, textBrush);
        foreach (var cue in cues)
        {
            Write($"[{cue.AudioStart:mm\\:ss}] {cue.Original}", originalFont, subtleBrush);
            if (cue.Translation.Trim().Length > 0)
            {
                Write(cue.Translation, bodyFont, textBrush, indent: 12, spacing: 6);
            }
        }

        graphics.Dispose();
        document.Save(outputPath);
    }

    private static IEnumerable<string> Wrap(XGraphics graphics, string text, XFont font, double maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield return string.Empty;
            yield break;
        }

        var builder = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = builder.Length == 0 ? word : $"{builder} {word}";
            if (graphics.MeasureString(candidate, font).Width <= maxWidth || builder.Length == 0)
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
