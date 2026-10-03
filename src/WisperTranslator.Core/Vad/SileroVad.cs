using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace WisperTranslator.Core.Vad;

/// <summary>
/// Silero VAD v5 in ONNX. Il modello lavora a finestre di 512 campioni (32 ms a 16 kHz)
/// a cui antepone 64 campioni di contesto della finestra precedente.
/// </summary>
public sealed class SileroVad : IVadModel
{
    private const int ContextSize = 64;
    private const int StateSize = 2 * 1 * 128;

    private readonly InferenceSession _session;
    private readonly float[] _input = new float[ContextSize + 512];
    private readonly float[] _context = new float[ContextSize];
    private readonly DenseTensor<float> _inputTensor;
    private readonly DenseTensor<float> _stateTensor;
    private readonly DenseTensor<long> _sampleRateTensor;
    private readonly NamedOnnxValue[] _inputs;

    public SileroVad(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Modello VAD non trovato: {modelPath}", modelPath);
        }

        using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        _session = new InferenceSession(modelPath, options);
        _inputTensor = new DenseTensor<float>(_input, [1, _input.Length]);
        _stateTensor = new DenseTensor<float>(new float[StateSize], [2, 1, 128]);
        _sampleRateTensor = new DenseTensor<long>(new[] { 16000L }, []);
        _inputs =
        [
            NamedOnnxValue.CreateFromTensor("input", _inputTensor),
            NamedOnnxValue.CreateFromTensor("state", _stateTensor),
            NamedOnnxValue.CreateFromTensor("sr", _sampleRateTensor),
        ];
    }

    public int FrameSamples => 512;

    public double SpeechProbability(ReadOnlySpan<float> frame)
    {
        if (frame.Length != 512)
        {
            throw new ArgumentException("Il frame VAD deve contenere 512 campioni.", nameof(frame));
        }

        _context.CopyTo(_input, 0);
        frame.CopyTo(_input.AsSpan(ContextSize));

        using var results = _session.Run(_inputs);

        double probability = 0;
        foreach (var result in results)
        {
            if (result.Name == "output")
            {
                probability = result.AsTensor<float>()[0];
            }
            else if (result.Name == "stateN")
            {
                var state = result.AsTensor<float>();
                var destination = _stateTensor.Buffer.Span;
                for (var index = 0; index < StateSize; index++)
                {
                    destination[index] = state.GetValue(index);
                }
            }
        }

        frame[^ContextSize..].CopyTo(_context);
        return probability;
    }

    public void Reset()
    {
        Array.Clear(_context);
        _stateTensor.Buffer.Span.Clear();
    }

    public void Dispose() => _session.Dispose();
}
