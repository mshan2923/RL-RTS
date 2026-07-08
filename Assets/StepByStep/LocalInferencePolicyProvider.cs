using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>
    /// 추론 모드. Python 학습이 끝난 ONNX 모델을 Unity Inference Engine으로 로컬에서 실행한다.
    /// 네트워크 왕복이 없어 지연이 거의 없고, 빌드에도 그대로 포함 가능.
    ///
    /// TObs -> float[] 변환과 float[] -> TAction 변환은 프로젝트마다 다르므로
    /// 델리게이트로 주입받는다 (Phase가 바뀌어도 이 클래스는 수정하지 않음).
    /// </summary>
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

        /// <param name="modelAsset">StreamingAssets 등에서 로드한 ModelAsset</param>
        /// <param name="backend">CPU 또는 GPUCompute</param>
        /// <param name="obsToInput">관측 구조체 -> 신경망 입력 float 배열 변환</param>
        /// <param name="outputToAction">신경망 출력 float 배열 -> 액션 구조체 변환</param>
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
            if (pendingIds.Count == 0) return;

            int count = pendingIds.Count;
            int inputDim = pendingInputs[0].Length;

            // 배치 입력 텐서 구성 (count, inputDim)
            float[] flat = new float[count * inputDim];
            for (int i = 0; i < count; i++)
            {
                Array.Copy(pendingInputs[i], 0, flat, i * inputDim, inputDim);
            }

            using var inputTensor = new Tensor<float>(new TensorShape(count, inputDim), flat);

            worker.Schedule(inputTensor);
            using var outputTensor = worker.PeekOutput() as Tensor<float>;

            // GPU 백엔드일 경우 비동기 readback 필요
            using var cpuTensor = await outputTensor.ReadbackAndCloneAsync() as Tensor<float>;
            float[] outputFlat = cpuTensor.DownloadToArray();

            int outputDim = outputFlat.Length / count;
            for (int i = 0; i < count; i++)
            {
                float[] singleOutput = new float[outputDim];
                Array.Copy(outputFlat, i * outputDim, singleOutput, 0, outputDim);

                TAction action = outputToAction(singleOutput);
                OnAction?.Invoke(pendingIds[i], action);
            }

            pendingIds.Clear();
            pendingInputs.Clear();
        }

        public void Dispose()
        {
            worker?.Dispose();
        }
    }
}
