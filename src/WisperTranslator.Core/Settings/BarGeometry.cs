namespace WisperTranslator.Core.Settings;

/// <summary>
/// Misure della barra fluttuante in un solo posto: la usano la finestra, le impostazioni e i test,
/// così larghezza minima, massima e raggio non possono divergere.
/// </summary>
public static class BarGeometry
{
    /// <summary>Bordo afferrato per ridimensionare: si combinano fra loro negli angoli.</summary>
    [Flags]
    public enum Edges
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
    }

    public const double MinWidth = 480;
    public const double MaxWidth = 1600;
    public const double DefaultWidth = 760;

    /// <summary>Altezza minima della card: maniglia e piè di pagina devono restare visibili.</summary>
    public const double MinHeight = 120;

    /// <summary>Altezza massima scelta dall'utente con lo slider (il trascinamento arriva al 70% dello schermo).</summary>
    public const double MaxSliderHeight = 900;

    /// <summary>Frazione massima dell'area di lavoro occupabile in altezza.</summary>
    public const double MaxHeightFraction = 0.7;

    /// <summary>
    /// Spessore della fascia sensibile sui bordi per il ridimensionamento. Generosa di proposito:
    /// gli angoli arrotondati tolgono qualche pixel proprio sul bordo della finestra.
    /// </summary>
    public const double EdgeGrip = 12;

    /// <summary>Capsula minimal: sta in alto come una voce di launcher e non copre nulla.</summary>
    public const double MiniWidth = 320;
    public const double MiniHeight = 48;
    public const double MiniRadius = 8;

    /// <summary>Righe visibili al massimo: oltre, la barra coprirebbe mezzo schermo.</summary>
    public const int MaxRows = 5;

    /// <summary>
    /// Angoli del pannello di vetro. Deve valere quanto il raggio che DWM dà alla finestra
    /// (8 DIP a qualsiasi DPI): è DWM a ritagliare il vetro, e un raggio più grande lascerebbe
    /// scoperti quattro spicchi di acrilico proprio sugli angoli.
    /// </summary>
    public const double CornerRadiusValue = 8;

    /// <summary>Riga comandi in alto (equalizzatore, stato, pulsante principale).</summary>
    public const double HandleHeight = 48;

    /// <summary>Altezza di una frase: originale su una riga e traduzione su due righe.</summary>
    public const double RowHeight = 76;

    /// <summary>Spazio fra due frasi.</summary>
    public const double RowGap = 4;

    public const double FooterHeight = 32;
    public const double IdleHeight = 44;

    /// <summary>Margine sotto le righe, identico all'anteprima HTML.</summary>
    public const double BodyBottomMargin = 6;

    /// <summary>Larghezza valida: valori assenti o assurdi non rompono la finestra.</summary>
    public static double ClampWidth(double width) =>
        double.IsFinite(width) ? Math.Clamp(width, MinWidth, MaxWidth) : DefaultWidth;

    public static double ViewportHeight(int rows) => Math.Clamp(rows, 1, MaxRows) * (RowHeight + RowGap);

    public static double WindowHeight(int rows) =>
        HandleHeight + ViewportHeight(rows) + BodyBottomMargin + FooterHeight;

    /// <summary>Altezza libera: il corpo prende tutto lo spazio rimasto fra maniglia e piè di pagina.</summary>
    public static double ClampHeight(double height) =>
        double.IsFinite(height) ? Math.Clamp(height, MinHeight, MaxSliderHeight) : WindowHeight(2);

    /// <summary>Altezza massima reale: oltre il 70% dell'area di lavoro la barra coprirebbe lo schermo.</summary>
    public static double MaxHeightFor(double workAreaHeight) =>
        double.IsFinite(workAreaHeight) && workAreaHeight > 0
            ? Math.Max(MinHeight, workAreaHeight * MaxHeightFraction)
            : MaxSliderHeight;

    /// <summary>Quante frasi intere entrano nell'altezza data: il resto si raggiunge con la rotellina.</summary>
    public static int RowsForHeight(double height) =>
        Math.Clamp((int)Math.Round((ClampHeight(height) - HandleHeight - BodyBottomMargin - FooterHeight)
                                   / (RowHeight + RowGap)), 1, MaxRows);

    /// <summary>Altezza della card che mostra esattamente quel numero di frasi.</summary>
    public static double HeightForRows(int rows) => WindowHeight(rows);

    /// <summary>Card completa senza sottotitoli, quando la modalità minimal è disattivata.</summary>
    public static double IdleWindowHeight => HandleHeight + IdleHeight + BodyBottomMargin + FooterHeight;

    /// <summary>Trasparenza del vetro: sotto 0,55 il testo non è più leggibile su sfondi chiari.</summary>
    public static double ClampOpacity(double opacity) =>
        double.IsFinite(opacity) ? Math.Clamp(opacity, 0.55, 0.92) : 0.72;

    /// <summary>
    /// Quanto il pannello colora il vetro: è la tinta dell'acrilico, che resta sfocato anche
    /// quando la finestra non è attiva. A 0,72 di slider il vetro è tinto per un terzo.
    /// </summary>
    public static double AcrylicTint(double opacity) =>
        Math.Clamp(ClampOpacity(opacity) * 0.5, 0.1, 0.5);

    /// <summary>
    /// Velo scuro disegnato sopra il vetro: più basso dello slider perché l'acrilico è già
    /// scuro, ma abbastanza alto da tenere leggibile il testo su uno sfondo chiaro.
    /// </summary>
    public static double PanelVeil(double opacity) =>
        Math.Clamp(ClampOpacity(opacity) * 0.42, 0.1, 0.45);

    /// <summary>Raggio stabile, mai oltre metà altezza: è quello che rende la finestra una sola forma.</summary>
    public static double CornerRadius(double height) =>
        Math.Max(8, Math.Min(CornerRadiusValue, height / 2));

    /// <summary>Quale bordo (o angolo) sta afferrando il puntatore, in coordinate finestra.</summary>
    public static Edges EdgeAt(double x, double y, double width, double height, double grip = EdgeGrip)
    {
        var edges = Edges.None;
        if (x <= grip) edges |= Edges.Left;
        else if (x >= width - grip) edges |= Edges.Right;
        if (y <= grip) edges |= Edges.Top;
        else if (y >= height - grip) edges |= Edges.Bottom;
        return edges;
    }

    /// <summary>
    /// Geometria dopo un trascinamento: il bordo opposto a quello afferrato resta fermo, le
    /// misure restano nei limiti e la finestra non esce dall'area di lavoro.
    /// </summary>
    public static (double Left, double Top, double Width, double Height) Resize(
        double left,
        double top,
        double width,
        double height,
        Edges edges,
        double dx,
        double dy,
        double areaLeft,
        double areaTop,
        double areaWidth,
        double areaHeight)
    {
        var resizedWidth = width;
        var resizedHeight = height;
        if ((edges & Edges.Left) != 0) resizedWidth = width - dx;
        if ((edges & Edges.Right) != 0) resizedWidth = width + dx;
        if ((edges & Edges.Top) != 0) resizedHeight = height - dy;
        if ((edges & Edges.Bottom) != 0) resizedHeight = height + dy;

        resizedWidth = Math.Clamp(resizedWidth, MinWidth, Math.Min(MaxWidth, Math.Max(MinWidth, areaWidth)));
        resizedHeight = Math.Clamp(resizedHeight, MinHeight, MaxHeightFor(areaHeight));

        var resizedLeft = (edges & Edges.Left) != 0 ? left + width - resizedWidth : left;
        var resizedTop = (edges & Edges.Top) != 0 ? top + height - resizedHeight : top;
        resizedLeft = Math.Clamp(resizedLeft, areaLeft, Math.Max(areaLeft, areaLeft + areaWidth - resizedWidth));
        resizedTop = Math.Clamp(resizedTop, areaTop, Math.Max(areaTop, areaTop + areaHeight - resizedHeight));
        return (resizedLeft, resizedTop, resizedWidth, resizedHeight);
    }
}
