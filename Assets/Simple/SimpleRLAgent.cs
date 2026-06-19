using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Unity.InferenceEngine;

public class PPOAgentManager : MonoBehaviour
{
    [Header("모드 설정")]
    public bool isTrainingMode = true;

    [Header("Unity 6 Inference Engine")]
    public ModelAsset inferenceModelAsset;
    private Model runtimeModel;
    private Worker inferenceWorker;

    [Header("개수 설정")]
    public int agentCount = 8;
    public int targetCount = 12;

    [Header("프리팹 설정")]
    public GameObject agentPrefab;
    public GameObject targetPrefab;

    [Header("스폰 범위 설정")]
    public float spawnAreaSize = 20f;

    private List<Transform> agents = new List<Transform>();
    private List<Transform> targets = new List<Transform>();

    private int[] agentTargetIndices;
    private float[] previousDistances;

    private int currentStep = 0;
    public float moveSpeed = 5f;
    public int maxEpisodeSteps = 500;

    // 소켓 통신용
    private TcpClient client;
    private NetworkStream stream;
    private byte[] receiveBuffer = new byte[8192];

    [Serializable]
    public class ActionData
    {
        public List<int> actions;
    }

    void Awake()
    {
        SpawnEnvironmentPool();
    }

    void Start()
    {
        if (isTrainingMode)
        {
            ConnectToPythonServer();
            SendResetEpisode();
        }
        else
        {
            InitInferenceEngine();
        }
    }

    void SpawnEnvironmentPool()
    {
        if (agentPrefab == null || targetPrefab == null) return;

        agentTargetIndices = new int[agentCount];
        previousDistances = new float[agentCount];

        for (int i = 0; i < targetCount; i++)
        {
            Vector3 randomPos = GetRandomPosInMap();
            GameObject targetObj = Instantiate(targetPrefab, randomPos, Quaternion.identity, transform);
            targetObj.name = $"Common_Target_{i}";
            targets.Add(targetObj.transform);
        }

        for (int i = 0; i < agentCount; i++)
        {
            Vector3 randomPos = GetRandomPosInMap();
            GameObject agentObj = Instantiate(agentPrefab, randomPos, Quaternion.identity, transform);
            agentObj.name = $"Agent_{i}";
            agents.Add(agentObj.transform);

            agentTargetIndices[i] = UnityEngine.Random.Range(0, targetCount);
        }

        Debug.Log($"[Unity] 에이전트 {agentCount}개, 공통 타겟 {targetCount}개 배치 완료야.");
    }

    Vector3 GetRandomPosInMap()
    {
        float rx = UnityEngine.Random.Range(-spawnAreaSize, spawnAreaSize);
        float rz = UnityEngine.Random.Range(-spawnAreaSize, spawnAreaSize);
        return new Vector3(rx, 0f, rz);
    }

