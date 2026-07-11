using System.Collections;
using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>facing 없음. axialPosition만 있고, 실제 로직은 PureMove_ToTarget이 담당.</summary>
    public class Phase2Unit : MonoBehaviour, IStepAgent<Phase2Observation, Phase2Action>
    {
        private static WaitForSeconds _waitForSeconds0_1 = new WaitForSeconds(0.1f);
        [SerializeField] private int _unitId;
        public int unitId => _unitId;

        public Vector3 direction;
        
        [SerializeField] private Transform _targetTransform;
        public Transform targetTransform => _targetTransform;

        [SerializeField] private int _mapRadius = 5;
        public int mapRadius => _mapRadius;

        // PureMove_ToTarget이 거리 변화량 보상 계산에 쓰는 상태. Unit.cs의 prevDistanceRaw와 동일 역할.
        public float prevDist;
        public Vector3 PrevPos;

        public bool hasPrevDist;

        private Phaase2_ToTarget rlPhase;
        private IPolicyProvider<Phase2Observation, Phase2Action> policyProvider;
        private bool lastObservationWasDone;
        private bool hasStarted;

        public bool isResetTargetPos;
        public float Speed = 1f;

        void Awake()
        {
            if (_unitId == 0) _unitId = GetInstanceID();
            rlPhase = new Phaase2_ToTarget();
        }

        async void Start()
        {
            hasStarted = true;
            RandomizePosition();
            Phase2StepManager.Instance?.Register(this);

            if (Phase2StepManager.Instance == null)
            {
                Debug.Log("Phase2StepManager.Instance is null");

                StartCoroutine (LazeStart());

                if (Phase2StepManager.Instance == null)
                    Debug.LogWarning("wtf");
            }

        }


        IEnumerator LazeStart()
        {
            float time = 0;
            while (Phase2StepManager.Instance == null && time < 10)
            {

                yield return _waitForSeconds0_1;
                time += 0.1f;

            }
            if (time < 10)
            {
                RandomizePosition();
                Phase2StepManager.Instance?.Register(this);
                Debug.Log("Laze Start");
            }
        }

        void OnEnable()
        {
            if (hasStarted) Phase2StepManager.Instance?.Register(this);
        }

        void OnDisable()
        {
            Phase2StepManager.Instance?.Unregister(this);
            if (policyProvider != null) policyProvider.OnAction -= HandleActionReceived;
        }

        public void SetPolicyProvider(IPolicyProvider<Phase2Observation, Phase2Action> provider)
        {
            if (policyProvider != null) policyProvider.OnAction -= HandleActionReceived;
            policyProvider = provider;
            policyProvider.OnAction += HandleActionReceived;
        }

        private void RandomizePosition()
        {
            transform.position = new Vector3(Random.Range(-_mapRadius, _mapRadius + 1), 0, Random.Range(-_mapRadius, _mapRadius + 1));

            if (isResetTargetPos)
            {
                var pos = HexUtil.AxialToWorld(new Vector2Int(Random.Range(-_mapRadius, _mapRadius + 1), Random.Range(-_mapRadius, _mapRadius + 1)));
            
                targetTransform.position = new Vector3(pos.x, transform.position.y, pos.y);   
            }
            
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

        private void HandleActionReceived(int receivedUnitId, Phase2Action action)
        {
            if (receivedUnitId != unitId) return;
            if (lastObservationWasDone) { lastObservationWasDone = false; return; }

            rlPhase.Apply(this, action);
        }

        public void SyncTransform()
        {
            // Vector2 world = HexUtil.AxialToWorld(axialPosition);
            transform.position += Mathf.Max(Phase2StepManager.Instance.Interval, Time.deltaTime) * Speed * direction;
        }
    }
}