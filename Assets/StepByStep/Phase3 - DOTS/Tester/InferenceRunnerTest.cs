/*
 * InferenceRunnerTest.cs — InferenceRunner 동작 검증
 * 아무 GameObject에나 붙이고 Play
 *
 * Inspector 설정
 *   2D 모델 (n, inputDim)        : seqLen = 1
 *   3D 모델 (n, seq, inputDim)   : seqLen = 실제 seq 값
 */

using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;

public class InferenceRunnerTest : MonoBehaviour
{
    public ModelAsset modelAsset;

    [Header("모델 설정")]
    public int    inputDim   = 7;    // feat 크기 (마지막 차원)
    /// <summary>
    /// 시퀀스 길이
    /// </summary>
    public int    seqLen     = 1;   // 시퀀스 길이 (2D면 1)
    public int    outputDim  = 2;
    public string inputName  = "observation";
    public string outputName = "action";
    public int    nAgents    = 4;

    async void Start()
    {
        if (modelAsset == null) { Debug.LogError("[Test] modelAsset 없음"); return; }

        using var runner = new InferenceRunner(modelAsset, inputDim, outputDim, inputName, outputName);

        // 에이전트 하나당 입력 길이 = seqLen * inputDim
        int singleLen = seqLen * inputDim;

        // ── 단일 추론 ────────────────────────────────────────────────
        Debug.Log($"=== 단일 추론  입력길이={singleLen} ===");
        var singleIn = new NativeArray<float>(singleLen, Allocator.TempJob);
        for (int i = 0; i < singleLen; i++) singleIn[i] = UnityEngine.Random.Range(-1f, 1f);

        using var singleOut = runner.Run(singleIn);
        Debug.Log($"입력  [{Fmt(singleIn)}]");
        Debug.Log($"출력  [{Fmt(singleOut)}]  길이={singleOut.Length}");
        singleIn.Dispose();

        // ── 배치 추론 ────────────────────────────────────────────────
        Debug.Log($"=== 배치 추론 n={nAgents}  입력길이={nAgents * singleLen} ===");
        var batchIn = new NativeArray<float>(nAgents * singleLen, Allocator.TempJob);
        for (int i = 0; i < batchIn.Length; i++) batchIn[i] = UnityEngine.Random.Range(-1f, 1f);

        using var batchOut = runner.RunBatch(batchIn, nAgents);
        int actualOutDim = batchOut.Length / nAgents;
        Debug.Log($"배치 출력 길이={batchOut.Length}  (n={nAgents} × outDim={actualOutDim})");
        for (int i = 0; i < nAgents; i++)
            Debug.Log($"  ag[{i}] [{FmtSlice(batchOut, i * actualOutDim, actualOutDim)}]");

        using var acts = runner.ArgMaxBatch(batchIn, nAgents);
        for (int i = 0; i < nAgents; i++)
            Debug.Log($"  ag[{i}] action={acts[i]}");

        batchIn.Dispose();

        // ── 비동기 ───────────────────────────────────────────────────
        Debug.Log("=== 비동기 배치 추론 ===");
        var asyncIn = new NativeArray<float>(nAgents * singleLen, Allocator.TempJob);
        for (int i = 0; i < asyncIn.Length; i++) asyncIn[i] = UnityEngine.Random.Range(-1f, 1f);

        using var asyncActs = await runner.ArgMaxBatchAsync(asyncIn, nAgents);
        for (int i = 0; i < nAgents; i++)
            Debug.Log($"  ag[{i}] async action={asyncActs[i]}");

        asyncIn.Dispose();
        Debug.Log("=== 테스트 완료 ===");
    }

    static string Fmt(NativeArray<float> a)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(a[i].ToString("F3")); }
        return sb.ToString();
    }

    static string FmtSlice(NativeArray<float> a, int offset, int len)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < len; i++) { if (i > 0) sb.Append(", "); sb.Append(a[offset + i].ToString("F3")); }
        return sb.ToString();
    }
}