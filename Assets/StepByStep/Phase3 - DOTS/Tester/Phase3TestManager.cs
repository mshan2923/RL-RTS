namespace RL_StepByStep
{
    using UnityEngine;
    using Unity.Collections;
    using System.Text;
    using Unity.Mathematics;
    using Unity.Entities;
    using Unity.Transforms;
    using Unity.Entities.UniversalDelegates;
    using System.Threading.Tasks;

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

        private bool isWaiting = false;

        EntityManager em;
        public static readonly float FixedStepSize = 0.02f;
        private const float ReachDistance = 0.75f;

        RLConfig rLConfig;

        float RadiusThreshold = 0.1f;

        void Start()
        {
            trainingPolicy = new PythonTrainingPolicy<Phase3Observation, Phase3Action>(serverIp, serverPort);
            agents = new GameObject[agentCount];
            obsArray = new NativeArray<Phase3Observation>(agentCount, Allocator.Persistent);
            actionArray = new NativeArray<Phase3Action>(agentCount, Allocator.Persistent);

            for (int i = 0; i < agentCount; i++)
            {
                Vector3 randomPos = new Vector3(UnityEngine.Random.Range(0, MapRadius * 2), 0.5f, UnityEngine.Random.Range(0, MapRadius * 2));
                agents[i] = Instantiate(agentPrefab, randomPos, Quaternion.identity, transform);
            }

            em = World.DefaultGameObjectInjectionWorld.EntityManager;


            //        if (!SystemAPI.TryGetSingleton<RLConfig>(out var config)) return;

            rLConfig = em.CreateEntityQuery(typeof(RLConfig)).GetSingleton<RLConfig>();

            RadiusThreshold = 1f / rLConfig.DetectionRange; // CellRadius : 1


        }



        async void Update()
        {
            if (targetTransform == null) return;
            if (isWaiting) return;

            isWaiting = true;

            for (int i = 0; i < agentCount; i++)
            {
                Vector3 agentPos = agents[i].transform.position;
                Vector3 targetPos = targetTransform.position;
                Vector3 directionToTarget = (targetPos - agentPos) / MapRadius;

                // 1. ECS 영역에서 벽 감지 데이터 먼저 확보하기
                var entity = Phase3Connecter.Instace.Units[i].Item2;
                var data = em.GetComponentData<DetectWallNormalize>(entity);

                // 2. 확보한 벽 데이터를 보상 함수에 함께 넘겨주기
                var isDone = CalculateReward(agents[i], targetTransform, float3.zero, data, out var reward);

                obsArray[i] = new Phase3Observation
                {
                    unitId = i,
                    dx = directionToTarget.x,
                    dy = directionToTarget.z,
                    d0 = data.n0,
                    d1 = data.n1,
                    d2 = data.n2,
                    d3 = data.n3,
                    d4 = data.n4,
                    d5 = data.n5,
                    reward = reward,
                    done = isDone ? 1 : 0
                };
                
                if (isDone)
                    agents[i].transform.position = new Vector3(UnityEngine.Random.Range(0, MapRadius * 2), 0.5f, UnityEngine.Random.Range(0, MapRadius * 2));
            }

            try
            {
                await trainingPolicy.UpdateTrainingAsync(obsArray, actionArray);

                // {
                //     var sb = new StringBuilder();
                //     for (int i = 0; i < agentCount; i++)
                //         sb.AppendLine($"actionArray[{i}] : {actionArray[i].dx} , {actionArray[i].dy}");

                //     print(sb.ToString());
                // }

                for (int i = 0; i < agentCount; i++)
                {
                    Phase3Action action = actionArray[i];
                    Vector3 movement = new Vector3(action.dx, 0f, action.dy) * moveSpeed * FixedStepSize;
                    agents[i].transform.Translate(movement, Space.World);


                     var entity = Phase3Connecter.Instace.Units[i].Item2;

                    em.SetComponentData(entity, new LocalTransform
                    {
                        Position = agents[i].transform.position,
                        Scale = 0.25f
                    });
                    em.SetComponentData(entity , new MoveTargetComponent
                    {
                        MoveTo = agents[i].transform.position
                    });

                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"통신 중 에러 발생했어: {e.Message}");
            }
            finally
            {
                isWaiting = false;
            }
        }

        void OnDestroy()
        {
            if (obsArray.IsCreated) obsArray.Dispose();
            if (actionArray.IsCreated) actionArray.Dispose();
            trainingPolicy?.Dispose();
        }

        // 매개변수에 DetectWallNormalize를 추가해 계산에 활용해
        bool CalculateReward(GameObject agent, Transform Target, float3 mapCenter, DetectWallNormalize wallData, out float reward)
        {
            float3 agentPos = agent.transform.position;
            float3 targetPos = Target.position;
            
            // 1. 맵 외곽 탈출 체크
            if (math.any(agentPos < 0) || math.any(agentPos > 2 * MapRadius))
            {
                reward = -5f;
                return true; 
            }

            // 2. 벽 감지 및 패널티 부여 (6개 센서 중 가장 가까운 벽 거리 탐색)
            float minWallDist = math.min(wallData.n0, 
                                math.min(wallData.n1, 
                                math.min(wallData.n2, 
                                math.min(wallData.n3, 
                                math.min(wallData.n4, wallData.n5)))));

            

            // 벽에 완전히 들이받은 상황 (충돌선 감지수치 0.05 미만일 때)
            // if (minWallDist < RadiusThreshold)
            // {
            //     reward = -15f; // 강한 충돌 페널티
            //     return true;  // 갇혀서 페널티만 무한히 쌓이는 걸 막기 위해 즉시 리셋
            // }

            float wallPenalty = 0f;
            float warningThreshold = RadiusThreshold * 3f; // 벽 접근 경고 시작선 (0.3 이하로 좁혀지면 작동)

            if (minWallDist < warningThreshold)
            {
                // 벽에 서서히 다가갈수록 페널티가 제곱으로 커지도록 설계
                float ratio = (warningThreshold - minWallDist) / warningThreshold;
                wallPenalty = -ratio * 5f;
            }

            float dis = math.distance(targetPos, agentPos);
            // 3. 타겟 도달 체크
            if (dis <  ReachDistance)
            {
                Debug.Log("goal");
                reward = 50f;
                return true; 
            }

            // 4. 거리 기반 보상에 벽 위험 패널티 합산
            float baseReward = (1f - (dis / (MapRadius * 2f))) * 5f - 0.1f;
            reward = baseReward + wallPenalty;

            return false;
        }
    }
}