    void ConnectToPythonServer()
    {
        try
        {
            client = new TcpClient("127.0.0.1", 5000);
            client.ReceiveTimeout = 1000;
            client.SendTimeout = 1000;
            stream = client.GetStream();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Unity] 서버 연결 실패: {e.Message}");
        }
    }

    void SendResetEpisode()
    {
        currentStep = 0;
        ResetAllPositionsAndTargets();

        List<float[]> currentStates = GatherAllStates();
        List<float> dummyRewards = new List<float>(new float[agents.Count]);
        List<bool> dummyDones = new List<bool>(new bool[agents.Count]);

        string json = MakeJsonPayload("reset", currentStates, dummyRewards, dummyDones) + "\n";
        SendDataAndReceiveAction(json);
    }

    void FixedUpdate()
    {
        if (agents.Count == 0) return;

        currentStep++;
        List<float[]> currentStates = GatherAllStates();

        if (isTrainingMode)
        {
            List<float> rewards = new List<float>();
            List<bool> dones = new List<bool>();

            for (int i = 0; i < agents.Count; i++)
            {
                Transform myTarget = targets[agentTargetIndices[i]];
                float currentDist = Vector3.Distance(agents[i].localPosition, myTarget.localPosition);

                float deltaDist = previousDistances[i] - currentDist;
                previousDistances[i] = currentDist;

                float reward = deltaDist * 10f;
                bool done = false;

                // 변경된 핵심 로직: 목표에 성공적으로 도달했을 때
                if (currentDist < 1.2f)
                {
                    reward += 15f;
                    done = true;

                    // 1. 해당 에이전트만 즉시 새로운 랜덤 위치로 리스폰(시각적 피드백 제공)
                    agents[i].localPosition = GetRandomPosInMap();

                    // 2. 새로운 목표물도 무작위로 다시 지정해줘
                    agentTargetIndices[i] = UnityEngine.Random.Range(0, targetCount);

                    // 3. 리스폰된 위치 기준으로 거리 데이터 갱신해
                    previousDistances[i] = Vector3.Distance(agents[i].localPosition, targets[agentTargetIndices[i]].localPosition);
                }

                rewards.Add(reward);
                dones.Add(done);
            }

            if (currentStep >= maxEpisodeSteps)
            {
                SendResetEpisode();
                return;
            }

            string json = MakeJsonPayload("step", currentStates, rewards, dones) + "\n";
            SendDataAndReceiveAction(json);
        }
        else
        {
            // 추론(테스트) 모드에서도 동일하게 도착하면 리스폰되도록 동기화해둠
            for (int i = 0; i < agents.Count; i++)
            {
                float currentDist = Vector3.Distance(agents[i].localPosition, targets[agentTargetIndices[i]].localPosition);
                if (currentDist < 1.2f)
                {
                    agents[i].localPosition = GetRandomPosInMap();
                    agentTargetIndices[i] = UnityEngine.Random.Range(0, targetCount);
                }
            }

            List<int> actions = RunLocalInference(currentStates);
            ApplyActionsToAgents(actions);

            if (currentStep >= maxEpisodeSteps)
            {
                currentStep = 0;
                ResetAllPositionsAndTargets();
            }
        }
    }

    List<float[]> GatherAllStates()
    {
        List<float[]> statesList = new List<float[]>();
        for (int i = 0; i < agents.Count; i++)
        {
            Transform myTarget = targets[agentTargetIndices[i]];
            Vector3 directionToTarget = (myTarget.localPosition - agents[i].localPosition).normalized;

            float angle = Mathf.Atan2(directionToTarget.x, directionToTarget.z);
            float currentDist = Vector3.Distance(agents[i].localPosition, myTarget.localPosition);

            statesList.Add(new float[] { angle, currentDist });
        }
        return statesList;
    }

    void ResetAllPositionsAndTargets()
    {
        for (int i = 0; i < targets.Count; i++)
        {
            targets[i].localPosition = GetRandomPosInMap();
        }

        for (int i = 0; i < agents.Count; i++)
        {
            agents[i].localPosition = GetRandomPosInMap();
            agentTargetIndices[i] = UnityEngine.Random.Range(0, targetCount);
            previousDistances[i] = Vector3.Distance(agents[i].localPosition, targets[agentTargetIndices[i]].localPosition);
        }
    }

    void InitInferenceEngine()
    {
        if (inferenceModelAsset == null) return;
        runtimeModel = ModelLoader.Load(inferenceModelAsset);
        inferenceWorker = new Worker(runtimeModel, BackendType.GPUCompute);
    }

    List<int> RunLocalInference(List<float[]> statesList)
    {
        int batchSize = statesList.Count;
        float[] inputArray = new float[batchSize * 2];
        int index = 0;
        for (int i = 0; i < batchSize; i++)
        {
            inputArray[index++] = statesList[i][0];
            inputArray[index++] = statesList[i][1];
        }

        using (Tensor<float> inputTensor = new Tensor<float>(new TensorShape(batchSize, 2), inputArray))
        {
            inferenceWorker.Schedule(inputTensor);
            Tensor<float> outputTensor = inferenceWorker.PeekOutput() as Tensor<float>;
            if (outputTensor == null) return new List<int>(new int[batchSize]);

            float[] flatOutputs = outputTensor.DownloadToArray();
            List<int> selectedActions = new List<int>();

            for (int i = 0; i < batchSize; i++)
            {
                int maxAct = 0;
                float maxVal = float.MinValue;
                for (int act = 0; act < 4; act++)
                {
                    float val = flatOutputs[i * 4 + act];
                    if (val > maxVal) { maxVal = val; maxAct = act; }
                }
                selectedActions.Add(maxAct);
            }
            return selectedActions;
        }
    }

    string MakeJsonPayload(string command, List<float[]> statesList, List<float> rewards, List<bool> dones)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"command\":\"{command}\",");

        sb.Append("\"states\":[");
        for (int i = 0; i < statesList.Count; i++)
        {
            sb.Append($"[{statesList[i][0].ToString("F6")},{statesList[i][1].ToString("F6")}]");
            if (i < statesList.Count - 1) sb.Append(",");
        }
        sb.Append("],");

        sb.Append("\"rewards\":[");
        for (int i = 0; i < rewards.Count; i++)
        {
            sb.Append(rewards[i].ToString("F6"));
            if (i < rewards.Count - 1) sb.Append(",");
        }
        sb.Append("],");

        sb.Append("\"dones\":[");
        for (int i = 0; i < dones.Count; i++)
        {
            sb.Append(dones[i].ToString().ToLower());
            if (i < dones.Count - 1) sb.Append(",");
        }
        sb.Append("]");
        sb.Append("}");
        return sb.ToString();
    }

    void ApplyActionsToAgents(List<int> actions)
    {
        for (int i = 0; i < agents.Count; i++)
        {
            Vector3 moveDir = Vector3.zero;
            switch (actions[i])
            {
                case 0: moveDir = Vector3.forward; break;
                case 1: moveDir = Vector3.back; break;
                case 2: moveDir = Vector3.left; break;
                case 3: moveDir = Vector3.right; break;
            }
            agents[i].Translate(moveDir * moveSpeed * Time.fixedDeltaTime, Space.Self);
        }
    }

    void SendDataAndReceiveAction(string jsonPayload)
    {
        if (stream == null || !client.Connected) return;
        try
        {
            byte[] sendBytes = Encoding.UTF8.GetBytes(jsonPayload);
            stream.Write(sendBytes, 0, sendBytes.Length);

            int bytesRead = stream.Read(receiveBuffer, 0, receiveBuffer.Length);
            if (bytesRead > 0)
            {
                string rawResponse = Encoding.UTF8.GetString(receiveBuffer, 0, bytesRead);
                string[] lines = rawResponse.Split('\n');
                if (lines.Length > 0 && !string.IsNullOrEmpty(lines[0]))
                {
                    ActionData actionData = JsonUtility.FromJson<ActionData>(lines[0]);
                    if (actionData != null && actionData.actions != null)
                    {
                        ApplyActionsToAgents(actionData.actions);
                    }
                }
            }
        }
        catch (Exception) { }
    }

    void OnDestroy()
    {
        if (stream != null) stream.Close();
        if (client != null) client.Close();
        if (inferenceWorker != null) inferenceWorker.Dispose();
    }
}