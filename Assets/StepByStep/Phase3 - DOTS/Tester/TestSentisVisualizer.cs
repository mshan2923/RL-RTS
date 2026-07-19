using UnityEngine;
using Unity.Collections;
using Unity.InferenceEngine;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Transforms; // Sentis 네임스페이스

public class SentisVisualizer : MonoBehaviour
{
    [Header("Sentis Setup")]
    public ModelAsset modelAsset;
    
    [Header("Agents")]
    public Transform Target;
    
    private InferenceRunner _runner;
    public InferenceRunnerTest runnerTest;
    private int _nAgents;
    [Header("Reset Settings")]
    public float arrivalThreshold = 0.5f; // 이 거리 안에 들어오면 도착으로 간주
    public float spawnRange = 10f;        // 랜덤 위치 생성 범위 (-10 ~ 10)
    public float Speed = 10;
    public float MapSize = 10f;
    private const float FixedStepSize = 0.02f;

    async void Start()
    {

        _runner = new InferenceRunner(runnerTest.modelAsset, runnerTest.inputDim, runnerTest.outputDim);

        _nAgents = Phase3Connecter.Instace.Amount;

            Debug.Log("--");
        await LateStart();
    }

        async Awaitable LateStart()
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;

            while(Phase3Connecter.Instace.Units.Count == 0)
                await Awaitable.NextFrameAsync();

            foreach(var (v,k) in Phase3Connecter.Instace.Units)
            {
                v.transform.position = new Vector3(UnityEngine.Random.Range(1f, MapSize * 2 - 1f), 0.5f, UnityEngine.Random.Range(1f, MapSize * 2 - 1f));
                em.SetComponentData(k, new LocalTransform
                {
                   Position = v.transform.position,
                   Scale = 0.25f 
                });
                em.SetComponentData(k , new MoveTargetComponent
                {
                    MoveTo = v.transform.position
                });
            }

            Debug.Log($"-- {Phase3Connecter.Instace.Units.Count}");
        }


    void Update()
    {
        // 1. 관측치 배열 생성
        var obsInput = new NativeArray<float>(_nAgents * runnerTest.inputDim, Allocator.TempJob);

        var data = Phase3Connecter.Instace.Units;

        for (int i = 0; i < _nAgents; i++)
        {
            var trans = data[i].Item1.transform;
            var entity = data[i].Item2;
            var wall = World.DefaultGameObjectInjectionWorld.EntityManager.GetComponentData<DetectWallNormalize>(entity);


            // 2. 관측값 계산 (새 위치가 반영됨)
            var pos = Target.position - trans.position;


            obsInput[i * runnerTest.inputDim + 0] = pos.x / MapSize; 
            obsInput[i * runnerTest.inputDim + 1] = pos.z / MapSize;

            obsInput[i * runnerTest.inputDim + 2] = wall.n0;
            obsInput[i * runnerTest.inputDim + 3] = wall.n1;
            obsInput[i * runnerTest.inputDim + 4] = wall.n2;
            obsInput[i * runnerTest.inputDim + 5] = wall.n3;
            obsInput[i * runnerTest.inputDim + 6] = wall.n4;
            obsInput[i * runnerTest.inputDim + 7] = wall.n5;
        }

        // 3. 추론 (기존과 동일)
        using var actions = _runner.RunBatch(obsInput, _nAgents);

        // 4. 결과 적용 (기존과 동일)
        for (int i = 0; i < _nAgents; i++)
        {
            var trans = data[i].Item1.transform;

            float ax = actions[i * runnerTest.outputDim + 0];
            float ay = actions[i * runnerTest.outputDim + 1];


            trans.Translate(new Vector3(ax, 0, ay) * Speed * FixedStepSize, Space.World);

            // trans.position += move;

            {
                Phase3Connecter.Instace.SyncTransform(i, trans.position);
            }
        }

        obsInput.Dispose();
    }

    void OnDestroy()
    {
        _runner?.Dispose();
    }
}