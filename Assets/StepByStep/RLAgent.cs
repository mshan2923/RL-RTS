// using Unity.InferenceEngine;
// using UnityEngine;

// namespace RL_StepByStep
// {
//     public class RLAgent<Unit, Observation, Action> : MonoBehaviour
//         where Unit : class, IStepAgent<Observation, Action>
//         where Observation : struct
//         where Action : struct
//     {
//         public GameStepManagerBase<Unit, Observation, Action> stepManager;
//         public CommsManager<Observation, Action> commsManager;


//         [SerializeField] protected RunMode runMode = RunMode.Training;
//         [SerializeField] protected float stepInterval = 0.2f;

//         [Header("Training Mode")]
//         [Tooltip("CommsManager<TObs,TAction>를 구현한 컴포넌트를 드래그. "
//                  + "Connect()는 ICommsManager로 호출하므로 타입 파라미터에 영향받지 않는다.")]
//         [SerializeField] protected MonoBehaviour commsManagerBehaviour;

//         [Header("Inference Mode")]
//         [SerializeField] protected ModelAsset onnxModel;
//         [SerializeField] protected BackendType inferenceBackend = BackendType.GPUCompute;


//         [SerializeField] private string host = "127.0.0.1";
//         [SerializeField] private int port = 5555;

//         protected virtual void Awake()
//         {
//             stepManager.setup(runMode, stepInterval, commsManagerBehaviour, onnxModel, inferenceBackend);
//             commsManager.setup(host, port);

//             stepManager.Awake();
//             commsManager.Awake();
//         }
//         protected virtual void Start()
//         {
            
//         }
//     }
// }