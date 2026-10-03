using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WisperTranslator.App.Interop;

/// <summary>Chiamate Win32 necessarie a overlay cliccabile-attraverso e hotkey globali.</summary>
internal static class Win32
{
    private const int GwlExStyle = -20;
    private const int GwlStyle = -16;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExAppWindow = 0x00040000;
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsBorder = 0x00800000;
    private const int WsDlgFrame = 0x00400000;
    private const int WsSysMenu = 0x00080000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int SwpNoSize = 0x0001;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;
    private const int SwpFrameChanged = 0x0020;

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
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static IntPtr Handle(Window window) => new WindowInteropHelper(window).Handle;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmcpRound = 2;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
    private const int DwmsbtNone = 1;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int WcaAccentPolicy = 19;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionAttribute data);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int State;
        public int Flags;
        public uint Color;
        public int Animation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionAttribute
    {
        public int Attribute;
        public IntPtr Data;
        public nuint Size;
    }

    /// <summary>
    /// Materiale della barra: angoli arrotondati da DWM (è DWM a ritagliare il vetro sulla forma
    /// della finestra), niente bordo, e acrilico dell'accent policy invece del backdrop di sistema.
    /// L'acrilico "accent" resta sfocato anche quando la finestra non è attiva, mentre Mica e
    /// Acrylic di DWM diventano tinta unita senza fuoco: la barra è quasi sempre in secondo piano.
    /// </summary>
    public static bool ApplyAcrylicBackdrop(Window window, byte tintAlpha, byte red, byte green, byte blue)
    {
        var handle = Handle(window);
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return false;
        }

        var dark = 1;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        var round = DwmcpRound;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
        var noBorder = DwmwaColorNone;
        DwmSetWindowAttribute(handle, DwmwaBorderColor, ref noBorder, sizeof(int));

        // Il backdrop di sistema vince sull'accent policy: va spento prima di applicarla.
        var noBackdrop = DwmsbtNone;
        DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref noBackdrop, sizeof(int));

        var tint = (uint)((tintAlpha << 24) | (blue << 16) | (green << 8) | red);
        return ApplyAccent(handle, AccentEnableAcrylicBlurBehind, tint);
    }

    /// <summary>Toglie l'acrilico della barra: il pannello torna un rettangolo pieno.</summary>
    public static bool ClearAcrylicBackdrop(Window window)
    {
        var handle = Handle(window);
        return handle != IntPtr.Zero && ApplyAccent(handle, 0, 0);
    }

    private static bool ApplyAccent(IntPtr handle, int state, uint color)
    {
        if (handle == IntPtr.Zero) return false;
        var policy = new AccentPolicy { State = state, Flags = state == 0 ? 0 : 2, Color = color };
        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new CompositionAttribute { Attribute = WcaAccentPolicy, Data = pointer, Size = (nuint)size };
            return SetWindowCompositionAttribute(handle, ref data) != 0;
        }
        catch (EntryPointNotFoundException) { return false; }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    public static bool TryGetBarBounds(Window window, out NativeRect bounds, out NativeRect work)
    {
        var handle = Handle(window);
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        work = default;
        if (!GetWindowRect(handle, out bounds) || !GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor))
            return false;
        work = monitor.Work;
        return true;
    }

    /// <summary>Overlay senza voce Alt+Tab; il materiale scuro resta leggibile anche col tema chiaro.</summary>
    public static void MakeFloatingBar(Window window)
    {
        var handle = Handle(window);
        if (handle == IntPtr.Zero) return;
        var extended = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, (extended | WsExToolWindow) & ~WsExAppWindow);

        // Il tema dell'applicazione può ripristinare uno stile con caption/thick frame: la barra
        // deve restare un popup anche dopo attivazione, cambio tema e cambio DPI.
        var style = GetWindowLong(handle, GwlStyle);
        style &= ~(WsCaption | WsThickFrame | WsBorder | WsDlgFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
        style |= WsPopup;
        SetWindowLong(handle, GwlStyle, style);
        SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        var dark = 1;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        var noBorder = DwmwaColorNone;
        DwmSetWindowAttribute(handle, DwmwaBorderColor, ref noBorder, sizeof(int));
    }

    /// <summary>Per l'autotest: vero se è rimasto uno stile finestra classico.</summary>
    public static bool HasWindowFrame(Window window)
    {
        var handle = Handle(window);
        if (handle == IntPtr.Zero) return false;
        var style = GetWindowLong(handle, GwlStyle);
        return (style & (WsCaption | WsThickFrame | WsBorder | WsDlgFrame)) != 0;
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
