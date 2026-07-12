namespace RL_StepByStep
{
        using UnityEngine;
    using Unity.Collections;
    using System.Text;
    using Unity.Mathematics;

    public class Phase3TestManager : MonoBehaviour
    {
        public string serverIp = "127.0.0.1";
        public int serverPort = 50051;
        public int agentCount = 10;
        public float MapRadius = 10;
        public GameObject agentPrefab;
        public Transform targetTransform;
        public float moveSpeed = 5f;

        private PythonTrainingPolicy<Phase3Observation, Phase3Action> trainingPolicy;
        private GameObject[] agents;
        private NativeArray<Phase3Observation> obsArray;
        private NativeArray<Phase3Action> actionArray;

        // 파이썬 응답을 대기 중인지 체크하는 플래그
        private bool isWaiting = false;

        void Start()
        {
            trainingPolicy = new PythonTrainingPolicy<Phase3Observation, Phase3Action>(serverIp, serverPort);
            agents = new GameObject[agentCount];
            obsArray = new NativeArray<Phase3Observation>(agentCount, Allocator.Persistent);
            actionArray = new NativeArray<Phase3Action>(agentCount, Allocator.Persistent);

            for (int i = 0; i < agentCount; i++)
            {
                Vector3 randomPos = new Vector3(UnityEngine.Random.Range(-MapRadius, MapRadius), 0.5f, UnityEngine.Random.Range(-MapRadius, MapRadius));
                agents[i] = Instantiate(agentPrefab, randomPos, Quaternion.identity, transform);
            }
        }

        // [수정] async void로 변경하여 내부에서 await를 쓸 수 있게 함
        async void Update()
        {
            if (targetTransform == null) return;
            
            // 이미 파이썬이 계산 중이라면 이번 프레임은 그냥 통과 (화면 멈춤 방지)
            if (isWaiting) return;

            isWaiting = true;

            // 관측 데이터 수집
            for (int i = 0; i < agentCount; i++)
            {
                Vector3 agentPos = agents[i].transform.position;
                Vector3 targetPos = targetTransform.position;
                Vector3 directionToTarget = (targetPos - agentPos).normalized;

                var isDone = CalculateReward(agents[i], targetTransform, float3.zero, out var reward);

                Debug.Log($"{isDone} : {reward}");

                obsArray[i] = new Phase3Observation
                {
                    unitId = i,
                    dx = directionToTarget.x,
                    dy = directionToTarget.z,
                    reward = reward,
                    done = isDone ? 1 : 0
                };
                
                if (isDone)
                    agents[i].transform.position = new Vector3(UnityEngine.Random.Range(-MapRadius, MapRadius), 0.5f, UnityEngine.Random.Range(-MapRadius, MapRadius));
            }

            try
            {
                // 비동기로 서버에 던지고 응답이 올 때까지 메인 스레드를 양보함
                await trainingPolicy.UpdateTrainingAsync(obsArray, actionArray);

                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < agentCount; i++)
                        sb.AppendLine($"actionArray[{i}] : {actionArray[i].dx} , {actionArray[i].dy}");

                    print(sb.ToString());
                }

                // 응답이 도착한 후에만 유닛들을 이동시킴 (인과관계 유지)
                for (int i = 0; i < agentCount; i++)
                {
                    Phase3Action action = actionArray[i];
                    Vector3 movement = new Vector3(action.dx, 0f, action.dy) * moveSpeed * Time.deltaTime;
                    agents[i].transform.Translate(movement, Space.World);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"통신 중 에러 발생했어: {e.Message}");
            }
            finally
            {
                // 에러가 나든 성공하든 대기 플래그를 풀어서 다음 턴 진행
                isWaiting = false;
            }
        }

        void OnDestroy()
        {
            if (obsArray.IsCreated) obsArray.Dispose();
            if (actionArray.IsCreated) actionArray.Dispose();
            trainingPolicy?.Dispose();
        }

        bool CalculateReward(GameObject agent, Transform Target, float3 mapCenter, out float reward)
        {
            float3 agentPos = agent.transform.position;
            float3 targetPos = Target.position;
            
            // 1. 진짜 맵 중심 기준으로 원형 탈출 체크
            if (math.any(math.abs(agentPos - mapCenter) > MapRadius))//(math.distance(agentPos, mapCenter) > MapRadius)
            {
                reward = -5f;
                return true; // 에피소드 종료
            }

            float dis = math.distance(targetPos, agentPos);

            // 2. 타겟 도달 체크
            if (dis < 1f)
            {
                reward = 50f;
                return true; // 에피소드 종료
            }

            // 3. 거리 기반 보상 설정
            // 기본적으로 가까울수록 높은 점수를 주되, 타임 페널티(-0.1f)를 주어 빨리 움직이게 유도해
            reward = (1f - (dis / (MapRadius * 2f))) * 5f - 0.1f;

            return false;
        }
    }
}