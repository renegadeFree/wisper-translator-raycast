using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WisperTranslator.Core.History;

/// <summary>Esportazione delle sessioni: SRT per i sottotitoli, testo e JSON per l'analisi.</summary>
public static class HistoryExporter
{
    public static string ToSrt(IEnumerable<HistoryCue> cues, bool includeOriginal = true)
    {
        var builder = new StringBuilder();
        var index = 1;
        foreach (var cue in cues.Where(cue => cue.Original.Trim().Length > 0))
        {
            var start = cue.AudioStart;
            var end = start + (cue.Duration > TimeSpan.Zero ? cue.Duration : TimeSpan.FromSeconds(2));
            builder.AppendLine(index.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine($"{Timecode(start)} --> {Timecode(end)}");

            if (includeOriginal && cue.Translation.Trim().Length > 0)
            {
                builder.AppendLine(cue.Translation.Trim());
                builder.AppendLine(cue.Original.Trim());
            }
            else
            {
                builder.AppendLine((cue.Translation.Trim().Length > 0 ? cue.Translation : cue.Original).Trim());
            }

            builder.AppendLine();
            index++;
        }

        return builder.ToString();
    }

    public static string ToText(IEnumerable<HistoryCue> cues, bool includeOriginal = true)
    {
        var builder = new StringBuilder();
        foreach (var cue in cues)
        {
            if (includeOriginal)
            {
                builder.AppendLine($"[{cue.AudioStart:mm\\:ss}] {cue.Original}");
                if (cue.Translation.Trim().Length > 0)
                {
                    builder.AppendLine($"          {cue.Translation}");
                }
            }
            else
            {
                builder.AppendLine(cue.Translation.Trim().Length > 0 ? cue.Translation : cue.Original);
            }
        }

        return builder.ToString();
    }

    public static string ToJson(HistorySession session, IEnumerable<HistoryCue> cues)
    {
        var payload = new
        {
            session = new
            {
                session.Id,
                session.StartedAt,
                session.EndedAt,
                session.SourceLanguage,
                session.TargetLanguage,
            },
            cues = cues.Select(cue => new
            {
                cue.Timestamp,
                Start = cue.AudioStart.TotalSeconds,
                Duration = cue.Duration.TotalSeconds,
                cue.Original,
                cue.Translation,
                cue.IsFinal,
            }),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Formato SRT: <c>HH:MM:SS,mmm</c> con virgola, non punto.</summary>
    public static string Timecode(TimeSpan time)
    {
        var totalHours = (int)time.TotalHours;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{totalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}");
    }
}
