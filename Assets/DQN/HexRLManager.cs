/*
 * HexRLManager.cs
 * ─────────────────────────────────────────────────────────────────────────────
 * Pointy-top 육각 타일 강화학습 환경 매니저 (단일 파일)
 *
 * ▶ 학습 모드  : Python DQN 서버(HTTP POST)와 동기 통신, 모든 에이전트 일괄 처리
 * ▶ 추론 모드  : Unity Sentis(ONNX)로 서버 없이 독립 실행
 *
 * 설치 요구사항
 *   - Unity 2022.3 LTS 이상
 *   - com.unity.sentis (Package Manager)
 *   - Newtonsoft.Json  (Package Manager: com.unity.nuget.newtonsoft-json)
 *
 * 씬 설정
 *   1. 빈 GameObject에 HexRLManager 컴포넌트 추가
 *   2. Inspector에서 agentPrefab, tilePrefab, goalPrefab 연결
 *   3. 학습 시 isInferenceMode = false, 추론 시 true + onnxAsset 연결
 * ─────────────────────────────────────────────────────────────────────────────
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

using Unity.InferenceEngine;

using Newtonsoft.Json;

// ═══════════════════════════════════════════════════════════════════
// 1. 데이터 구조체 (JSON 직렬화)
// ═══════════════════════════════════════════════════════════════════
[Serializable]
public class AgentStepData
{
    public int id;
    public float[] state;        // [dir_x, dir_z, delta_dist]
    public float reward;
    public bool done;
    public float[] prev_state;   // null이면 첫 스텝
    public int prev_action;  // -1이면 첫 스텝
}

[Serializable]
public class StepRequest { public List<AgentStepData> agents; }

[Serializable]
public class StepResponse
{
    public Dictionary<string, int> actions;
    public float epsilon;
    public int steps;
    public float? loss;
}

// ═══════════════════════════════════════════════════════════════════
// 2. 에이전트 런타임 데이터
// ═══════════════════════════════════════════════════════════════════
public class HexAgent
{
    public int id;
    public GameObject go;
    public Vector2Int hexCoord;   // 현재 큐브 좌표 (offset)
    public int action;     // 직전 행동 인덱스
    public float[] prevState;
    public int prevAction = -1;
    public float prevDist;
    public bool firstStep = true;
}

// ═══════════════════════════════════════════════════════════════════
// 3. HexRLManager (MonoBehaviour)
// ═══════════════════════════════════════════════════════════════════
public class HexRLManager : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────
    [Header("Prefabs")]
    public GameObject agentPrefab;
    public GameObject tilePrefab;
    public GameObject goalPrefab;

    [Header("Grid")]
    public int gridRadius = 5;    // 육각 그리드 반지름
    public float hexSize = 1.0f; // 타일 중심 간 거리

    [Header("Episode")]
    public int agentCount = 4;
    public int maxSteps = 200;

    [Header("Server (학습 모드)")]
    public bool isInferenceMode = false;
    public string serverUrl = "http://127.0.0.1:9000";

    [Header("Inference (추론 모드)")]
    public ModelAsset onnxAsset;     // Inspector에서 ONNX 파일 연결
    public float inferenceTickSec = 0.1f;  // 추론 모드 스텝 간격

    // ── Private ──────────────────────────────────────────────────
    private List<Vector2Int> allTiles = new();
    private List<HexAgent> agents = new();
    private GameObject goalObj;
    private Vector2Int goalHex;
    private int stepCount;
    private bool episodeRunning;

    // Sentis 런타임 (추론 모드)
    private Model runtimeModel;
    private Worker worker;

    // ── Pointy-top 육각 6방향 (offset row-odd) ───────────────────
    // 인덱스:  0=NE  1=E   2=SE  3=SW  4=W   5=NW
    // WASD 직관 매핑: 4=W(←), 1=E(→), 5=NW(↖), 0=NE(↗), 3=SW(↙), 2=SE(↘)
    private static readonly Vector2Int[] HexDirs = new Vector2Int[6]
    {
        new( 1,  0),   // 0: NE
        new( 1,  0),   // placeholder – 짝/홀 행마다 달라짐 (아래 함수 참조)
        new( 0,  1),   // 2: SE
        new(-1,  1),   // 3: SW
        new(-1,  0),   // 4: W
        new( 0, -1),   // 5: NW – NW(홀수행)
    };

    // ─────────────────────────────────────────────────────────────
    // Pointy-top offset 이동: col은 행의 홀/짝에 따라 달라짐
    // ─────────────────────────────────────────────────────────────
    private Vector2Int HexNeighbor(Vector2Int h, int dir)
    {
        bool odd = (h.y & 1) == 1;
        // Pointy-top offset directions (odd-r)
        Vector2Int[] d = odd
            ? new[]{ new Vector2Int(1,1), new Vector2Int(1,0), new Vector2Int(1,-1),
                     new Vector2Int(0,-1), new Vector2Int(-1,0), new Vector2Int(0,1) }
            : new[]{ new Vector2Int(0,1), new Vector2Int(1,0), new Vector2Int(0,-1),
                     new Vector2Int(-1,-1), new Vector2Int(-1,0), new Vector2Int(-1,1) };
        return h + d[dir];
    }

    // ─────────────────────────────────────────────────────────────
    // 오프셋 좌표 → 월드 좌표 (Pointy-top)
    // ─────────────────────────────────────────────────────────────
    private Vector3 HexToWorld(Vector2Int h)
    {
        float x = hexSize * (h.x + 0.5f * (h.y & 1));
        float z = hexSize * h.y * (Mathf.Sqrt(3f) / 2f);
        return new Vector3(x, 0f, z);
    }

    // ═══════════════════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════════════════
    private void Start()
    {
        BuildGrid();

        if (isInferenceMode && onnxAsset != null)
        {
            runtimeModel = ModelLoader.Load(onnxAsset);
            worker = new Worker(runtimeModel, BackendType.GPUCompute);
            Debug.Log("[HexRL] Sentis 추론 모드 시작");
            StartCoroutine(InferenceLoop());
            return;
        }
        if (!isInferenceMode)
        {
            Debug.Log("[HexRL] 학습 모드 시작");
            StartCoroutine(TrainingLoop());
        }
    }

    private void OnDestroy()
    {
        worker?.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════
    // 그리드 생성
    // ═══════════════════════════════════════════════════════════════
    private void BuildGrid()
    {
        allTiles.Clear();
        for (int r = -gridRadius; r <= gridRadius; r++)
            for (int c = -gridRadius; c <= gridRadius; c++)
            {
                // 큐브 좌표로 변환 후 반지름 체크
                int cx = c - (r - (r & 1)) / 2;
                int cz = r;
                int cy = -cx - cz;
                if (Mathf.Abs(cx) <= gridRadius && Mathf.Abs(cy) <= gridRadius && Mathf.Abs(cz) <= gridRadius)
                {
                    var coord = new Vector2Int(c, r);
                    allTiles.Add(coord);
                    if (tilePrefab != null)
                        Instantiate(tilePrefab, HexToWorld(coord), Quaternion.identity, transform);
                }
            }
        Debug.Log($"[HexRL] 타일 수: {allTiles.Count}");
    }

    // ═══════════════════════════════════════════════════════════════
    // 에피소드 초기화 (동기식 전체 리셋)
    // ═══════════════════════════════════════════════════════════════
    private void ResetEpisode()
    {
        // 기존 에이전트 및 목표물 제거
        foreach (var ag in agents) Destroy(ag.go);
        agents.Clear();
        if (goalObj != null) Destroy(goalObj);

        // 목표 랜덤 배치
        goalHex = allTiles[UnityEngine.Random.Range(0, allTiles.Count)];
        if (goalPrefab != null)
            goalObj = Instantiate(goalPrefab, HexToWorld(goalHex), Quaternion.identity, transform);

        // 에이전트 랜덤 배치 (목표와 겹치지 않게)
        var usedTiles = new HashSet<Vector2Int> { goalHex };
        for (int i = 0; i < agentCount; i++)
        {
            Vector2Int spawn;
            do { spawn = allTiles[UnityEngine.Random.Range(0, allTiles.Count)]; }
            while (usedTiles.Contains(spawn));
            usedTiles.Add(spawn);

            var ag = new HexAgent
            {
                id = i,
                hexCoord = spawn,
                firstStep = true,
                prevAction = -1,
                prevDist = HexDistance(spawn, goalHex),
            };

            if (agentPrefab != null)
                ag.go = Instantiate(agentPrefab, HexToWorld(spawn), Quaternion.identity, transform);

            agents.Add(ag);
        }

        stepCount = 0;
        episodeRunning = true;
    }

    // ═══════════════════════════════════════════════════════════════
    // 학습 루프 (Python 서버와 동기 통신)
    // ═══════════════════════════════════════════════════════════════
    private IEnumerator TrainingLoop()
    {
        ResetEpisode();

        while (true)
        {
            // ── 1. 요청 패킷 구성 ──────────────────────────────
            var req = new StepRequest { agents = new List<AgentStepData>() };

            foreach (var ag in agents)
            {
                float curDist = HexDistance(ag.hexCoord, goalHex);
                float[] state = BuildState(ag);

                var data = new AgentStepData
                {
                    id = ag.id,
                    state = state,
                    reward = ag.firstStep ? 0f : CalcReward(ag, curDist),
                    done = ag.firstStep ? false : (ag.hexCoord == goalHex),
                };

                if (!ag.firstStep)
                {
                    data.prev_state = ag.prevState;
                    data.prev_action = ag.prevAction;
                }

                req.agents.Add(data);
                ag.prevState = state;
                ag.prevDist = curDist;
                ag.firstStep = false;
            }

            // ── 2. HTTP POST /step ──────────────────────────────
            string json = JsonConvert.SerializeObject(req);
            using var www = new UnityWebRequest(serverUrl + "/step", "POST");
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[HexRL] 서버 통신 오류: {www.error}");
                yield return new WaitForSeconds(1f);
                continue;
            }

            // ── 3. 응답 파싱 후 행동 적용 ───────────────────────
            var resp = JsonConvert.DeserializeObject<StepResponse>(www.downloadHandler.text);
            if (resp?.actions == null) { yield return null; continue; }

            bool episodeDone = false;

            foreach (var ag in agents)
            {
                if (!resp.actions.TryGetValue(ag.id.ToString(), out int act)) continue;

                ag.prevAction = act;
                Vector2Int next = HexNeighbor(ag.hexCoord, act);

                if (allTiles.Contains(next))   // 그리드 범위 내 이동만 허용
                {
                    ag.hexCoord = next;
                    if (ag.go != null)
                        ag.go.transform.position = HexToWorld(next);
                }

                if (ag.hexCoord == goalHex) episodeDone = true;
            }

            stepCount++;
            if (stepCount >= maxSteps) episodeDone = true;

            // ── 4. 에피소드 종료 시 전체 리셋 ───────────────────
            if (episodeDone)
            {
                Debug.Log($"[HexRL] 에피소드 종료 (스텝:{stepCount}) | ε={resp.epsilon:F3} loss={resp.loss}");
                ResetEpisode();
            }

            yield return null;  // 1프레임 대기 후 다음 스텝
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 추론 루프 (Sentis ONNX, 서버 불필요)
    // ═══════════════════════════════════════════════════════════════
    private IEnumerator InferenceLoop()
    {
        ResetEpisode();

        while (true)
        {
            foreach (var ag in agents)
            {
                float[] state = BuildState(ag);
                int act = InferAction(state);

                Vector2Int next = HexNeighbor(ag.hexCoord, act);
                if (allTiles.Contains(next))
                {
                    ag.hexCoord = next;
                    if (ag.go != null)
                        ag.go.transform.position = HexToWorld(next);
                }
            }

            stepCount++;
            bool done = agents.Exists(a => a.hexCoord == goalHex) || stepCount >= maxSteps;
            if (done) ResetEpisode();

            yield return new WaitForSeconds(inferenceTickSec);
        }
    }

    private int InferAction(float[] state)
    {
        using var tensor = new Tensor<float>(new TensorShape(1, state.Length), state);
        worker.Schedule(tensor);
        using var output = worker.PeekOutput("q_values") as Tensor<float>;
        output.DownloadToArray();

        int best = 0;
        float bestQ = float.MinValue;
        for (int i = 0; i < 6; i++)
            if (output[0, i] > bestQ) { bestQ = output[0, i]; best = i; }
        return best;
    }

    // ═══════════════════════════════════════════════════════════════
    // 상태 구성: [dir_x, dir_z, delta_dist]
    // ═══════════════════════════════════════════════════════════════
    private float[] BuildState(HexAgent ag)
    {
        Vector3 agPos = HexToWorld(ag.hexCoord);
        Vector3 goalPos = HexToWorld(goalHex);
        Vector3 diff = goalPos - agPos;

        float curDist = diff.magnitude;
        float deltaDist = ag.prevDist - curDist;  // 양수 = 목표에 가까워짐

        Vector3 dir = diff.magnitude > 1e-4f ? diff.normalized : Vector3.zero;

        return new float[] { dir.x, dir.z, deltaDist };
    }

    // ═══════════════════════════════════════════════════════════════
    // 보상 함수
    // ═══════════════════════════════════════════════════════════════
    private float CalcReward(HexAgent ag, float curDist)
    {
        if (ag.hexCoord == goalHex) return 1.0f;    // 도달 성공
        float delta = ag.prevDist - curDist;
        return Mathf.Clamp(delta * 0.1f, -0.5f, 0.5f); // 거리 변화 쉐이핑
    }

    // ═══════════════════════════════════════════════════════════════
    // 육각 거리 (offset → cube 변환 후 계산)
    // ═══════════════════════════════════════════════════════════════
    private float HexDistance(Vector2Int a, Vector2Int b)
    {
        // odd-r offset → cube
        int ac = a.x - (a.y - (a.y & 1)) / 2;
        int bc = b.x - (b.y - (b.y & 1)) / 2;
        int da = ac - bc;
        int db = a.y - b.y;
        int dc = -da - db;
        return (Mathf.Abs(da) + Mathf.Abs(db) + Mathf.Abs(dc)) / 2f;
    }
}