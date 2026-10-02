using System.Management;
using System.Runtime.Intrinsics.X86;

namespace WisperTranslator.Core.Hardware;

public enum HardwareTier
{
    /// <summary>GPU dedicata con VRAM sufficiente.</summary>
    A,

    /// <summary>CPU con almeno 6 core fisici: la fascia consigliata.</summary>
    B,

    /// <summary>Macchina debole o molto vecchia: modelli minimi.</summary>
    C,
}

public sealed record HardwareProfile(
    string CpuName,
    int PhysicalCores,
    int LogicalCores,
    long RamMegabytes,
    string? GpuName,
    long VramMegabytes,
    bool HasAvx2,
    HardwareTier Tier)
{
    /// <summary>
    /// Modelli consigliati per questa macchina. Dipendono dai core disponibili perché il runtime
    /// incluso è **solo CPU**: misurato qui, `large-v3-turbo` costa RTF 0,59 anche su 24 thread,
    /// quindi su una macchina debole non starebbe nel tempo reale. La GPU dedicata conterà quando
    /// arriverà un runtime accelerato (F9); per ora resta un'informazione, non una promessa.
    /// </summary>
    public (string PartialModelId, string FinalModelId) RecommendedModels => PhysicalCores >= 6
        ? ("whisper-base-q5_1", "whisper-small-q5_1")
        : ("whisper-base-q5_1", "whisper-base-q5_1");

    public string Summary =>
        $"{CpuName} · {PhysicalCores} core ({LogicalCores} thread) · {RamMegabytes / 1024.0:F1} GB RAM · "
        + (GpuName is null ? "GPU non rilevata" : $"{GpuName} ({VramMegabytes / 1024.0:F1} GB VRAM)")
        + (HasAvx2 ? " · AVX2" : " · senza AVX2")
        + $" → tier {Tier}";
}

/// <summary>
/// Rileva le caratteristiche della macchina e propone i modelli adatti. Il tier è un
/// suggerimento: l'utente può sempre scegliere manualmente modelli diversi.
/// </summary>
public static class HardwareDetector
{
    private static HardwareProfile? _cached;
    private static readonly object Gate = new();

    public static HardwareProfile Detect()
    {
        lock (Gate)
        {
            return _cached ??= Query();
        }
    }

    private static HardwareProfile Query()
    {
        var cpuName = "CPU sconosciuta";
        var physical = 0;
        var logical = Environment.ProcessorCount;
        long ramMegabytes = 0;
        string? gpuName = null;
        long vramMegabytes = 0;

        try
        {
            foreach (var processor in QueryObjects("Win32_Processor"))
            {
                cpuName = processor["Name"]?.ToString()?.Trim() ?? cpuName;
                physical = Convert.ToInt32(processor["NumberOfCores"] ?? 0);
                logical = Convert.ToInt32(processor["NumberOfLogicalProcessors"] ?? logical);
                break;
            }
        }
        catch (Exception)
        {
            // WMI non disponibile: si resta sui valori di ripiego
        }

        try
        {
            foreach (var system in QueryObjects("Win32_ComputerSystem"))
            {
                ramMegabytes = Convert.ToInt64(system["TotalPhysicalMemory"] ?? 0L) / (1024 * 1024);
                break;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            long bestVram = 0;
            foreach (var adapter in QueryObjects("Win32_VideoController"))
            {
                var name = adapter["Name"]?.ToString() ?? string.Empty;
                // Win32_VideoController.AdapterRAM è un uint32: sopra i 4 GB riporta valori troncati.
                var reportedMegabytes = Convert.ToInt64(adapter["AdapterRAM"] ?? 0L) / (1024 * 1024);
                var preciseMegabytes = ReadVramFromRegistry(name);
                var megabytes = preciseMegabytes > 0 ? preciseMegabytes : reportedMegabytes;
                if (megabytes > bestVram)
                {
                    bestVram = megabytes;
                    gpuName = name;
                }
            }

            vramMegabytes = bestVram;
        }
        catch (Exception)
        {
        }

        if (physical <= 0)
        {
            physical = Math.Max(1, logical / 2);
        }

        var dedicated = gpuName is not null
                        && (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                            || gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                        && vramMegabytes >= 3500;

        var tier = dedicated
            ? HardwareTier.A
            : physical >= 6
                ? HardwareTier.B
                : HardwareTier.C;

        return new HardwareProfile(
            cpuName,
            physical,
            logical,
            ramMegabytes,
            gpuName,
            vramMegabytes,
            Avx2.IsSupported,
            tier);
    }

    private static ManagementObjectCollection QueryObjects(string wmiClass)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT * FROM {wmiClass}");
        return searcher.Get();
    }

    /// <summary>
    /// La VRAM reale sta nel registro della scheda video: WMI la tronca a 4 GB. Si legge
    /// <c>HardwareInformation.qwMemorySize</c> e si ripiega su WMI se la chiave manca.
    /// </summary>
    private static long ReadVramFromRegistry(string adapterName)
    {
        const string displayClassKey =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        try
        {
            using var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(displayClassKey);
            if (classKey is null)
            {
                return 0;
            }

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (subKeyName.Length != 4 || !subKeyName.All(char.IsDigit))
                {
                    continue;
                }

                using var adapterKey = classKey.OpenSubKey(subKeyName);
                if (adapterKey is null)
                {
                    continue;
                }

                var description = adapterKey.GetValue("DriverDesc")?.ToString();
                if (description is null || !description.Equals(adapterName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var raw = adapterKey.GetValue("HardwareInformation.qwMemorySize");
                var bytes = raw switch
                {
                    long value => value,
                    int value => value,
                    byte[] buffer when buffer.Length >= 8 => BitConverter.ToInt64(buffer, 0),
                    _ => 0L,
                };

                if (bytes > 0)
                {
                    return bytes / (1024 * 1024);
                }
            }
        }
        catch (Exception)
        {
            // registro non accessibile: si resta sulla stima WMI
        }

        return 0;
    }
}
