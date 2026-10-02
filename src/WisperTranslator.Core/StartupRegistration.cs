using Microsoft.Win32;

namespace WisperTranslator.Core;

/// <summary>Avvio automatico con Windows tramite la chiave Run dell'utente (nessun admin).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WisperTranslator";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Set(bool enabled, string? executablePath = null, string arguments = "--autostart")
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var path = executablePath ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        key.SetValue(ValueName, $"\"{path}\" {arguments}".Trim(), RegistryValueKind.String);
    }
}
