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

    /// <summary>Riga comandi in alto (equalizzatore, stato, pulsante principale).</summary>
    public const double HandleHeight = 44;

    /// <summary>Altezza di una frase: originale su una riga e traduzione su due.</summary>
    public const double RowHeight = 74;

    /// <summary>Spazio fra due frasi.</summary>
    public const double RowGap = 4;

    public const double BottomPadding = 10;

    /// <summary>Larghezza valida: valori assenti o assurdi non rompono la finestra.</summary>
    public static double ClampWidth(double width) =>
        double.IsFinite(width) ? Math.Clamp(width, MinWidth, MaxWidth) : DefaultWidth;

    public static double ViewportHeight(int rows) => Math.Clamp(rows, 1, 3) * (RowHeight + RowGap);

    public static double WindowHeight(int rows) => HandleHeight + ViewportHeight(rows) + BottomPadding;

    /// <summary>Capsula: il raggio è metà altezza, come nei riferimenti.</summary>
    public static double CornerRadius(double height) => Math.Max(8, height / 2);
}
