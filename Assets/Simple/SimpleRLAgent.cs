// RLManager.cs
// =====================================================================
//  Target Seek RL  ─  Unity 매니저 (단일 파일)
// =====================================================================
//  역할
//    ┌─ 학습 모드 (useInference = false)
//    │    TCP 소켓으로 Python 서버에 관측 전송 → 행동 수신 → 에이전트 이동
//    │    에피소드 종료 시 전체 에이전트/목표물 일괄 제거 후 재배치
//    └─ 추론 모드 (useInference = true)
//         Unity Inference Engine 으로 ONNX 로컬 실행 (서버 불필요)
//         활성 에이전트 전체를 배치로 묶어 프레임당 추론 1회
//
//  관측 (Python 서버와 스펙 동일)
//    obs[0] angleNorm   : 에이전트 정면 → 목표 방향 signed angle / 180°  (-1~1)
//    obs[1] distDelta   : (이전거리 - 현재거리) / moveSpeed  (-1~1, +가까워짐)
//
//  행동 (discrete 3)
//    0 = 좌회전 + 전진   1 = 직진   2 = 우회전 + 전진
//
//  사용법
//    1. 빈 GameObject → 이 스크립트 부착
//    2. Inspector 에서 agentPrefab / targetPrefab 할당
//    3. 학습 : useInference=false, Python 서버 먼저 실행
//    4. 추론 : useInference=true, onnxModel 에 ONNX 파일 할당
//              패키지 매니저 → Unity Inference Engine 설치 필요
//    5. ONNX 수동 export : 런타임 중 RequestExport() 호출
//       예) UI Button OnClick → RLManager.RequestExport
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
    public int numTarget = 8;
    public float spawnRadius = 8f;
    public float minDist = 2f;
    public float moveSpeed = 0.3f;
    public float turnDeg = 15f;
    public float successDist = 0.8f;
    public int maxSteps = 300;
    public float stepPenalty = 0.005f;
    public float successBonus = 5f;

    [Header("학습 서버 연결")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 9000;

    [Header("추론 모드")]
    public bool useInference = false;
    public ModelAsset onnxModel;           // Inference Engine ONNX 파일
    public BackendType backendType = BackendType.CPU;

    // ── 에이전트 상태 ─────────────────────────────────────────────
    struct AgentState
    {
        public Vector3 pos;
        public float heading;     // 도(°), 0 = +Z
        public Vector3 targetPos;
        public float prevDist;
        public float reward;      // 직전 StepAgent 에서 계산된 보상
        public bool done;
        public int steps;
    }

    GameObject[] agentGOs;
    GameObject[] targetGOs;
    AgentState[] states;

    // ── 소켓 (학습 모드) ─────────────────────────────────────────
    TcpClient tcp;
    StreamWriter sw;
    StreamReader sr;
    Thread recvThread;
    readonly Queue<string> recvQ = new Queue<string>();
    readonly object qLock = new object();

    // ── Inference Engine (추론 모드) ─────────────────────────────
    Worker inferWorker;

    // ── 통계 ─────────────────────────────────────────────────────
    int episode = 0;
    float totalReward = 0f;

    // ═════════════════════════════════════════════════════════════
    //  라이프사이클
    // ═════════════════════════════════════════════════════════════
    async void Start()
    {
        try
        {
            if (useInference) InitInference();
            else ConnectServer();

            await EpisodeLoopAsync();
        }
        catch (OperationCanceledException) { /* 씬 언로드 */ }
        catch (Exception e) { Debug.LogError($"[RL] 치명 오류: {e}"); }
    }

    void OnDestroy()
    {
        recvThread?.Abort();
        sw?.Close();
        sr?.Close();
        tcp?.Close();
        inferWorker?.Dispose();
        DestroyAll();
    }

    // ═════════════════════════════════════════════════════════════
    //  초기화
    // ═════════════════════════════════════════════════════════════
    void ConnectServer()
    {
        tcp = new TcpClient(serverIP, serverPort);
        var stream = tcp.GetStream();
        sw = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        sr = new StreamReader(stream, Encoding.UTF8);
        recvThread = new Thread(RecvLoop) { IsBackground = true };
        recvThread.Start();
        Debug.Log($"[RL] 서버 연결: {serverIP}:{serverPort}");
    }

    void RecvLoop()    // 백그라운드 스레드 ─ 소켓에서 줄 단위로 읽어 큐에 적재
    {
        try
        {
            while (true)
            {
                var line = sr.ReadLine();
                if (line == null) break;
                lock (qLock) recvQ.Enqueue(line);
            }
        }
        catch (Exception e) { Debug.LogWarning($"[RL] recv 종료: {e.Message}"); }
    }

    void InitInference()
    {
        if (onnxModel == null) { Debug.LogError("[RL] onnxModel 이 할당되지 않았습니다."); return; }
        var model = ModelLoader.Load(onnxModel);
        inferWorker = new Worker(model, backendType);
        Debug.Log($"[RL] 추론 모드  backend={backendType}");
    }

    // ═════════════════════════════════════════════════════════════
    //  에피소드 루프 (최상위)
    // ═════════════════════════════════════════════════════════════
    async Awaitable EpisodeLoopAsync()
    {
        while (true)
        {
            SpawnAll();
            episode++;
            totalReward = 0f;

            // ── 학습 / 추론 경로 완전 분리 ──────────────────────
            if (useInference)
                await RunInferenceEpisodeAsync();
            else
                await RunTrainingEpisodeAsync();

            Debug.Log($"[RL] Episode {episode:D4}  reward: {totalReward:F2}");
            DestroyAll();
            await Awaitable.NextFrameAsync();   // Destroy 반영 1프레임 대기
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  학습 에피소드  (Python 서버 ↔ 소켓)
    // ═════════════════════════════════════════════════════════════
    async Awaitable RunTrainingEpisodeAsync()
    {
        // 첫 스텝: 초기 관측 전송 (reward=0, done=false)
        SendToServer(isFirst: true);
        string initResp = await WaitResponseAsync();
        ApplyActions(initResp);

        while (true)
        {
            // 행동 적용 후 상태(보상/done) 전송
            SendToServer(isFirst: false);

            if (AllDone())
            {
                // 터미널 전환을 Python 이 저장하도록 응답 1회 수신
                await WaitResponseAsync();
                break;
            }

            string resp = await WaitResponseAsync();
            ApplyActions(resp);
        }
    }

    void SendToServer(bool isFirst)
    {
        var sb = new StringBuilder("{\"type\":\"step\",\"agents\":[");
        for (int i = 0; i < numAgents; i++)
        {
            var (ang, dd) = ComputeObs(i);
            float rew = isFirst ? 0f : states[i].reward;
            bool done = !isFirst && states[i].done;
            sb.Append($"{{\"id\":{i},\"obs\":[{ang:F4},{dd:F4}]," +
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

    void ApplyActions(string json)
    {
        // {"actions":[1,0,2,...]}
        var resp = JsonUtility.FromJson<ActionMsg>(json);
        for (int i = 0; i < numAgents; i++)
        {
            if (states[i].done) continue;
            StepAgent(i, resp.actions[i]);
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  추론 에피소드  (Inference Engine 로컬 실행)
    // ═════════════════════════════════════════════════════════════
    //  ※ 이 경로에는 소켓 대기가 전혀 없으므로 멈추지 않는다.
    //  ※ 활성 에이전트를 배치로 묶어 추론 1회/프레임으로 효율화.
    async Awaitable RunInferenceEpisodeAsync()
    {
        var activeIdx = new List<int>(numAgents);

        while (!AllDone())
        {
            // 활성 에이전트 인덱스 수집
            activeIdx.Clear();
            for (int i = 0; i < numAgents; i++)
                if (!states[i].done) activeIdx.Add(i);

            // 배치 입력 텐서 구성  shape (activeCount, 2)
            int n = activeIdx.Count;
            var inputData = new float[n * 2];
            for (int j = 0; j < n; j++)
            {
                var (ang, dd) = ComputeObs(activeIdx[j]);
                inputData[j * 2] = ang;
                inputData[j * 2 + 1] = dd;
            }

            // 추론
            int[] actions = await BatchInferAsync(inputData, n);

            // 행동 적용
            for (int j = 0; j < n; j++)
                StepAgent(activeIdx[j], actions[j]);

            await Awaitable.NextFrameAsync();
        }
    }

    async Awaitable<int[]> BatchInferAsync(float[] inputData, int batchSize)
    {
        using var input = new Tensor<float>(new TensorShape(batchSize, 2), inputData);
        inferWorker.Schedule(input);

        var outTensor = inferWorker.PeekOutput("q_values") as Tensor<float>;

        // 비동기 CPU 읽기  (GPU 백엔드 사용 시에도 블록 없이 동작)
        using var result = await outTensor.ReadbackAndCloneAsync();

        var actions = new int[batchSize];
        for (int j = 0; j < batchSize; j++)
        {
            int best = 0;
            for (int k = 1; k < 3; k++)
                if (result[j, k] > result[j, best]) best = k;
            actions[j] = best;
        }
        return actions;
    }

    // ═════════════════════════════════════════════════════════════
    //  에이전트 스텝 / 관측 계산
    // ═════════════════════════════════════════════════════════════
    void StepAgent(int i, int action)
    {
        // 회전  (0=좌, 1=직진, 2=우)
        if (action == 0) states[i].heading -= turnDeg;
        else if (action == 2) states[i].heading += turnDeg;

        // 이동  (heading 0° = +Z, 시계방향 양수)
        float rad = states[i].heading * Mathf.Deg2Rad;
        var fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        states[i].pos += fwd * moveSpeed;

        // 비주얼 갱신
        agentGOs[i].transform.SetPositionAndRotation(
            states[i].pos,
            Quaternion.Euler(0f, states[i].heading, 0f));

        // 보상 계산
        float dist = Vector3.Distance(states[i].pos, states[i].targetPos);
        float delta = states[i].prevDist - dist;   // + = 가까워짐
        states[i].reward = delta - stepPenalty;
        states[i].prevDist = dist;
        states[i].steps++;

        // 종료 판정
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

    (float angleNorm, float distDelta) ComputeObs(int i)
    {
        float rad = states[i].heading * Mathf.Deg2Rad;
        var fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        var dir = (states[i].targetPos - states[i].pos).normalized;

        float angle = Vector3.SignedAngle(fwd, dir, Vector3.up);
        float angleNorm = Mathf.Clamp(angle / 180f, -1f, 1f);

        float dist = Vector3.Distance(states[i].pos, states[i].targetPos);
        float delta = Mathf.Clamp(
            (states[i].prevDist - dist) / Mathf.Max(moveSpeed, 1e-5f), -1f, 1f);

        return (angleNorm, delta);
    }

    bool AllDone()
    {
        foreach (var s in states) if (!s.done) return false;
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  스폰 / 제거  (에피소드 단위 일괄)
    // ═════════════════════════════════════════════════════════════
    void SpawnAll()
    {
        agentGOs = new GameObject[numAgents];
        targetGOs = new GameObject[numTarget];
        states = new AgentState[numAgents];

        for (int i = 0; i < numTarget; i++)
        {
            Vector3 tPos;
            tPos = RandPos();
            targetGOs[i] = Instantiate(targetPrefab, tPos, Quaternion.identity);
        }

        for (int i = 0; i < numAgents; i++)
        {
            Vector3 aPos;
            int RanIndex = UnityEngine.Random.Range(0, numTarget);

            var tPos = targetGOs[RanIndex].transform.position;

            do { aPos = RandPos(); }
            while (Vector3.Distance(aPos, tPos) < minDist);

            float h = UnityEngine.Random.Range(0f, 360f);
            agentGOs[i] = Instantiate(agentPrefab, aPos, Quaternion.Euler(0f, h, 0f));


            states[i] = new AgentState
            {
                pos = aPos,
                heading = h,
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
        float ang = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
    }

    void SetColor(int i, Color c)
        => agentGOs[i]?.GetComponent<Renderer>()?.material.SetColor("_Color", c);

    // ═════════════════════════════════════════════════════════════
    //  ONNX 수동 export  (UI 버튼 등에서 호출)
    // ═════════════════════════════════════════════════════════════
    public void RequestExport()
    {
        if (useInference || sw == null) return;
        sw.WriteLine("{\"type\":\"export\"}");
        Debug.Log("[RL] ONNX export 요청 전송");
    }

    // ── JSON 파싱용 ──────────────────────────────────────────────
    [Serializable] class ActionMsg { public int[] actions; }
}