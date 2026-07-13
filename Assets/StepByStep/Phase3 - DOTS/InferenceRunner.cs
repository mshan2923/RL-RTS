/*
 * InferenceRunner.cs — Unity Inference Engine 범용 추론 래퍼
 *
 * 사용법
 *   var runner = new InferenceRunner(modelAsset, inputDim:3, outputDim:6);
 *   var runner = new InferenceRunner(modelAsset, inputDim:3, outputDim:6, "obs", "logits");
 *
 * 단일   : Run(NativeArray)         → NativeArray
 * 배치   : RunBatch(NativeArray, n) → NativeArray (n × outputDim flat)
 * 유틸   : ArgMax / ArgMaxBatch / Softmax
 *
 * 반환된 NativeArray는 호출자가 Dispose() 책임
 */

using System;
using System.Text;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;

public class InferenceRunner : IDisposable
{
    public string InputName  { get; }
    public string OutputName { get; }
    public int    InputDim   { get; }
    public int    OutputDim  { get; }

    readonly Worker      _worker;
    bool _disposed;

    public InferenceRunner(
        ModelAsset  asset,
        int         inputDim,
        int         outputDim,
        string      inputName  = null,
        string      outputName = null,
        BackendType backend    = BackendType.GPUCompute)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));

        var model = ModelLoader.Load(asset);
        _worker   = new Worker(model, backend);

        if (model.inputs.Count  == 0) throw new Exception("[InferenceRunner] 입력 없음");
        if (model.outputs.Count == 0) throw new Exception("[InferenceRunner] 출력 없음");

        var inp  = inputName  != null ? FindInput (model, inputName)  : model.inputs[0];
        var outp = outputName != null ? FindOutput(model, outputName) : model.outputs[0];

        InputName   = inp.name;
        OutputName  = outp.name;
        InputDim    = inputDim;
        OutputDim   = outputDim;

        Debug.Log($"[InferenceRunner] input='{InputName}'(dim={InputDim})  " +
                  $"output='{OutputName}'(dim={OutputDim})  shape=\n"//{_inputShape}
                  + DumpModelInfo(model));
    }

    // ════════════════════════════════════════════════════════════════
    // 단일 추론  batch=1
    // ════════════════════════════════════════════════════════════════
    public NativeArray<float> Run(NativeArray<float> input)
    {
        ThrowIfDisposed();
        using var t = MakeTensor(input, 1);
        _worker.SetInput(InputName, t);
        _worker.Schedule();
        return ReadOutput();
    }

    public async Awaitable<NativeArray<float>> RunAsync(NativeArray<float> input)
    {
        ThrowIfDisposed();
        using var t = MakeTensor(input, 1);
        _worker.SetInput(InputName, t);
        _worker.Schedule();
        return await ReadOutputAsync();
    }

    // ════════════════════════════════════════════════════════════════
    // 배치 추론  batch=n  →  출력 길이 = n × OutputDim (flat)
    // ════════════════════════════════════════════════════════════════
    public NativeArray<float> RunBatch(NativeArray<float> input, int n)
    {
        ThrowIfDisposed();
        using var t = MakeTensor(input, n);
        _worker.SetInput(InputName, t);
        _worker.Schedule();
        return ReadOutput();
    }

    public async Awaitable<NativeArray<float>> RunBatchAsync(NativeArray<float> input, int n)
    {
        ThrowIfDisposed();
        using var t = MakeTensor(input, n);
        _worker.SetInput(InputName, t);
        _worker.Schedule();
        return await ReadOutputAsync();
    }

    // ════════════════════════════════════════════════════════════════
    // 유틸
    // ════════════════════════════════════════════════════════════════
    public int ArgMax(NativeArray<float> input)
    {
        using var output = Run(input);
        return BestIdx(output);
    }

    public async Awaitable<int> ArgMaxAsync(NativeArray<float> input)
    {
        using var output = await RunAsync(input);
        return BestIdx(output);
    }

    /// 반환: NativeArray<int> 길이 n  (호출자 Dispose)
    public NativeArray<int> ArgMaxBatch(NativeArray<float> input, int n)
    {
        using var output = RunBatch(input, n);
        return ExtractArgMax(output, n);
    }

    public async Awaitable<NativeArray<int>> ArgMaxBatchAsync(NativeArray<float> input, int n)
    {
        using var output = await RunBatchAsync(input, n);
        return ExtractArgMax(output, n);
    }

    /// 반환: NativeArray<float> 길이 OutputDim  (호출자 Dispose)
    public NativeArray<float> Softmax(NativeArray<float> input)
    {
        var output = Run(input);
        float max  = output[0];
        for (int i = 1; i < output.Length; i++) if (output[i] > max) max = output[i];
        float sum  = 0f;
        for (int i = 0; i < output.Length; i++) { output[i] = Mathf.Exp(output[i] - max); sum += output[i]; }
        for (int i = 0; i < output.Length; i++) output[i] /= sum;
        return output;
    }

    // ════════════════════════════════════════════════════════════════
    // IDisposable
    // ════════════════════════════════════════════════════════════════
    public void Dispose()
    {
        if (_disposed) return;
        _worker?.Dispose();
        _disposed = true;
    }

    // ════════════════════════════════════════════════════════════════
    // 내부 헬퍼
    // ════════════════════════════════════════════════════════════════

    // shape 결정
    // input.Length == n * InputDim          → 2D (n, InputDim)
    // input.Length == n * seq * InputDim    → 3D (n, seq, InputDim)
    Tensor<float> MakeTensor(NativeArray<float> input, int n)
    {
        int total = input.Length;
        int flat  = n * InputDim;

        if (total == flat)
            return new Tensor<float>(new TensorShape(n, InputDim), input.ToArray());

        // 3D: seq = total / flat
        if (total % flat == 0)
        {
            int seq = total / flat;
            return new Tensor<float>(new TensorShape(n, seq, InputDim), input.ToArray());
        }

        // 나눠 떨어지지 않으면 그냥 2D로 (오류는 Inference Engine이 냄)
        Debug.LogWarning($"[InferenceRunner] 입력 길이 {total} 이 n({n}) × inputDim({InputDim}) 의 배수가 아님");
        return new Tensor<float>(new TensorShape(n, InputDim), input.ToArray());
    }

    NativeArray<float> ReadOutput()
    {
        var t = _worker.PeekOutput(OutputName) as Tensor<float>;
        if (t == null)
        {
            Debug.LogError($"[InferenceRunner] '{OutputName}' 출력 없음");
            return new NativeArray<float>(0, Allocator.TempJob);
        }
        using var r  = t.ReadbackAndClone();
        var ro       = r.AsReadOnlyNativeArray();
        var result   = new NativeArray<float>(ro.Length, Allocator.TempJob);
        NativeArray<float>.Copy(ro, result);
        return result;
    }

    async Awaitable<NativeArray<float>> ReadOutputAsync()
    {
        var t = _worker.PeekOutput(OutputName) as Tensor<float>;
        if (t == null)
        {
            Debug.LogError($"[InferenceRunner] '{OutputName}' 출력 없음");
            return new NativeArray<float>(0, Allocator.TempJob);
        }
        using var r  = await t.ReadbackAndCloneAsync();
        var ro       = r.AsReadOnlyNativeArray();
        var result   = new NativeArray<float>(ro.Length, Allocator.TempJob);
        NativeArray<float>.Copy(ro, result);
        return result;
    }

    NativeArray<int> ExtractArgMax(NativeArray<float> output, int n)
    {
        var result = new NativeArray<int>(n, Allocator.TempJob);
        for (int i = 0; i < n; i++)
        {
            int   best = 0;
            float bq   = float.MinValue;
            for (int j = 0; j < OutputDim; j++)
            {
                float v = output[i * OutputDim + j];
                if (v > bq) { bq = v; best = j; }
            }
            result[i] = best;
        }
        return result;
    }

    static int BestIdx(NativeArray<float> v)
    {
        int best = 0;
        for (int i = 1; i < v.Length; i++) if (v[i] > v[best]) best = i;
        return best;
    }

    static Model.Input FindInput(Model m, string name)
    {
        foreach (var i in m.inputs) if (i.name == name) return i;
        throw new Exception($"[InferenceRunner] 입력 '{name}' 없음. 가능: {ListNames(m)}");
    }

    static Model.Output FindOutput(Model m, string name)
    {
        foreach (var o in m.outputs) if (o.name == name) return o;
        throw new Exception($"[InferenceRunner] 출력 '{name}' 없음.");
    }

    static string ListNames(Model m)
    {
        var sb = new StringBuilder();
        foreach (var i in m.inputs) sb.Append($"'{i.name}' ");
        return sb.ToString();
    }

    static string DumpModelInfo(Model m)
    {
        var sb = new StringBuilder();
        foreach (var i in m.inputs)  sb.AppendLine($"  IN  '{i.name}' {i.shape}");
        foreach (var o in m.outputs) sb.AppendLine($"  OUT '{o.name}'");
        return sb.ToString();
    }

    void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InferenceRunner));
    }
}