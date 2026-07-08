using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

namespace RL_StepByStep
{
    public enum RunMode
    {
        Training,   // Python 서버로 관측 전송, 서버가 액션 계산 (학습 중)
        Inference,  // Unity Inference Engine으로 로컬 추론 (학습 완료 후 배포/테스트)
    }

    /// <summary>
    /// 전역 스텝 타이머 + 모드(Training/Inference)에 따라 알맞은 IPolicyProvider를
    /// 생성해서 모든 유닛에 주입한다.
    ///
    /// TAgent/TObs/TAction 전부 제네릭이라, Phase가 바뀌어도(Phase0Unit, Phase2Unit 등)
    /// 이 클래스 자체는 절대 수정하지 않는다. Phase별로 구체 타입을 지정한
    /// 얇은 서브클래스(Phase1StepManager 같은)만 새로 만들면 된다 - CommsManager를
    /// Phase1CommsManager로 감쌌던 것과 완전히 같은 패턴.
    /// </summary>
    public abstract class GameStepManagerBase<TAgent, TObs, TAction> : MonoBehaviour
        where TAgent : IStepAgent<TObs, TAction>
        where TObs : struct
        where TAction : struct
    {
        [SerializeField] protected RunMode runMode = RunMode.Training;
        [SerializeField] protected float stepInterval = 0.2f;

        [Header("Training Mode")]
        [Tooltip("CommsManager<TObs,TAction>를 구현한 컴포넌트를 드래그. "
                 + "Connect()는 ICommsManager로 호출하므로 타입 파라미터에 영향받지 않는다.")]
        [SerializeField] protected MonoBehaviour commsManagerBehaviour;

        [Header("Inference Mode")]
        [SerializeField] protected ModelAsset onnxModel;
        [SerializeField] protected BackendType inferenceBackend = BackendType.CPU;

        private float stepTimer;
        private readonly List<TAgent> activeUnits = new List<TAgent>();
        private IPolicyProvider<TObs, TAction> policyProvider;

        /// <summary>
        /// Inference 모드에서 관측/액션 <-> float[] 변환 함수. Phase별 서브클래스가 구현.
        /// (Phase1Converters.ObsToInput/OutputToAction 같은 역할)
        /// </summary>
        protected abstract System.Func<TObs, float[]> ObsToInput { get; }
        protected abstract System.Func<float[], TAction> OutputToAction { get; }

        protected virtual void Awake()
        {
            try
            {
                policyProvider = CreateProvider();
                if (policyProvider == null)
                    Debug.LogError($"[{GetType().Name}] CreateProvider가 null을 반환했습니다.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[{GetType().Name}] CreateProvider 중 예외 발생: {e}");
            }
        }

        private IPolicyProvider<TObs, TAction> CreateProvider()
        {
            switch (runMode)
            {
                case RunMode.Training:
                    if (commsManagerBehaviour == null)
                    {
                        Debug.LogError($"[{GetType().Name}] commsManagerBehaviour가 인스펙터에 연결되어 있지 않습니다.");
                        return null;
                    }

                    if (commsManagerBehaviour is ICommsManager icomms)
                    {
                        icomms.Connect();
                        Debug.Log("Connected");
                    }
                    else
                    {
                        Debug.LogError($"[{GetType().Name}] commsManagerBehaviour가 ICommsManager를 구현하지 않습니다.");
                        return null;
                    }

                    if (!(commsManagerBehaviour is CommsManager<TObs, TAction> commsManager))
                    {
                        Debug.LogError($"[{GetType().Name}] commsManagerBehaviour가 CommsManager<{typeof(TObs).Name},{typeof(TAction).Name}>가 아닙니다.");
                        return null;
                    }
                    return new RemotePolicyProvider<TObs, TAction>(commsManager);

                case RunMode.Inference:
                    if (onnxModel == null)
                    {
                        Debug.LogError($"[{GetType().Name}] onnxModel이 인스펙터에 연결되어 있지 않습니다.");
                        return null;
                    }
                    return new LocalInferencePolicyProvider<TObs, TAction>(
                        onnxModel,
                        inferenceBackend,
                        ObsToInput,
                        OutputToAction
                    );

                default:
                    Debug.LogError($"[{GetType().Name}] 알 수 없는 RunMode");
                    return null;
            }
        }

        public void Register(TAgent unit)
        {
            if (!activeUnits.Contains(unit))
                activeUnits.Add(unit);

            if (policyProvider != null)
                unit.SetPolicyProvider(policyProvider);
            else
                Debug.Log("policyProvider is Null");
        }

        public void Unregister(TAgent unit)
        {
            activeUnits.Remove(unit);
        }

        protected virtual void Update()
        {
            stepTimer += Time.deltaTime;
            if (stepTimer >= stepInterval)
            {
                stepTimer = 0f;
                RunStep();
            }
        }

        private void RunStep()
        {
            Debug.Log($"Run : {activeUnits.Count}");

            foreach (var unit in activeUnits)
            {
                unit.CollectObservation();
            }

            if (policyProvider != null)
                policyProvider?.Flush();
            else
                Debug.Log("policyProvider is Null");
        }

        protected virtual void OnDestroy()
        {
            if (policyProvider is System.IDisposable disposable)
                disposable.Dispose();
        }
    }
}