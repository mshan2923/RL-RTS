using System.Threading.Tasks;
using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>facing 없음. axialPosition만 있고, 실제 로직은 PureMove_ToTarget이 담당.</summary>
    public class PureMoveUnit : MonoBehaviour, IStepAgent<PureMoveObservation, PureMoveAction>
    {
        [SerializeField] private int _unitId;
        public int unitId => _unitId;

        public Vector2Int axialPosition;
        [SerializeField] private Transform _targetTransform;
        public Transform targetTransform => _targetTransform;

        [SerializeField] private int _mapRadius = 5;
        public int mapRadius => _mapRadius;

        // PureMove_ToTarget이 거리 변화량 보상 계산에 쓰는 상태. Unit.cs의 prevDistanceRaw와 동일 역할.
        public float prevDist;
        public bool hasPrevDist;

        private PureMove_ToTarget rlPhase;
        private IPolicyProvider<PureMoveObservation, PureMoveAction> policyProvider;
        private bool lastObservationWasDone;
        private bool hasStarted;

        void Awake()
        {
            if (_unitId == 0) _unitId = GetInstanceID();
            rlPhase = new PureMove_ToTarget();
        }

        async void Start()
        {
            hasStarted = true;
            RandomizePosition();
            PureMoveStepManager.Instance?.Register(this);

            if (PureMoveStepManager.Instance == null)
            {
                Debug.Log("PureMoveStepManager.Instance is null");

                await LazeStart();

                if (PureMoveStepManager.Instance == null)
                    Debug.LogWarning("wtf");

                RandomizePosition();
                PureMoveStepManager.Instance?.Register(this);
                Debug.Log("Laze");
            }

        }

        Awaitable LazeStart()
        {

            return Awaitable.WaitForSecondsAsync(0.5f);
        }

        void OnEnable()
        {
            if (hasStarted) PureMoveStepManager.Instance?.Register(this);
        }

        void OnDisable()
        {
            PureMoveStepManager.Instance?.Unregister(this);
            if (policyProvider != null) policyProvider.OnAction -= HandleActionReceived;
        }

        public void SetPolicyProvider(IPolicyProvider<PureMoveObservation, PureMoveAction> provider)
        {
            if (policyProvider != null) policyProvider.OnAction -= HandleActionReceived;
            policyProvider = provider;
            policyProvider.OnAction += HandleActionReceived;
        }

        private void RandomizePosition()
        {
            axialPosition = new Vector2Int(Random.Range(-_mapRadius, _mapRadius + 1), Random.Range(-_mapRadius, _mapRadius + 1));
            hasPrevDist = false;
            SyncTransform();
        }

        public void CollectObservation()
        {
            var data = rlPhase.Encode(this);

            if (policyProvider == null)
                Debug.Log("policyProvider is Null");
            else
                Debug.Log(data.reward);
                
            policyProvider?.Submit(unitId, data);

            lastObservationWasDone = data.done == 1;
            if (lastObservationWasDone)
                RandomizePosition();
        }

        private void HandleActionReceived(int receivedUnitId, PureMoveAction action)
        {
            if (receivedUnitId != unitId) return;
            if (lastObservationWasDone) { lastObservationWasDone = false; return; }

            rlPhase.Apply(this, action);
        }

        public void SyncTransform()
        {
            Vector2 world = HexUtil.AxialToWorld(axialPosition);
            transform.position = new Vector3(world.x, transform.position.y, world.y);
        }
    }
}