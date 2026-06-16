using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.InferenceEngine; // 프로젝트 환경에 맞는 올바른 네임스페이스 확인
using UnityEngine;

public class LocalInferenceManager : MonoBehaviour
{
    private Model _model;
    private Worker _worker;
    private CancellationTokenSource _cts;
    private Entity _entity;

    // WebSocketManager와 완전히 동일한 구조와 변수명 유지
    public NativeQueue<WebSocketManager.ActionData> ActionQueue;
    public NativeQueue<WebSocketManager.StateData> StateQueue;


    void Awake()
    {
        // 10년 차의 리스크 관리를 반영하여 영속성 앨로케이터 지정
        ActionQueue = new NativeQueue<WebSocketManager.ActionData>(Allocator.Persistent);
        StateQueue = new NativeQueue<WebSocketManager.StateData>(Allocator.Persistent);

        // ECS 엔티티 및 컴포넌트 데이터 등록 (기존 웹소켓 구조와 매칭)
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

        // [주의] 프로젝트 내 ONNX 모델 에셋 로드 경로를 맞춰줘
        // model = ModelLoader.Load(modelAsset); // Sentis 기준인 경우
        _worker = new Worker(_model, BackendType.GPUCompute);

        StartCoroutine(InferenceLoop(_cts.Token));
    }

    IEnumerator InferenceLoop(CancellationToken ct)
    {
        var states = new List<WebSocketManager.StateData>();
        const int featureCount = 12;

        while (!ct.IsCancellationRequested)
        {
            // 1. StateQueue에서 데이터 전부 뽑아오기
            while (StateQueue.TryDequeue(out var s))
            {
                states.Add(s);
            }

            int batchSize = states.Count;
            if (batchSize > 0)
            {
                // 2. 텐서에 밀어 넣을 플랫한 NativeArray 생성 (생략 없이 전처리 채움)
                var tensorData = new NativeArray<float>(batchSize * featureCount, Allocator.TempJob);

                for (int i = 0; i < batchSize; i++)
                {
                    int offset = i * featureCount;
                    var s = states[i];

                    // 파이썬 handler의 state 리스트 추가 순서와 완벽 매칭
                    tensorData[offset + 0] = s.BaseDist;       // unit["baseDist"]
                    tensorData[offset + 1] = s.BaseDir;        // unit["baseDir"]
                    tensorData[offset + 2] = s.CaptureRatio;   // unit["captureRatio"]
                    tensorData[offset + 3] = s.N0;             // unit["n0"]
                    tensorData[offset + 4] = s.N1;             // unit["n1"]
                    tensorData[offset + 5] = s.N2;             // unit["n2"]
                    tensorData[offset + 6] = s.N3;             // unit["n3"]
                    tensorData[offset + 7] = s.N4;             // unit["n4"]
                    tensorData[offset + 8] = s.N5;             // unit["n5"]
                    tensorData[offset + 9] = s.TargetDist;     // float(unit["TargetDist"])
                    tensorData[offset + 10] = s.TargetDir;      // float(unit["TargetDir"])
                    tensorData[offset + 11] = s.TargetActive;   // float(unit["TargetActive"])
                }

                // 3. 입력 텐서 할당 및 비동기 추론 수행
                using var inputTensor = new Tensor<float>(new TensorShape(batchSize, featureCount), tensorData);
                yield return _worker.ScheduleIterable(inputTensor);

                // 4. 추론 결과 텐서 가져오기
                var outputTensor = _worker.PeekOutput() as Tensor<float>;
                int actionCount = outputTensor.shape[1]; // 모델이 출력하는 액션 가짓수 (예: 이동 방향 등)

                // 5. 후처리 로직 완벽히 구현 (ArgMax 연산으로 가장 확률 높은 액션 선택)
                for (int i = 0; i < batchSize; i++)
                {
                    int bestAction = 0;
                    float maxConfidence = float.MinValue;

                    for (int a = 0; a < actionCount; a++)
                    {
                        float confidence = outputTensor[i, a];
                        if (confidence > maxConfidence)
                        {
                            maxConfidence = confidence;
                            bestAction = a;
                        }
                    }

                    // 수신 큐에 데이터 적재하여 ECS 시스템이 읽어가도록 처리
                    ActionQueue.Enqueue(new WebSocketManager.ActionData
                    {
                        UnitId = states[i].UnitId,
                        Action = bestAction
                    });
                }

                states.Clear();
                tensorData.Dispose();
            }

            // 6. 메인 스레드 블로킹 방지 및 타임아웃 제어 (웹소켓 루프 주기와 동기화)
            // await Task.Delay(16, ct);
        }
    }

    void OnDestroy()
    {
        _cts?.Cancel();
        _worker?.Dispose();
        ActionQueue.Dispose();
        StateQueue.Dispose();

        if (World.DefaultGameObjectInjectionWorld != null)
        {
            World.DefaultGameObjectInjectionWorld.EntityManager.DestroyEntity(_entity);
        }
    }
}