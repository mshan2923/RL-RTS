// RLManager.cs  ─  PPO 캐릭터 이동 버전 (상하좌우)
// =====================================================================
//  이전 버전과의 차이
//    [움직임]
//      이전: pos += Vector3(vx,0,vz) * moveSpeed  → vx/vz 크기에 따라 속도 달라짐 (레이싱느낌)
//      수정: moveDir = Vector3(vx,0,vz).normalized → 항상 moveSpeed 일정 (캐릭터 느낌)
//            출력이 [0.1, 0.9] 이든 [1.0, 0.0] 이든 동일 속도로 이동
//
//    [관측]
//      이전: [angle_norm, dist_delta]  (방향 각도)
//      수정: [dx_norm, dz_norm]        (목표까지 X·Z 방향 성분, 정규화)
//             → 캐릭터 이동과 자연스럽게 대응: "오른쪽에 있으면 vx 양수로"
//
//    [export / 종료]
//      이전: readline 무한 대기 → 'e' 눌러도 반응 없음
//      수정: Python 서버 RECV_TIMEOUT=0.3 으로 주기적 플래그 확인 (서버 수정 사항)
//            Unity 측은 그대로 RequestExport() 호출
//
//  사용법
//    1. 빈 GameObject → 스크립트 부착, agentPrefab / targetPrefab 할당
//    2. 학습: useInference=false, train_server_ppo.py 먼저 실행
//    3. 추론: useInference=true, onnxModel 에 ppo_actor.onnx 할당
//             ONNX 출력: "action"  (q_values 아님!)
// =====================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Unity.InferenceEngine;

