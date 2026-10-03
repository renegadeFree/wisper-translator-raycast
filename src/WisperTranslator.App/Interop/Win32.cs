using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WisperTranslator.App.Interop;

/// <summary>Chiamate Win32 necessarie a overlay cliccabile-attraverso e hotkey globali.</summary>
internal static class Win32
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    // Esclude la finestra dalla cattura (registrazioni schermo e condivisioni).
    private const uint WdaExcludeFromCapture = 0x00000011;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr region, bool redraw);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowRgn(IntPtr hWnd, IntPtr region);

    public static IntPtr Handle(Window window) => new WindowInteropHelper(window).Handle;

    /// <summary>
    /// Ritaglia davvero l'HWND ad angoli arrotondati: è ciò che impedisce ai pixel acrilici
    /// di riempire il rettangolo attorno alla capsula.
    /// </summary>
    public static bool ApplyRoundedRegion(Window window, int widthPixels, int heightPixels, int diameterPixels)
    {
        var handle = Handle(window);
        if (handle == IntPtr.Zero || widthPixels < 2 || heightPixels < 2)
        {
            return false;
        }

        var region = CreateRoundRectRgn(0, 0, widthPixels + 1, heightPixels + 1, diameterPixels, diameterPixels);
        if (region == IntPtr.Zero)
        {
            return false;
        }

        // SetWindowRgn prende possesso della regione: non va cancellata qui.
        if (SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
            return false;
        }

        return true;
    }

    /// <summary>Tipo di regione corrente: 3 = COMPLEXREGION, cioè non è un semplice rettangolo.</summary>
    public static int WindowRegionType(Window window)
    {
        var handle = Handle(window);
        if (handle == IntPtr.Zero)
        {
            return 0;
        }

        var probe = CreateRoundRectRgn(0, 0, 1, 1, 2, 2);
        if (probe == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            return GetWindowRgn(handle, probe);
        }
        finally
        {
            DeleteObject(probe);
        }
    }

    /// <summary>Rende la finestra trasparente ai click e, se richiesto, invisibile alle registrazioni.</summary>
    public static void MakeOverlay(Window window, bool hideFromCapture = true)
    {
        var handle = Handle(window);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var extended = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, extended | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate);
        SetWindowDisplayAffinity(handle, hideFromCapture ? WdaExcludeFromCapture : 0x00000000);
    }

    public static bool TryRegisterHotKey(Window window, int id, uint modifiers, uint virtualKey)
    {
        var handle = Handle(window);
        return handle != IntPtr.Zero && RegisterHotKey(handle, id, modifiers | ModNoRepeat, virtualKey);
    }

    public static void UnregisterHotKey(Window window, int id)
    {
        var handle = Handle(window);
        if (handle != IntPtr.Zero)
        {
            UnregisterHotKey(handle, id);
        }
    }
}
