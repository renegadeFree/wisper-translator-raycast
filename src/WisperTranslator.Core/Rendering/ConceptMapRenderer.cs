using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using WisperTranslator.Core.Ai;

namespace WisperTranslator.Core.Rendering;

/// <summary>
/// Disegna la mappa concettuale senza librerie di grafi: i nodi si dispongono su colonne
/// secondo la loro distanza dalla radice (BFS), gli archi diventano frecce etichettate.
/// </summary>
public static class ConceptMapRenderer
{
    private const int NodeWidth = 210;
    private const int NodeHeight = 74;
    private const int ColumnGap = 80;
    private const int RowGap = 26;
    private const int Padding = 40;
    private const int MaxNodes = 18;

    public static Bitmap Render(ConceptMap map)
    {
        var nodes = map.Nodes.Take(MaxNodes).ToList();
        var ids = nodes.Select(node => node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = map.Edges
            .Where(edge => ids.Contains(edge.From) && ids.Contains(edge.To))
            .ToList();

        var levels = ComputeLevels(nodes, edges);
        // I livelli (distanza dalla radice) stanno sull'asse X, i nodi dello stesso livello sull'asse Y.
        var columns = Math.Max(1, levels.Count);
        var rows = levels.Count == 0 ? 1 : levels.Values.Max(bucket => bucket.Count);

        var width = Padding * 2 + Math.Max(1, columns) * (NodeWidth + ColumnGap);
        var height = Padding * 2 + 60 + Math.Max(1, rows) * (NodeHeight + RowGap);
        var bitmap = new Bitmap(Math.Min(width, 2400), Math.Min(height, 2000));

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.FromArgb(255, 24, 24, 28));

        using var titleFont = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        using var nodeFont = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        using var edgeFont = new Font("Segoe UI", 9, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(Color.FromArgb(255, 255, 150, 70));
        using var textBrush = new SolidBrush(Color.White);
        using var edgeBrush = new SolidBrush(Color.FromArgb(200, 170, 170, 180));
        using var nodeFill = new SolidBrush(Color.FromArgb(255, 38, 38, 44));
        using var nodeBorder = new Pen(Color.FromArgb(255, 255, 140, 60), 1.6f);
        using var edgePen = new Pen(Color.FromArgb(190, 255, 150, 80), 1.4f);

        if (!string.IsNullOrWhiteSpace(map.Title))
        {
            graphics.DrawString(map.Title, titleFont, titleBrush, Padding, 14);
        }

        var positions = new Dictionary<string, RectangleF>(StringComparer.OrdinalIgnoreCase);
        for (var column = 0; column < columns; column++)
        {
            var bucket = levels
                .OrderBy(pair => pair.Key)
                .ElementAtOrDefault(column).Value ?? [];
            var columnNodes = bucket.ToList();
            for (var row = 0; row < columnNodes.Count; row++)
            {
                var x = Padding + column * (NodeWidth + ColumnGap);
                var y = Padding + 50 + row * (NodeHeight + RowGap);
                positions[columnNodes[row].Id] = new RectangleF(x, y, NodeWidth, NodeHeight);
            }
        }

        foreach (var edge in edges)
        {
            if (!positions.TryGetValue(edge.From, out var from) || !positions.TryGetValue(edge.To, out var to))
            {
                continue;
            }

            var start = new PointF(from.Right, from.Top + from.Height / 2);
            var end = new PointF(to.Left, to.Top + to.Height / 2);
            graphics.DrawLine(edgePen, start, end);

            // punta della freccia
            var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            var size = 9f;
            var p1 = new PointF(
                end.X - (float)(size * Math.Cos(angle - 0.4)),
                end.Y - (float)(size * Math.Sin(angle - 0.4)));
            var p2 = new PointF(
                end.X - (float)(size * Math.Cos(angle + 0.4)),
                end.Y - (float)(size * Math.Sin(angle + 0.4)));
            graphics.FillPolygon(edgeBrush, [end, p1, p2]);

            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                var middle = new PointF((start.X + end.X) / 2, (start.Y + end.Y) / 2 - 10);
                graphics.DrawString(edge.Label, edgeFont, edgeBrush, middle.X, middle.Y);
            }
        }

        foreach (var node in nodes)
        {
            if (!positions.TryGetValue(node.Id, out var bounds))
            {
                continue;
            }

            using var path = RoundedRectangle(bounds, 10);
            graphics.FillPath(nodeFill, path);
            graphics.DrawPath(nodeBorder, path);

            var label = Wrap(graphics, node.Label, nodeFont, (int)bounds.Width - 20, 3);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            graphics.DrawString(label, nodeFont, textBrush, bounds, format);
        }

        return bitmap;
    }

    public static byte[] RenderPng(ConceptMap map)
    {
        using var bitmap = Render(map);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>Assegna a ogni nodo il livello = lunghezza del cammino più lungo dalla radice.</summary>
    private static Dictionary<int, List<ConceptNode>> ComputeLevels(
        List<ConceptNode> nodes,
        List<ConceptEdge> edges)
    {
        var incoming = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            incoming[edge.To] = incoming.GetValueOrDefault(edge.To) + 1;
        }

        var roots = nodes.Where(node => incoming[node.Id] == 0).ToList();
        if (roots.Count == 0 && nodes.Count > 0)
        {
            roots.Add(nodes[0]);
        }

        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(ConceptNode Node, int Level)>();
        foreach (var root in roots)
        {
            queue.Enqueue((root, 0));
            levels[root.Id] = 0;
        }

        while (queue.Count > 0)
        {
            var (node, level) = queue.Dequeue();
            foreach (var edge in edges.Where(edge => string.Equals(edge.From, node.Id, StringComparison.OrdinalIgnoreCase)))
            {
                var next = level + 1;
                if (levels.TryGetValue(edge.To, out var existing) && existing >= next)
                {
                    continue;
                }

                levels[edge.To] = next;
                var target = nodes.FirstOrDefault(candidate => string.Equals(candidate.Id, edge.To, StringComparison.OrdinalIgnoreCase));
                if (target is not null)
                {
                    queue.Enqueue((target, next));
                }
            }
        }

        // i nodi isolati finiscono nel primo livello
        foreach (var node in nodes.Where(node => !levels.ContainsKey(node.Id)))
        {
            levels[node.Id] = 0;
        }

        return nodes
            .GroupBy(node => levels[node.Id])
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2f;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string Wrap(Graphics graphics, string text, Font font, int maxWidth, int maxLines)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (graphics.MeasureString(candidate, font).Width <= maxWidth)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }

            current = word;
            if (lines.Count == maxLines)
            {
                break;
            }
        }

        if (lines.Count < maxLines && current.Length > 0)
        {
            lines.Add(current);
        }

        if (lines.Count == maxLines && words.Length > 0)
        {
            lines[^1] = lines[^1].TrimEnd() + "…";
        }

        return string.Join('\n', lines);
    }
}
