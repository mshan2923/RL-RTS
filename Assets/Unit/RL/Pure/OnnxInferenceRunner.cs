using Unity.Collections;
using UnityEngine;
using Unity.InferenceEngine;
using System.Threading.Tasks;
using System;

/// <summary>
/// 
/// </summary>
/// <typeparam name="action">ActionType</typeparam>
public class OnnxInferenceRunner<action> where action : Enum
{
    Model runtimeModel;
    Worker worker;

    int actionAmount = Enum.GetNames(typeof(action)).Length;

    public OnnxInferenceRunner(ModelAsset modelAsset)
    {
        runtimeModel = ModelLoader.Load(modelAsset);
        worker = new Worker(runtimeModel, BackendType.GPUCompute);
    }

    public void Infer(NativeArray<CObservation> obsArray, NativeArray<CActionData> actionArray)
    {
        int count = obsArray.Length;
        int obsDim = RLConstants.OBS_DIM; // dx, dy, selfHp, targetHp, InAttackRange, distToEdge

        // obs를 입력 텐서로 변환
        using var inputTensor = new Tensor<float>(new TensorShape(count, obsDim));
        for (int i = 0; i < count; i++)
        {
            var o = obsArray[i];
            inputTensor[i, 0] = o.dx;
            inputTensor[i, 1] = o.dy;
            inputTensor[i, 2] = o.delta;
            inputTensor[i, 3] = o.selfHp;
            inputTensor[i, 4] = o.targetHp;
            inputTensor[i, 5] = o.InAttackRange;
            inputTensor[i, 6] = o.distToEdge;
        }

        worker.Schedule(inputTensor);
        var outputTensor = worker.PeekOutput() as Tensor<float>;
        outputTensor.CompleteAllPendingOperations(); // 동기 대기

        // logits(action_dim=5)에서 argmax
        for (int i = 0; i < count; i++)
        {
            int bestAction = 0;
            float bestVal = float.MinValue;
            for (int a = 0; a < actionAmount; a++)
            {
                float v = outputTensor[i, a];
                if (v > bestVal) { bestVal = v; bestAction = a; }
            }

            actionArray[i] = new CActionData { action_index = bestAction };
        }
    }

    public async Task InferAsync(NativeArray<CObservation> obsArray, NativeArray<CActionData> actionArray)
    {
        if (!actionArray.IsCreated) return;

        int count = obsArray.Length;
        int obsDim = RLConstants.OBS_DIM;
        
        // obs를 입력 텐서로 변환
        using var inputTensor = new Tensor<float>(new TensorShape(count, obsDim));
        for (int i = 0; i < count; i++)
        {
            var o = obsArray[i];
            inputTensor[i, 0] = o.dx;
            inputTensor[i, 1] = o.dy;
            inputTensor[i, 2] = o.delta;
            inputTensor[i, 3] = o.selfHp;
            inputTensor[i, 4] = o.targetHp;
            inputTensor[i, 5] = o.InAttackRange;
            inputTensor[i, 6] = o.distToEdge;
        }

        worker.Schedule(inputTensor);
        var outputTensor = worker.PeekOutput() as Tensor<float>;
        if (outputTensor == null)
        {
            Debug.LogError("[InferenceRunner] output tensor cast 실패 - 모델 output 타입 확인 필요");
            return;
        }

        using var result = await outputTensor.ReadbackAndCloneAsync();

        // logits(action_dim=5)에서 argmax
        for (int i = 0; i < count; i++)
        {
            int bestAction = 0;
            float bestVal = float.MinValue;
            for (int a = 0; a < actionAmount; a++)
            {
                float v = result[i, a];
                if (v > bestVal) { bestVal = v; bestAction = a; }
            }

            if (actionArray.IsCreated)
                actionArray[i] = new CActionData { action_index = bestAction };
        }
    }

    public void Dispose()
    {
        worker?.Dispose();
    }
}