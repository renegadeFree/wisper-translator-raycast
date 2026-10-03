using System.Diagnostics;
using System.Runtime.InteropServices;
using WisperTranslator.Core.Audio;

// Isola la manutenzione della finestra audio; non misura ASR, rete o UI.
const int capacity = 45 * 16000;
const int blocks = 60 * 16000 / 512;
var block = Enumerable.Repeat(0.25f, 512).ToArray();
var initial = new float[capacity];

(double Milliseconds, long Bytes) Measure(bool circular)
{
    // La vecchia List, dopo la crescita iniziale, aveva capacità 2^20.
    var list = circular ? null : new List<float>(1 << 20);
    list?.AddRange(initial);
    var ring = circular ? new FloatRingBuffer(capacity) : null;
    ring?.Write(initial);
    var gate = new object();
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var started = Stopwatch.GetTimestamp();
    for (var index = 0; index < blocks; index++)
    {
        if (ring is not null) ring.Write(block);
        else lock (gate)
        {
            list!.AddRange(block.AsSpan().ToArray());
            list.RemoveRange(0, list.Count - capacity);
        }
    }
    var result = (Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        GC.GetAllocatedBytesForCurrentThread() - allocated);
    var samples = ring?.Snapshot() ?? list!.ToArray();
    if (samples.Length != capacity || samples.Any(sample => sample != 0.25f))
        throw new InvalidOperationException("Finestra audio non equivalente.");
    return result;
}

for (var index = 0; index < 3; index++) { Measure(false); Measure(true); }
var previous = Enumerable.Range(0, 7).Select(_ => Measure(false)).OrderBy(item => item.Milliseconds).ElementAt(3);
var current = Enumerable.Range(0, 7).Select(_ => Measure(true)).OrderBy(item => item.Milliseconds).ElementAt(3);
Console.WriteLine($"{RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; .NET {Environment.Version}");
Console.WriteLine($"60 s di blocchi dopo saturazione della finestra da 45 s; mediana di 7 passaggi (3 warmup).");
Console.WriteLine($"List precedente: {previous.Milliseconds:F3} ms; {previous.Bytes:N0} byte allocati");
Console.WriteLine($"Ring attuale:    {current.Milliseconds:F3} ms; {current.Bytes:N0} byte allocati");
Console.WriteLine($"Rapporto: {previous.Milliseconds / current.Milliseconds:F1}x (solo gestione del buffer)");