public class RLManager : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────
    [Header("프리팹")]
    public GameObject agentPrefab;
    public GameObject targetPrefab;

    [Header("에피소드 설정")]
    public int numAgents = 8;
    public float spawnRadius = 8f;
    public float minDist = 2f;
    public float moveSpeed = 0.3f;    // 항상 이 속도로 이동 (방향만 PPO 가 결정)
    public float successDist = 0.8f;
    public int maxSteps = 300;
    public float stepPenalty = 0.005f;
    public float successBonus = 5f;

    [Header("학습 서버")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 9000;

    [Header("추론 모드")]
    public bool useInference = false;
    public ModelAsset onnxModel;
    public BackendType backendType = BackendType.CPU;

    // ── 에이전트 상태 ─────────────────────────────────────────────
    struct AgentState
    {
        public Vector3 pos;
        public Vector3 targetPos;
        public float prevDist;
        public float reward;
        public bool done;
        public int steps;
    }

    GameObject[] agentGOs;
    GameObject[] targetGOs;
    AgentState[] states;

    // ── 소켓 ─────────────────────────────────────────────────────
    TcpClient tcp;
    StreamWriter sw;
    StreamReader sr;
    Thread recvThread;
    readonly Queue<string> recvQ = new Queue<string>();
    readonly object qLock = new object();

    // ── Inference Engine ─────────────────────────────────────────
    Worker inferWorker;

    int episode = 0;
    float totalReward = 0f;

    // ═════════════════════════════════════════════════════════════
    async void Start()
    {
        try
        {
            if (useInference) InitInference();
            else ConnectServer();
            await EpisodeLoopAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogError($"[RL] {e}"); }
    }

    void OnDestroy()
    {
        recvThread?.Abort();
        sw?.Close(); sr?.Close(); tcp?.Close();
        inferWorker?.Dispose();
        DestroyAll();
    }

    // ─── 초기화 ──────────────────────────────────────────────────
    void ConnectServer()
    {
        tcp = new TcpClient(serverIP, serverPort);
        var ns = tcp.GetStream();
        sw = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true };
        sr = new StreamReader(ns, Encoding.UTF8);
        recvThread = new Thread(RecvLoop) { IsBackground = true };
        recvThread.Start();
        Debug.Log($"[RL] 서버 연결: {serverIP}:{serverPort}");
    }

    void RecvLoop()
    {
        try { while (true) { var l = sr.ReadLine(); if (l == null) break; lock (qLock) recvQ.Enqueue(l); } }
        catch (Exception e) { Debug.LogWarning($"[RL] recv: {e.Message}"); }
    }

    void InitInference()
    {
        var model = ModelLoader.Load(onnxModel);
        inferWorker = new Worker(model, backendType);
        Debug.Log("[RL] 추론 모드 (PPO actor)");
    }

    // ─── 에피소드 루프 ───────────────────────────────────────────
    async Awaitable EpisodeLoopAsync()
    {
        while (true)
        {
            SpawnAll();
            episode++;
            totalReward = 0f;

            if (useInference) await RunInferenceEpisodeAsync();
            else await RunTrainingEpisodeAsync();

            Debug.Log($"[RL] Episode {episode:D4}  reward: {totalReward:F2}");
            DestroyAll();
            await Awaitable.NextFrameAsync();
        }
    }

    // ─── 학습 에피소드 ───────────────────────────────────────────
    async Awaitable RunTrainingEpisodeAsync()
    {
        SendToServer(isFirst: true);
        ApplyActions(await WaitResponseAsync());

        while (true)
        {
            SendToServer(isFirst: false);
            if (AllDone()) { await WaitResponseAsync(); break; }  // 터미널 전환 저장
            ApplyActions(await WaitResponseAsync());
        }
    }

    // ─── 추론 에피소드 ───────────────────────────────────────────
    async Awaitable RunInferenceEpisodeAsync()
    {
        var active = new List<int>(numAgents);
        while (!AllDone())
        {
            active.Clear();
            for (int i = 0; i < numAgents; i++) if (!states[i].done) active.Add(i);

            var inputData = new float[active.Count * 2];
            for (int j = 0; j < active.Count; j++)
            {
                var (dx, dz) = ComputeObs(active[j]);
                inputData[j * 2] = dx; inputData[j * 2 + 1] = dz;
            }

            var actions = await BatchInferAsync(inputData, active.Count);
            for (int j = 0; j < active.Count; j++)
                StepAgent(active[j], actions[j, 0], actions[j, 1]);

            await Awaitable.NextFrameAsync();
        }
    }

    async Awaitable<float[,]> BatchInferAsync(float[] inputData, int n)
    {
        using var input = new Tensor<float>(new TensorShape(n, 2), inputData);
        inferWorker.Schedule(input);
        var outTensor = inferWorker.PeekOutput("action") as Tensor<float>;
        using var result = await outTensor.ReadbackAndCloneAsync();

        var actions = new float[n, 2];
        for (int j = 0; j < n; j++)
        {
            actions[j, 0] = Mathf.Clamp(result[j, 0], -1f, 1f);
            actions[j, 1] = Mathf.Clamp(result[j, 1], -1f, 1f);
        }
        return actions;
    }

    // ─── 소켓 통신 ───────────────────────────────────────────────
    void SendToServer(bool isFirst)
    {
        var sb = new StringBuilder("{\"type\":\"step\",\"agents\":[");
        for (int i = 0; i < numAgents; i++)
        {
            var (dx, dz) = ComputeObs(i);
            float rew = isFirst ? 0f : states[i].reward;
            bool done = !isFirst && states[i].done;
            sb.Append($"{{\"id\":{i},\"obs\":[{dx:F4},{dz:F4}]," +
                      $"\"reward\":{rew:F4},\"done\":{(done ? "true" : "false")}}}");
            if (i < numAgents - 1) sb.Append(',');
        }
        sb.Append("]}");
        sw.WriteLine(sb);
    }

    async Awaitable<string> WaitResponseAsync()
    {
        while (true)
        {
            lock (qLock) if (recvQ.Count > 0) return recvQ.Dequeue();
            await Awaitable.NextFrameAsync();
        }
    }

    // JSON: {"actions":[vx0,vz0,vx1,vz1,...]}
    void ApplyActions(string json)
    {
        var resp = JsonUtility.FromJson<ActionMsg>(json);
        for (int i = 0; i < numAgents; i++)
        {
            if (states[i].done) continue;
            StepAgent(i, resp.actions[i * 2], resp.actions[i * 2 + 1]);
        }
    }

    // ─── 에이전트 스텝 ───────────────────────────────────────────
    void StepAgent(int i, float vx, float vz)
    {
        // ★ 핵심 수정: 정규화로 항상 일정 속도 (캐릭터처럼)
        var moveDir = new Vector3(vx, 0f, vz);
        if (moveDir.sqrMagnitude > 1e-4f)
        {
            moveDir.Normalize();
            states[i].pos += moveDir * moveSpeed;
            agentGOs[i].transform.rotation = Quaternion.LookRotation(moveDir, Vector3.up);
        }
        agentGOs[i].transform.position = states[i].pos;

        float dist = Vector3.Distance(states[i].pos, states[i].targetPos);
        float delta = states[i].prevDist - dist;
        states[i].reward = delta - stepPenalty;
        states[i].prevDist = dist;
        states[i].steps++;

        if (dist < successDist)
        {
            states[i].reward += successBonus;
            states[i].done = true;
            SetColor(i, Color.green);
        }
        else if (states[i].steps >= maxSteps)
        {
            states[i].done = true;
            SetColor(i, Color.red);
        }
        totalReward += states[i].reward;
    }

    // ─── 관측: 목표까지 정규화된 X·Z 방향 성분 ──────────────────
    //   레이싱 버전의 "각도"보다 캐릭터 이동과 직접 대응
    //   ex) 목표가 오른쪽에 있으면 dx>0 → actor 가 vx>0 을 학습
    (float dx_norm, float dz_norm) ComputeObs(int i)
    {
        var toTarget = states[i].targetPos - states[i].pos;
        float dist = toTarget.magnitude + 1e-6f;
        return (toTarget.x / dist, toTarget.z / dist);  // 단위 벡터 성분
    }

    // ─── 스폰 / 제거 ─────────────────────────────────────────────
    void SpawnAll()
    {
        agentGOs = new GameObject[numAgents];
        targetGOs = new GameObject[numAgents];
        states = new AgentState[numAgents];

        for (int i = 0; i < numAgents; i++)
        {
            Vector3 aPos, tPos;
            do { aPos = RandPos(); tPos = RandPos(); }
            while (Vector3.Distance(aPos, tPos) < minDist);

            agentGOs[i] = Instantiate(agentPrefab, aPos, Quaternion.identity);
            targetGOs[i] = Instantiate(targetPrefab, tPos, Quaternion.identity);

            states[i] = new AgentState
            {
                pos = aPos,
                targetPos = tPos,
                prevDist = Vector3.Distance(aPos, tPos),
            };
        }
    }

    void DestroyAll()
    {
        if (agentGOs != null) foreach (var o in agentGOs) if (o) Destroy(o);
        if (targetGOs != null) foreach (var o in targetGOs) if (o) Destroy(o);
        agentGOs = null; targetGOs = null;
    }

    Vector3 RandPos()
    {
        float r = UnityEngine.Random.Range(minDist, spawnRadius);
        float a = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
    }

    bool AllDone() { foreach (var s in states) if (!s.done) return false; return true; }

    void SetColor(int i, Color c)
        => agentGOs[i]?.GetComponent<Renderer>()?.material.SetColor("_Color", c);

    // ─── ONNX 수동 export (UI 버튼 등) ──────────────────────────
    public void RequestExport()
    {
        if (useInference || sw == null) return;
        sw.WriteLine("{\"type\":\"export\"}");
        Debug.Log("[RL] export 요청 전송");
    }

    [Serializable] class ActionMsg { public float[] actions; }
}