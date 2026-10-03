namespace WisperTranslator.Core.Settings;

/// <summary>
/// Misure della barra fluttuante in un solo posto: la usano la finestra, le impostazioni e i test,
/// così larghezza minima, massima e raggio non possono divergere.
/// </summary>
public static class BarGeometry
{
    public const double MinWidth = 480;
    public const double MaxWidth = 1600;
    public const double DefaultWidth = 1000;

    /// <summary>Oltre due righe visibili la barra coprirebbe mezzo schermo: le altre restano in memoria.</summary>
    public const int MaxRows = 2;

    /// <summary>Angoli del pannello di vetro: il riferimento è un rettangolo arrotondato, non una pillola.</summary>
    public const double CornerRadiusValue = 20;

    /// <summary>Riga comandi in alto (equalizzatore, stato, pulsante principale).</summary>
    public const double HandleHeight = 48;

    /// <summary>Altezza di una frase: originale su una riga e traduzione su due righe.</summary>
    public const double RowHeight = 86;

    /// <summary>Spazio fra due frasi.</summary>
    public const double RowGap = 6;

    public const double BottomPadding = 8;

    /// <summary>Larghezza valida: valori assenti o assurdi non rompono la finestra.</summary>
    public static double ClampWidth(double width) =>
        double.IsFinite(width) ? Math.Clamp(width, MinWidth, MaxWidth) : DefaultWidth;

    public static double ViewportHeight(int rows) => Math.Clamp(rows, 1, MaxRows) * (RowHeight + RowGap);

    public static double WindowHeight(int rows) => HandleHeight + ViewportHeight(rows) + BottomPadding;

    /// <summary>Raggio stabile, mai oltre metà altezza: è quello che rende la finestra una sola forma.</summary>
    public static double CornerRadius(double height) =>
        Math.Max(8, Math.Min(CornerRadiusValue, height / 2));

    /// <summary>Diametro dell'ellisse di arrotondamento in pixel device, per SetWindowRgn.</summary>
    public static int RegionDiameter(double radius, double dpiScale) =>
        Math.Max(2, (int)Math.Round(radius * 2 * (dpiScale > 0 ? dpiScale : 1)));
}
