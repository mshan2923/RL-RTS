using UnityEngine;
using Unity.Collections;
using Unity.InferenceEngine;
using Unity.Mathematics; // Sentis 네임스페이스

public class SentisVisualizer : MonoBehaviour
{
    [Header("Sentis Setup")]
    public ModelAsset modelAsset;
    
    [Header("Agents")]
    public Transform Target;
    public Transform[] agentTransforms; // 테스트할 큐브들
    
    private InferenceRunner _runner;
    public InferenceRunnerTest runnerTest;
    private int _nAgents;
    [Header("Reset Settings")]
    public float arrivalThreshold = 0.5f; // 이 거리 안에 들어오면 도착으로 간주
    public float spawnRange = 10f;        // 랜덤 위치 생성 범위 (-10 ~ 10)
    public float Speed = 10;
    public float MapSize = 10f;

    void Start()
    {
        _nAgents = agentTransforms.Length;

        _runner = new InferenceRunner(runnerTest.modelAsset, runnerTest.inputDim, runnerTest.outputDim);

        Phase3Connecter.Instace.Amount = _nAgents;
    }


    void Update()
    {
        // 1. 관측치 배열 생성
        var obsInput = new NativeArray<float>(_nAgents * runnerTest.inputDim, Allocator.TempJob);

        for (int i = 0; i < _nAgents; i++)
        {
            // --- [추가] 도착 검사 및 재배치 로직 ---
            float dist = agentTransforms[i].position.magnitude; // (0,0,0)까지의 거리
            
            if (dist < arrivalThreshold)
            {
                agentTransforms[i].position = new Vector3(
                    UnityEngine.Random.Range(-spawnRange, spawnRange), 
                    0, 
                    UnityEngine.Random.Range(-spawnRange, spawnRange)
                );
            }
            // --------------------------------------

            // 2. 관측값 계산 (새 위치가 반영됨)
            var pos = Target.position - agentTransforms[i].position;

            obsInput[i * runnerTest.inputDim + 0] = pos.x / MapSize; 
            obsInput[i * runnerTest.inputDim + 1] = pos.z / MapSize;

            //! ==============  관측값!! ========
        }

        // 3. 추론 (기존과 동일)
        using var actions = _runner.RunBatch(obsInput, _nAgents);

        // 4. 결과 적용 (기존과 동일)
        for (int i = 0; i < _nAgents; i++)
        {
            float ax = actions[i * runnerTest.outputDim + 0];
            float ay = actions[i * runnerTest.outputDim + 1];

            // 이동 적용 (Time.deltaTime 추가해서 부드럽게)
            Vector3 move = new Vector3(ax, 0, ay) * Speed * Time.deltaTime;
            agentTransforms[i].position += move;


            {
                Phase3Connecter.Instace.SetTransform(i, agentTransforms[i].position);
            }
        }

        obsInput.Dispose();
    }

    void OnDestroy()
    {
        _runner?.Dispose();
    }
}