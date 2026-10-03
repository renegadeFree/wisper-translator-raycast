using NAudio.CoreAudioApi;

namespace WisperTranslator.Core.Audio;

/// <summary>Sorgente audio gestita dall'applicazione.</summary>
public enum SourceKind
{
    /// <summary>Audio riprodotto dal PC (loopback WASAPI).</summary>
    System,

    /// <summary>Ingresso da periferica fisica.</summary>
    Microphone,
}

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault, SourceKind Kind);

/// <summary>Enumerazione e apertura dei dispositivi audio di Windows.</summary>
public static class AudioDevices
{
    public static IReadOnlyList<AudioDeviceInfo> List(SourceKind kind)
    {
        using var enumerator = new MMDeviceEnumerator();
        var flow = Flow(kind);
        var defaultId = TryGetDefaultId(enumerator, flow);

        var devices = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId, kind));
            }
        }

        return devices;
    }

    /// <summary>
    /// Risolve un dispositivo dal suo ID oppure, se non è un ID, dalla prima periferica
    /// il cui nome contiene la stringa indicata. Con <paramref name="idOrName"/> nullo
    /// restituisce il dispositivo predefinito.
    /// </summary>
    public static MMDevice Open(SourceKind kind, string? idOrName = null)
    {
        var enumerator = new MMDeviceEnumerator();
        try
        {
            if (string.IsNullOrWhiteSpace(idOrName))
            {
                return enumerator.GetDefaultAudioEndpoint(Flow(kind), Role.Multimedia);
            }

            var devices = List(kind);
            foreach (var info in devices)
            {
                if (string.Equals(info.Id, idOrName, StringComparison.OrdinalIgnoreCase))
                {
                    return enumerator.GetDevice(info.Id);
                }
            }

            foreach (var info in devices)
            {
                if (info.Name.Contains(idOrName, StringComparison.OrdinalIgnoreCase))
                {
                    return enumerator.GetDevice(info.Id);
                }
            }

            throw new InvalidOperationException(
                $"Nessun dispositivo {(kind == SourceKind.System ? "di uscita" : "di ingresso")} corrisponde a \"{idOrName}\".");
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    private static DataFlow Flow(SourceKind kind) =>
        kind == SourceKind.System ? DataFlow.Render : DataFlow.Capture;

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return device.ID;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
