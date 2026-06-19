using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;
using Unity.InferenceEngine; // 사용 중인 ONNX 네임스페이스
using UnityEngine;

public class WebSocketManager : MonoBehaviour
{
    public enum OperationMode { Training_WS, Inference_ONNX }

    [Header("Mode Setting")]
    public OperationMode mode = OperationMode.Training_WS;

    [Header("ONNX Setting")]
    [SerializeField] private ModelAsset _modelAsset;// 에디터에서 할당할 ONNX 에셋
    private Model _model;

    private Worker _worker;

    [Serializable] class ActionMessage { public List<UnitAction> units; }
    [Serializable] class UnitAction { public int id; public int action; }
    [Serializable] class StateMessage { public List<UnitState> units; public float captureRatio; }

    [Serializable]
    class UnitState
    {
        public int id, col, row, yaw;
        public float reward;
        public bool done;
        public float captureRatio, baseDist, baseDir;
        public float TargetDist, TargetDir, TargetActive;
        public float n0, n1, n2, n3, n4, n5;

        public override string ToString()
        {
            return $"{id} / {col} / {row} / {yaw} / {reward} / {captureRatio} / {baseDist} / {baseDir} / {TargetDist} / {TargetDir} / {TargetActive} / {done}\n {n0} / {n1} / {n2} / {n3} / {n4} / {n5}";
        }
    }

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private Entity _entity;

    public NativeQueue<ActionData> ActionQueue;
    public NativeQueue<StateData> StateQueue;

    public struct ActionData
    {
        public int UnitId;
        public int Action;
    }

    public struct StateData
    {
        public int UnitId;
        public int Col, Row, Yaw;
        public float Reward;
        public bool Done;
        public float CaptureRatio;
        public float BaseDist, BaseDir;
        public float TargetDist, TargetDir, TargetActive;
        public float N0, N1, N2, N3, N4, N5;

        public override string ToString()
        {
            return $"{UnitId} / {Col} / {Row} / {Yaw} / {Reward} / {Done} / {CaptureRatio} / {BaseDist} / {BaseDir} / {TargetDist} / {TargetDir} / {TargetActive} \n {N0} / {N1} / {N2} / {N3} / {N4} / {N5}";
        }
    }

    void Awake()
    {
        ActionQueue = new NativeQueue<ActionData>(Allocator.Persistent);
        StateQueue = new NativeQueue<StateData>(Allocator.Persistent);

        var world = World.DefaultGameObjectInjectionWorld;
        _entity = world.EntityManager.CreateEntity();
        world.EntityManager.AddComponentData(_entity, new WebSocketManagerComponent
        {
            ActionQueue = ActionQueue,
            StateQueue = StateQueue
        });
    }

