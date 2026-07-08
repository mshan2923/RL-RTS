/*
 * HexRLManager.cs  —  Pointy-top Hex RL 환경 매니저
 * 필수: com.unity.inferenceengine / com.unity.nuget.newtonsoft-json
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Unity.InferenceEngine;
using Newtonsoft.Json;

public enum SpeedMode { Observe = 1, Normal = 2, Fast = 3, Turbo = 4 }
public enum RLMode { Training, Inference }

[Serializable]
public class AgentStepData
{
    public int id; public float[] state; public float reward;
    public bool done; public float[] prev_state; public int prev_action;
}
[Serializable]
public class StepRequest
{
    public List<AgentStepData> agents;
    public bool is_episode_end;
}
[Serializable]
public class StepResponse
{
    public Dictionary<string, int> actions;
    public float epsilon; public int steps; public float? loss;
}

public class HexAgent
{
    public int id; public GameObject go; public Vector2Int hexCoord;
    public Vector2Int prevHexCoord; // ★ 추가: 내적 계산을 위한 직전 위치 저장용
    public float[] prevState; public int prevAction = -1;
    public float prevDist; public bool firstStep = true;
    public Vector3 worldFrom, worldTo; public Coroutine moveCoroutine;
}

public class HexRLManager : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject agentPrefab, tilePrefab, goalPrefab;

    [Header("Grid")]
    public int gridRadius = 5;
    public float hexSize = 1f;
    public Vector3 tileOffset;

    [Header("Episode")]
    public int agentCount = 4, maxSteps = 200;

    [Header("Speed")]
    public SpeedMode speedMode = SpeedMode.Normal;

    [Header("Mode")]
    public RLMode currentMode = RLMode.Training;

    [Header("Server (학습)")]
    public string serverUrl = "http://127.0.0.1:9000";

    [Header("Inference")]
    public ModelAsset modelAsset;

    [Header("Debug")]
    public bool debugLog = true;       // Inspector에서 켜고 끄기
    public int debugAgentId = 0;      // 로그 찍을 에이전트 번호

    static readonly float[] Delay = { 0.4f, 0.1f, 0f, 0f };
    static readonly float[] LerpDur = { 0.35f, 0.08f, 0f, 0f };
    static readonly float[] TScales = { 1f, 1f, 1f, 4f };
    int SI => (int)speedMode - 1;

    List<Vector2Int> allTiles = new();
    List<HexAgent> agents = new();
    GameObject goalObj; Vector2Int goalHex; int stepCount;
    bool _running; SpeedMode _prevSpeed;
    Model _model; Worker _worker;

    // ════════════════════════════════════════════════════════════════
    void Start()
    {
        _prevSpeed = speedMode;
        Time.timeScale = TScales[SI];
        BuildGrid();
        LaunchMode(currentMode);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) speedMode = SpeedMode.Observe;
        if (Input.GetKeyDown(KeyCode.Alpha2)) speedMode = SpeedMode.Normal;
        if (Input.GetKeyDown(KeyCode.Alpha3)) speedMode = SpeedMode.Fast;
        if (Input.GetKeyDown(KeyCode.Alpha4)) speedMode = SpeedMode.Turbo;
        if (_prevSpeed != speedMode) { Time.timeScale = TScales[SI]; _prevSpeed = speedMode; }

        if (Input.GetKeyDown(KeyCode.I) && currentMode == RLMode.Training)
            SwitchToInference();
    }

    void OnDestroy() { Time.timeScale = 1f; DisposeWorker(); }

    void LaunchMode(RLMode mode)
    {
        _running = true; currentMode = mode;
        if (mode == RLMode.Training) { Debug.Log("[HexRL] Training 모드"); StartCoroutine(TrainingLoop()); }
        else SwitchToInference();
    }

    void SwitchToInference()
    {
        if (modelAsset == null) { Debug.LogWarning("[HexRL] modelAsset이 비어있습니다."); return; }
        _running = false;
        DisposeWorker();
        _model = ModelLoader.Load(modelAsset);
        _worker = new Worker(_model, BackendType.GPUCompute);
        currentMode = RLMode.Inference;
        _running = true;
        Debug.Log("[HexRL] Inference 모드 전환 완료");

        // ── 모델 입출력 정보 덤프 ──────────────────────────────────
        Debug.Log("=== Model Input/Output Info ===");
        foreach (var inp in _model.inputs)
            Debug.Log($"  INPUT  name='{inp.name}'  shape={inp.shape}");
        foreach (var outp in _model.outputs)
            Debug.Log($"  OUTPUT name='{outp.name}'  shape={outp}");
        Debug.Log("================================");

        ResetEpisode();
        StartCoroutine(InferenceLoop());
    }

    void DisposeWorker() { _worker?.Dispose(); _worker = null; _model = null; }

    // ════════════════════════════════════════════════════════════════
    // 그리드
    // ════════════════════════════════════════════════════════════════
    void BuildGrid()
    {
        allTiles.Clear();
        for (int r = -gridRadius; r <= gridRadius; r++)
            for (int c = -gridRadius; c <= gridRadius; c++)
            {
                int cx = c - (r - (r & 1)) / 2, cz = r, cy = -cx - cz;
                if (Mathf.Abs(cx) <= gridRadius && Mathf.Abs(cy) <= gridRadius && Mathf.Abs(cz) <= gridRadius)
                {
                    allTiles.Add(new Vector2Int(c, r));
                    if (tilePrefab) Instantiate(tilePrefab, HexToWorld(new Vector2Int(c, r)) + tileOffset, Quaternion.identity, transform);
                }
            }
        Debug.Log($"[HexRL] 타일: {allTiles.Count}");
    }

    // ════════════════════════════════════════════════════════════════
    // 에피소드 리셋
    // ════════════════════════════════════════════════════════════════
    void ResetEpisode()
    {
        foreach (var ag in agents)
        {
            if (ag.moveCoroutine != null) StopCoroutine(ag.moveCoroutine);
            if (ag.go) Destroy(ag.go);
        }
        agents.Clear();
        if (goalObj) Destroy(goalObj);

        goalHex = allTiles[UnityEngine.Random.Range(0, allTiles.Count)];
        if (goalPrefab) goalObj = Instantiate(goalPrefab, HexToWorld(goalHex), Quaternion.identity, transform);

        var used = new HashSet<Vector2Int> { goalHex };
        for (int i = 0; i < agentCount; i++)
        {
            Vector2Int sp;
            do { sp = allTiles[UnityEngine.Random.Range(0, allTiles.Count)]; } while (used.Contains(sp));
            used.Add(sp);
            var wp = HexToWorld(sp);
            var ag = new HexAgent
            {
                id = i,
                hexCoord = sp,
                firstStep = true,
                prevAction = -1,
                prevDist = HexDist(sp, goalHex),
                worldFrom = wp,
                worldTo = wp
            };
            if (agentPrefab) ag.go = Instantiate(agentPrefab, wp, Quaternion.identity, transform);
            agents.Add(ag);
        }
        stepCount = 0;

        if (debugLog)
            Debug.Log($"[HexRL] ResetEpisode  goalHex={goalHex}");
    }

    void RespawnSingleAgent(HexAgent ag)
    {
        if (ag.moveCoroutine != null) StopCoroutine(ag.moveCoroutine);
        var used = new HashSet<Vector2Int> { goalHex };
        foreach (var a in agents) if (a != ag) used.Add(a.hexCoord);
        Vector2Int sp;
        do { sp = allTiles[UnityEngine.Random.Range(0, allTiles.Count)]; } while (used.Contains(sp));
        ag.hexCoord = sp; ag.firstStep = true; ag.prevAction = -1;
        ag.prevDist = HexDist(sp, goalHex);
        var wp = HexToWorld(sp); ag.worldFrom = wp; ag.worldTo = wp;
        if (ag.go) ag.go.transform.position = wp;
    }

    // ════════════════════════════════════════════════════════════════
    // 이동
    // ════════════════════════════════════════════════════════════════
    void MoveAgent(HexAgent ag, Vector2Int next)
    {
        ag.hexCoord = next;
        var dest = HexToWorld(next);
        if (!ag.go) return;
        float dur = LerpDur[SI];
        if (dur > 0f)
        {
            if (ag.moveCoroutine != null) StopCoroutine(ag.moveCoroutine);
            ag.worldFrom = ag.go.transform.position; ag.worldTo = dest;
            ag.moveCoroutine = StartCoroutine(LerpMove(ag, dur));
        }
        else ag.go.transform.position = dest;
    }

    IEnumerator LerpMove(HexAgent ag, float dur)
    {
        float t = 0; var from = ag.worldFrom; var to = ag.worldTo;
        while (t < dur)
        {
            if (!ag.go) yield break;
            t += Time.deltaTime;
            ag.go.transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, t / dur));
            yield return null;
        }
        if (ag.go) ag.go.transform.position = to;
    }

    // ════════════════════════════════════════════════════════════════
    // 학습 루프
    // ════════════════════════════════════════════════════════════════
    IEnumerator TrainingLoop()
    {
        ResetEpisode();
        while (_running)
        {
            bool isEpisodeEnd = (stepCount + 1 >= maxSteps);
            var req = new StepRequest { agents = new List<AgentStepData>(), is_episode_end = isEpisodeEnd };

            foreach (var ag in agents)
            {
                float cd = HexDist(ag.hexCoord, goalHex);
                var st = BuildState(ag, cd);
                bool arrived = Vector2.Distance((Vector2)ag.hexCoord, (Vector2)goalHex) <= 2.5f;//ag.hexCoord == goalHex;

                var d = new AgentStepData
                {
                    id = ag.id,
                    state = st,
                    reward = ag.firstStep ? 0f : Reward(ag, cd),
                    done = arrived
                };
                if (!ag.firstStep) { d.prev_state = ag.prevState; d.prev_action = ag.prevAction; }
                req.agents.Add(d);

                ag.prevState = st;
                ag.prevDist = cd;
                ag.firstStep = false;

                if (arrived) RespawnSingleAgent(ag);
            }

            using var www = Post(serverUrl + "/step", JsonConvert.SerializeObject(req));
            yield return www.SendWebRequest();
            if (!_running) yield break;
            if (www.result != UnityWebRequest.Result.Success)
            { Debug.LogError($"[HexRL] {www.error}"); yield return new WaitForSeconds(1f); continue; }

            var resp = JsonConvert.DeserializeObject<StepResponse>(www.downloadHandler.text);
            if (resp?.actions == null) { yield return null; continue; }

            foreach (var ag in agents)
            {
                if (!resp.actions.TryGetValue(ag.id.ToString(), out int act)) continue;
                ag.prevAction = act;
                var next = Neighbor(ag.hexCoord, act);

                ag.prevHexCoord = ag.hexCoord; // ★ 이동 직전에 현재 위치를 무조건 저장!
                if (allTiles.Contains(next)) MoveAgent(ag, next);
            }

            if (++stepCount >= maxSteps)
            {
                Debug.Log($"[HexRL] ep끝 step:{stepCount} ε={resp.epsilon:F3} loss={resp.loss}");
                ResetEpisode();
            }

            float delay = Delay[SI];
            if (delay > 0) yield return new WaitForSeconds(delay); else yield return null;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 추론 루프
    // ════════════════════════════════════════════════════════════════
    IEnumerator InferenceLoop()
    {
        int logStep = 0;
        while (_running)
        {
            var snapshot = new float[agents.Count][];
            for (int i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                float cd = HexDist(ag.hexCoord, goalHex);
                snapshot[i] = BuildState(ag, cd);

                // ★ [버그 수정] 상태 빌드 직후에 prevDist를 업뎃해야 다음 턴 delta가 살아나!
                ag.prevDist = cd;
            }

            bool done = false;
            for (int i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                int act = Infer(snapshot[i], ag.id, logStep);
                ag.prevAction = act;
                var next = Neighbor(ag.hexCoord, act);

                ag.prevHexCoord = ag.hexCoord; // ★ 추론 모드에서도 동일하게 직전 위치 추적
                if (allTiles.Contains(next)) MoveAgent(ag, next);

                if (ag.hexCoord == goalHex) done = true;
            }

            logStep++;

            if (++stepCount >= maxSteps || done)
            {
                Debug.Log($"[HexRL-Inf] 에피소드 종료  step={stepCount}  done={done}");
                ResetEpisode();
                logStep = 0;
            }

            // (기존 루프 맨 아래에 있던 ag.prevDist = HexDist(...) 코드는 완전히 삭제해야 해)
            float delay = Delay[SI];
            if (delay > 0) yield return new WaitForSeconds(delay); else yield return null;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Infer  — 상세 로그 포함
    // ════════════════════════════════════════════════════════════════
    int Infer(float[] state, int agentId, int step)
    {
        if (_worker == null) return UnityEngine.Random.Range(0, 6);

        using var inputTensor = new Tensor<float>(new TensorShape(1, state.Length), state);
        _worker.SetInput("state", inputTensor);
        _worker.Schedule();

        var ot = _worker.PeekOutput("q_values") as Tensor<float>;
        if (ot == null)
        {
            Debug.LogError($"[Infer] PeekOutput('q_values') == null  agent={agentId}");
            return UnityEngine.Random.Range(0, 6);
        }

        using var result = ot.ReadbackAndClone();

        // Q값 전체 읽기
        float[] qVals = new float[6];
        for (int i = 0; i < 6; i++) qVals[i] = result[0, i];

        int best = 0;
        float bq = float.MinValue;
        for (int i = 0; i < 6; i++)
            if (qVals[i] > bq) { bq = qVals[i]; best = i; }

        // ── 로그: 첫 5스텝 + 이후 10스텝마다, 지정 에이전트만 ──────
        if (debugLog && agentId == debugAgentId && (step < 5 || step % 10 == 0))
        {
            Debug.Log(
                $"[Infer] step={step}  agent={agentId}\n" +
                $"  state : dir_x={state[0]:F3}  dir_z={state[1]:F3}  delta={state[2]:F3}\n" +
                $"  Q     : [{string.Join(", ", System.Array.ConvertAll(qVals, v => v.ToString("F3")))}]\n" +
                $"  action: {best}  (Q={bq:F3})"
            );
        }

        return best;
    }

    // ════════════════════════════════════════════════════════════════
    // 헬퍼
    // ════════════════════════════════════════════════════════════════
    float[] BuildState(HexAgent ag, float currentDist)
    {
        var diff = HexToWorld(goalHex) - HexToWorld(ag.hexCoord);
        var dir = currentDist > 0 ? diff.normalized : Vector3.zero;
        float delta = ag.prevDist - currentDist;
        return new[] { dir.x, dir.z, delta };
    }

    float Reward(HexAgent ag, float cd)
    {
        if (ag.hexCoord == goalHex) return 1f;

        // 만약 움직이지 않고 제자리에 멈췄거나 벽을 들이받았다면 페널티
        if (ag.hexCoord == ag.prevHexCoord) return -0.1f;

        // 1. 내가 실제로 움직인 방향 벡터 (World Space)
        Vector3 moveDir = (HexToWorld(ag.hexCoord) - HexToWorld(ag.prevHexCoord)).normalized;
        // 2. 출발지점에서 목적지를 향하는 올바른 방향 벡터
        Vector3 goalDir = (HexToWorld(goalHex) - HexToWorld(ag.prevHexCoord)).normalized;

        // 3. 두 벡터의 내적 (똑바로 가면 1.0, 90도 옆길은 0.0, 역주행은 -1.0)
        float dot = Vector3.Dot(moveDir, goalDir);

        // 거리 변화량(distDelta)과 방향 일치도(dotReward)를 적절히 융합
        float distDelta = (ag.prevDist - cd) * 0.1f;
        float dotReward = dot * 0.05f;

        return Mathf.Clamp(distDelta + dotReward, -0.5f, 0.5f);
    }

    Vector2Int Neighbor(Vector2Int h, int dir)
    {
        bool odd = (h.y & 1) == 1;
        var d = odd ? new[]{ new Vector2Int(1,1),new Vector2Int(1,0),new Vector2Int(1,-1),
                              new Vector2Int(0,-1),new Vector2Int(-1,0),new Vector2Int(0,1) }
                    : new[]{ new Vector2Int(0,1),new Vector2Int(1,0),new Vector2Int(0,-1),
                              new Vector2Int(-1,-1),new Vector2Int(-1,0),new Vector2Int(-1,1) };
        return h + d[dir];
    }

    Vector3 HexToWorld(Vector2Int h)
        => new(hexSize * (h.x + 0.5f * (h.y & 1)), 0f, hexSize * h.y * (Mathf.Sqrt(3f) / 2f));

    float HexDist(Vector2Int a, Vector2Int b)
    {
        int ac = a.x - (a.y - (a.y & 1)) / 2, bc = b.x - (b.y - (b.y & 1)) / 2;
        int da = ac - bc, db = a.y - b.y, dc = -da - db;
        return (Mathf.Abs(da) + Mathf.Abs(db) + Mathf.Abs(dc)) / 2f;
    }

    static UnityWebRequest Post(string url, string json)
    {
        var w = new UnityWebRequest(url, "POST");
        w.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        w.downloadHandler = new DownloadHandlerBuffer();
        w.SetRequestHeader("Content-Type", "application/json");
        return w;
    }

    // ════════════════════════════════════════════════════════════════
    // HUD
    // ════════════════════════════════════════════════════════════════
    void OnGUI()
    {
        var n = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        var b = new GUIStyle(n) { fontStyle = FontStyle.Bold };
        b.normal.textColor = Color.yellow;

        float hw = 220f, hh = 102f, hx = Screen.width - hw - 10f, hy = 10f;
        GUI.Box(new Rect(hx, hy, hw, hh), "");
        GUI.Label(new Rect(hx + 8, hy + 4, hw, 18), "Speed  [1~4]", n);
        string[] sl ={"[1] Observe  0.4s lerp ON","[2] Normal   0.1s lerp ON",
                     "[3] Fast     frame lerp OFF","[4] Turbo  4×time lerp OFF"};
        for (int i = 0; i < 4; i++)
        {
            bool cur = SI == i;
            GUI.Label(new Rect(hx + 8, hy + 24 + i * 18, hw - 8, 18), (cur ? "▶ " : "   ") + sl[i], cur ? b : n);
        }

        float pw = 240f, ph = 52f, px = Screen.width - pw - 10f, py = Screen.height - ph - 10f;
        GUI.Box(new Rect(px, py, pw, ph), "");
        string ml = currentMode == RLMode.Training ? "🔵 Training" : "🟢 Inference (InferenceEngine)";
        GUI.Label(new Rect(px + 8, py + 6, pw, 18), ml, b);
        string hint = currentMode == RLMode.Training
            ? "서버 E → .onnx 저장  |  I 키 → 추론 전환"
            : "로컬 추론 실행 중";
        GUI.Label(new Rect(px + 8, py + 26, pw, 18), hint, n);
    }
}