using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

namespace RL_StepByStep
{
    public class LocalInferencePolicyProvider<TObs, TAction> : IPolicyProvider<TObs, TAction>, IDisposable
        where TObs : struct
        where TAction : struct
    {
        public event Action<int, TAction> OnAction;

        private readonly Model model;
        private readonly Worker worker;
        private readonly Func<TObs, float[]> obsToInput;
        private readonly Func<float[], TAction> outputToAction;

        private readonly List<int> pendingIds = new List<int>();
        private readonly List<float[]> pendingInputs = new List<float[]>();
        
        // 중복 플러시 방지용 락 플래그
        private bool isFlushing = false;

        public LocalInferencePolicyProvider(
            ModelAsset modelAsset,
            BackendType backend,
            Func<TObs, float[]> obsToInput,
            Func<float[], TAction> outputToAction)
        {
            model = ModelLoader.Load(modelAsset);
            worker = new Worker(model, backend);
            this.obsToInput = obsToInput;
            this.outputToAction = outputToAction;
        }

        public void Submit(int unitId, TObs observation)
        {
            pendingIds.Add(unitId);
            pendingInputs.Add(obsToInput(observation));
        }

        public async void Flush()
        {
            // 아직 이전 플러시가 비동기 대기 중이거나 데이터가 없으면 패스
            if (pendingIds.Count == 0 || isFlushing) return;

            isFlushing = true;

            int count = pendingIds.Count;
            int inputDim = pendingInputs[0].Length;

            float[] flat = new float[count * inputDim];
            for (int i = 0; i < count; i++)
            {
                Array.Copy(pendingInputs[i], 0, flat, i * inputDim, inputDim);
            }

            // [수정 포인트 1] await 호출 전에 원본 리스트를 로컬로 복사하고 즉시 비운다.
            // 그래야 다음 프레임에서 Submit된 데이터와 섞이거나 중복 처리되지 않아.
            var currentIds = new List<int>(pendingIds);
            pendingIds.Clear();
            pendingInputs.Clear();

            try
            {
                using var inputTensor = new Tensor<float>(new TensorShape(count, inputDim), flat);
                worker.Schedule(inputTensor);

                // [수정 포인트 2] PeekOutput은 using을 쓰지 않는다. (소유권은 Worker에게 있음)
                var outputTensor = worker.PeekOutput() as Tensor<float>;
                if (outputTensor == null) return;

                // ReadbackAndCloneAsync가 뱉는 텐서는 새로 할당된 클론이므로 using이 필수야.
                using var cpuTensor = await outputTensor.ReadbackAndCloneAsync();
                float[] outputFlat = cpuTensor.DownloadToArray();

                int outputDim = outputFlat.Length / count;
                for (int i = 0; i < count; i++)
                {
                    float[] singleOutput = new float[outputDim];
                    Array.Copy(outputFlat, i * outputDim, singleOutput, 0, outputDim);

                    TAction action = outputToAction(singleOutput);
                    OnAction?.Invoke(currentIds[i], action);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"추론 중 에러 발생: {e.Message}");
            }
            finally
            {
                isFlushing = false;
            }
        }

        public void Dispose()
        {
            worker?.Dispose();
        }
    }
}