    async void Start()
    {
        _cts = new CancellationTokenSource();

        if (mode == OperationMode.Training_WS)
        {
            // [학습 모드] 웹소켓 연결 및 기존 루프 구동
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri("ws://localhost:8765"), _cts.Token);
            Debug.Log("[WS] Connected for Training");

            _ = ReceiveLoop(_cts.Token);
            _ = SendLoop(_cts.Token);
        }
        else
        {
            // [추론 모드] 로컬 ONNX 엔진 초기화 및 단일 루프 구동
            if (_modelAsset != null)
            {
                _model = ModelLoader.Load(_modelAsset);
                _worker = new Worker(_model, BackendType.GPUCompute);
                Debug.Log("[ONNX] Inference Engine Loaded");
            }
            else
            {
                Debug.LogError("[ONNX] Model Asset is missing!");
                return;
            }

            // StartCoroutine(LocalInferenceLoop(_cts.Token));
            await LocalInferenceLoopAsync(_cts.Token);
        }
    }

    // 로컬 추론 전용 루프 (웹소켓 없이 데이터 큐만 가지고 다이렉트 처리)
    IEnumerator LocalInferenceLoop(CancellationToken ct)
    {
        var localStates = new List<StateData>();
        const int featureCount = 12; // 파이썬 순서와 일치하는 12개 피처

        while (!ct.IsCancellationRequested)
        {
            while (StateQueue.TryDequeue(out var s))
            {
                localStates.Add(s);
            }

            int batchSize = localStates.Count;
            if (batchSize > 0)
            {
                // 1. 파이썬 handler 순서와 100% 일치하는 전처리
                var tensorData = new NativeArray<float>(batchSize * featureCount, Allocator.TempJob);

                for (int i = 0; i < batchSize; i++)
                {
                    int offset = i * featureCount;
                    var s = localStates[i];

                    tensorData[offset + 0] = s.BaseDist;
                    tensorData[offset + 1] = s.BaseDir;
                    tensorData[offset + 2] = s.CaptureRatio;
                    tensorData[offset + 3] = s.N0;
                    tensorData[offset + 4] = s.N1;
                    tensorData[offset + 5] = s.N2;
                    tensorData[offset + 6] = s.N3;
                    tensorData[offset + 7] = s.N4;
                    tensorData[offset + 8] = s.N5;
                    tensorData[offset + 9] = s.TargetDist;
                    tensorData[offset + 10] = s.TargetDir;
                    tensorData[offset + 11] = s.TargetActive;
                }

                // 2. 입력 텐서 할당 및 비동기 추론
                using var inputTensor = new Tensor<float>(new TensorShape(batchSize, featureCount), tensorData);
                yield return _worker.ScheduleIterable(inputTensor);



                // 4. 결과 출력 및 ArgMax 처리
                var outputTensor = _worker.PeekOutput() as Tensor<float>;
                var result = outputTensor.ReadbackAndClone();
                int actionCount = outputTensor.shape[1];

                for (int i = 0; i < batchSize; i++)
                {
                    int bestAction = 0;
                    float maxConfidence = float.MinValue;

                    for (int a = 0; a < actionCount; a++)
                    {
                        float confidence = result[i, a];
                        if (confidence > maxConfidence)
                        {
                            maxConfidence = confidence;
                            bestAction = a;
                        }
                    }

                    // ECS가 읽어갈 액션 큐에 바로 탑재
                    ActionQueue.Enqueue(new ActionData
                    {
                        UnitId = localStates[i].UnitId,
                        Action = bestAction
                    });

                }

                Debug.Log($"=> {ActionQueue.Count}");
                localStates.Clear();
                tensorData.Dispose();
            }
            else
            {
                yield return null;
            }

        }
    }

    async Awaitable LocalInferenceLoopAsync(CancellationToken ct)
    {
        var localStates = new List<StateData>();
        const int featureCount = 12; // 파이썬 순서와 일치하는 12개 피처

        while (!ct.IsCancellationRequested)
        {

            while (StateQueue.TryDequeue(out var s))
            {
                localStates.Add(s);
            }

            int batchSize = localStates.Count;
            if (localStates.Count > 0)
            {
                // 1. 파이썬 handler 순서와 100% 일치하는 전처리
                var tensorData = new NativeArray<float>(batchSize * featureCount, Allocator.TempJob);

                for (int i = 0; i < batchSize; i++)
                {
                    int offset = i * featureCount;
                    var s = localStates[i];

                    tensorData[offset + 0] = s.BaseDist;
                    tensorData[offset + 1] = s.BaseDir;
                    tensorData[offset + 2] = s.CaptureRatio;
                    tensorData[offset + 3] = s.N0;
                    tensorData[offset + 4] = s.N1;
                    tensorData[offset + 5] = s.N2;
                    tensorData[offset + 6] = s.N3;
                    tensorData[offset + 7] = s.N4;
                    tensorData[offset + 8] = s.N5;
                    tensorData[offset + 9] = s.TargetDist;
                    tensorData[offset + 10] = s.TargetDir;
                    tensorData[offset + 11] = s.TargetActive;
                }

                // 2. 입력 텐서 할당 및 비동기 추론
                using var inputTensor = new Tensor<float>(new TensorShape(batchSize, featureCount), tensorData);
                _worker.Schedule(inputTensor);


                // 4. 결과 출력 및 ArgMax 처리
                var outputTensor = _worker.PeekOutput() as Tensor<float>;
                using var result = await outputTensor.ReadbackAndCloneAsync();
                int actionCount = outputTensor.shape[1];

                for (int i = 0; i < batchSize; i++)
                {
                    int bestAction = 0;
                    float maxConfidence = float.MinValue;

                    for (int a = 0; a < actionCount; a++)
                    {
                        float confidence = result[i, a];
                        if (confidence > maxConfidence)
                        {
                            maxConfidence = confidence;
                            bestAction = a;
                        }
                    }

                    // ECS가 읽어갈 액션 큐에 바로 탑재
                    ActionQueue.Enqueue(new ActionData
                    {
                        UnitId = localStates[i].UnitId,
                        Action = bestAction
                    });
                }

                localStates.Clear();
                tensorData.Dispose();
            }
            else
            {
                // yield return null;
                await Awaitable.NextFrameAsync(ct);
            }

        }
    }


    async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var msg = JsonUtility.FromJson<ActionMessage>(json);
                foreach (var a in msg.units)
                    ActionQueue.Enqueue(new ActionData { UnitId = a.id, Action = a.action });
            }
            catch (Exception e) { Debug.LogError($"[WS] {e.Message}"); break; }
        }
    }

    async Task SendLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var states = new List<UnitState>();
            float captureRatio = 0f;

            while (StateQueue.TryDequeue(out var s))
            {
                states.Add(new UnitState
                {
                    id = s.UnitId,
                    col = s.Col,
                    row = s.Row,
                    yaw = s.Yaw,
                    reward = s.Reward,
                    done = s.Done,
                    captureRatio = s.CaptureRatio,
                    baseDist = s.BaseDist,
                    baseDir = s.BaseDir,
                    n0 = s.N0,
                    n1 = s.N1,
                    n2 = s.N2,  // 추가
                    n3 = s.N3,
                    n4 = s.N4,
                    n5 = s.N5,   // 추가

                    TargetDist = s.TargetDist,
                    TargetDir = s.TargetDir,
                    TargetActive = s.TargetActive
                });
                captureRatio = s.CaptureRatio;
            }

            if (states.Count > 0)
            {
                var json = JsonUtility.ToJson(new StateMessage { units = states, captureRatio = captureRatio });
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }

            await Task.Delay(16, ct);
        }
    }

    void OnDestroy()
    {
        _cts?.Cancel();
        _ws?.Dispose();
        ActionQueue.Dispose();
        StateQueue.Dispose();
        World.DefaultGameObjectInjectionWorld?.EntityManager.DestroyEntity(_entity);
    }
}