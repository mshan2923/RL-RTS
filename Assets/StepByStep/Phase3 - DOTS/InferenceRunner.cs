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

    readonly Worker _worker;
    readonly int    _modelInputRank; // 모델 원본의 차원 수 저장
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

        InputName       = inp.name;
        OutputName      = outp.name;
        InputDim        = inputDim;
        OutputDim       = outputDim;
        _modelInputRank = inp.shape.rank; // 모델의 입력 차원(Rank) 감지

        Debug.Log($"[InferenceRunner] input='{InputName}'(dim={InputDim})  " +
                  $"output='{OutputName}'(dim={OutputDim}) rank={_modelInputRank}\n" + DumpModelInfo(model));
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
        return ReadOutput(Allocator.TempJob);
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

        // Debug.Log($"<color=cyan>[InferenceRunner 진단]</color> " +
        //       $"입력 배열 길이(input.Length) = {input.Length} | " +
        //       $"요청 배치 크기(n) = {n} | " +
        //       $"설정된 InputDim = {InputDim} | " +
        //       $"이론상 필요 크기(n * InputDim) = {n * InputDim}");
              
        using var t = MakeTensor(input, n);
        _worker.SetInput(InputName, t);
        _worker.Schedule();
        return ReadOutput(Allocator.TempJob);
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
    // 유틸리티 함수
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

    public void Dispose()
    {
        if (_disposed) return;
        _worker?.Dispose();
        _disposed = true;
    }

    // ════════════════════════════════════════════════════════════════
    // 내부 헬퍼 (차원 자동 적응형 텐서 생성 기법)
    // ════════════════════════════════════════════════════════════════
    Tensor<float> MakeTensor(NativeArray<float> input, int n)
    {
        // 모델 원본 차원에 맞춰 셰이프 구조를 분기 처리해서 엔진 내부의 오작동을 차단해
        if (_modelInputRank == 3)
        {
            // (batch, 1, feature) 형태의 3D 구조 대응
            return new Tensor<float>(new TensorShape(n, 1, InputDim), input);
        }
        else if (_modelInputRank == 4)
        {
            // 레거시 배라쿠다 호환성 레이아웃이 섞여 있을 때의 4D 대응
            return new Tensor<float>(new TensorShape(n, 1, 1, InputDim), input);
        }

        // 가장 기본 구조인 2D (batch, feature)
        return new Tensor<float>(new TensorShape(n, InputDim), input);
    }

    NativeArray<float> ReadOutput(Allocator allocator)
    {
        var t = _worker.PeekOutput(OutputName) as Tensor<float>;
        if (t == null)
        {
            Debug.LogError($"[InferenceRunner] '{OutputName}' 출력 없음");
            return new NativeArray<float>(0, allocator);
        }
        using var r  = t.ReadbackAndClone();
        var ro       = r.AsReadOnlyNativeArray();
        var result   = new NativeArray<float>(ro.Length, allocator);
        NativeArray<float>.Copy(ro, result);
        return result;
    }

    async Awaitable<NativeArray<float>> ReadOutputAsync()
    {
        var t = _worker.PeekOutput(OutputName) as Tensor<float>;
        if (t == null)
        {
            Debug.LogError($"[InferenceRunner] '{OutputName}' 출력 없음");
            return new NativeArray<float>(0, Allocator.Persistent);
        }
        using var r  = await t.ReadbackAndCloneAsync();
        var ro       = r.AsReadOnlyNativeArray();
        var result   = new NativeArray<float>(ro.Length, Allocator.Persistent);
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

    static Model.Input FindInput(Model m, string name) { foreach (var i in m.inputs) if (i.name == name) return i; throw new Exception($"입력 '{name}' 없음."); }
    static Model.Output FindOutput(Model m, string name) { foreach (var o in m.outputs) if (o.name == name) return o; throw new Exception($"출력 '{name}' 없음."); }
    static string DumpModelInfo(Model m) { var sb = new StringBuilder(); foreach (var i in m.inputs) sb.AppendLine($"  IN  '{i.name}' {i.shape}"); foreach (var o in m.outputs) sb.AppendLine($"  OUT '{o.name}'"); return sb.ToString(); }
    void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(InferenceRunner)); }
}