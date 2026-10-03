using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace WisperTranslator.App.Windows;

/// <summary>
/// Schermate per la documentazione: si disegna l'elemento WPF, non si copia lo schermo.
/// Così nell'immagine non può finire niente del desktop, e non servono né finestre visibili
/// né librerie esterne.
/// </summary>
internal static class WindowShot
{
    /// <summary>Risoluzione doppia: le immagini del README restano nitide anche ingrandite.</summary>
    private const double Scale = 2.0;

    public static bool Capture(FrameworkElement element, string path, Color background, double cornerRadius)
    {
        if (element.ActualWidth < 1 || element.ActualHeight < 1)
        {
            return false;
        }

        var width = (int)Math.Round(element.ActualWidth * Scale);
        var height = (int)Math.Round(element.ActualHeight * Scale);
        if (width < 1 || height < 1)
        {
            return false;
        }

        var content = new RenderTargetBitmap(width, height, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        content.Render(element);

        var radius = Math.Min(cornerRadius * Scale, Math.Min(width, height) / 2.0);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            context.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), radius, radius));
            context.DrawImage(content, new Rect(0, 0, width, height));
            context.Pop();
        }

        var output = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        output.Render(visual);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        using var stream = File.Create(path);
        encoder.Save(stream);

        Log($"{Path.GetFileName(path)}: {width}x{height} px · raggio {radius:F0} px");
        return true;
    }

    /// <summary>Traccia delle catture: dimensioni reali, utili quando un ritaglio non torna.</summary>
    private static void Log(string message)
    {
        try
        {
            var path = Path.Combine(Core.AppPaths.EnsureSubdirectory("logs"), "screenshot.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // il log non deve far fallire gli screenshot
        }
    }
}
