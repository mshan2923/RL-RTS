using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>
    /// 유닛. IPolicyProvider만 참조하므로 학습 모드(RemotePolicyProvider)든
    /// 추론 모드(LocalInferencePolicyProvider)든 코드 변경 없이 동작한다.
    /// 모드는 GameStepManager/부트스트랩에서 어떤 Provider를 주입하느냐로 결정됨.
    /// </summary>
    public class Unit : MonoBehaviour, IStepAgent<Phase1Observation, Phase1Action>
    {
        [SerializeField] private int _unitId;
        public int unitId => _unitId;

        public Vector2Int axialPosition;
        public int facingDirection;
        public Vector2Int targetAxial;

        /// <summary>직전 스텝의 실제(정규화 전) 월드 거리. RewardCalculator가 거리 변화량 계산에 사용.</summary>
        public float prevDistanceRaw;
        public bool hasPrevDistance;

        [SerializeField] private GameManagerBase _gameManagerRef;
        public GameManagerBase gameManagerRef => _gameManagerRef;

        private Phase1_MoveToTarget rlPhase;
        private IPolicyProvider<Phase1Observation, Phase1Action> policyProvider;

        void Awake()
        {
            if (_unitId == 0)
                _unitId = GetInstanceID();

            rlPhase = new Phase1_MoveToTarget();
        }

        private bool hasStarted;

        void Start()
        {
            hasStarted = true;
            // 최초 스폰도 랜덤화해야 함. 모든 유닛이 같은 위치/방향에서 시작하면
            // 관측(dot/cross)이 서로 비슷해져서 정책이 "다 같은 방향"을 배우는 게
            // 오히려 합리적인 결과가 되어버린다 -> 겉으로는 "collapse"처럼 보이지만
            // 실제로는 다양한 상황 자체를 겪어본 적이 없는 것.
            gameManagerRef.RespawnUnit(this);
            SyncTransform();
            Phase1StepManager.Instance?.Register(this);
        }

        void OnEnable()
        {
            // Start()는 오브젝트 최초 활성화 시 한 번만 호출되므로,
            // 비활성화 -> 재활성화 케이스는 여기서 재등록한다.
            if (hasStarted)
                Phase1StepManager.Instance?.Register(this);
        }

        void OnDisable()
        {
            Phase1StepManager.Instance?.Unregister(this);
            if (policyProvider != null)
                policyProvider.OnAction -= HandleActionReceived;
        }

        /// <summary>GameStepManager가 모드에 맞는 Provider를 주입.</summary>
        public void SetPolicyProvider(IPolicyProvider<Phase1Observation, Phase1Action> provider)
        {
            if (policyProvider != null)
                policyProvider.OnAction -= HandleActionReceived;

            policyProvider = provider;
            policyProvider.OnAction += HandleActionReceived;
        }

        /// <summary>직전 CollectObservation이 done=1이었는지. 그 경우 되돌아온 액션은 적용하지 않는다
        /// (이미 리스폰되어 위치가 바뀐 상태에서 옛 관측 기준 액션을 적용하면 안 되므로).</summary>
        private bool lastObservationWasDone;

        public void CollectObservation()
        {
            targetAxial = gameManagerRef.GetTargetFor(this);
            var obs = rlPhase.Encode(this); // 이 안에서 reward/done 계산 + prevDistance 갱신까지 처리됨
            policyProvider?.Submit(unitId, obs);

            lastObservationWasDone = obs.done == 1;
            if (lastObservationWasDone)
            {
                // Unity가 이미 done을 알고 있으므로, Python 응답을 기다리지 않고 바로 리스폰한다.
                gameManagerRef.RespawnUnit(this);
                SyncTransform();
            }
        }

        private void HandleActionReceived(int receivedUnitId, Phase1Action action)
        {
            if (receivedUnitId != unitId) return;

            if (lastObservationWasDone)
            {
                // 이 액션은 리스폰 전(옛 상태) 관측에 대한 응답이라 적용하면 안 됨.
                lastObservationWasDone = false;
                return;
            }

            rlPhase.Apply(this, action);
        }

        public void SyncTransform()
        {
            Vector2 world = HexUtil.AxialToWorld(axialPosition);
            transform.position = new Vector3(world.x, transform.position.y, world.y);
            transform.rotation = Quaternion.Euler(0, facingDirection * 60f, 0);
        }
    }
}