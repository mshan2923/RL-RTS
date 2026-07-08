namespace RL_StepByStep
{
    /// <summary>
    /// Phase1Observation/Phase1Action <-> float[] 변환.
    /// LocalInferencePolicyProvider 생성 시 델리게이트로 주입한다.
    /// Phase가 바뀌면 이 클래스와 짝이 되는 Phase2Converters 등을 새로 만들면 됨.
    /// </summary>
    public static class Phase1Converters
    {
        // 신경망 입력 순서: distanceToTarget, dotToTarget, crossToTarget, h0~h5 (9개)
        // Python 학습 스크립트의 obs_dim/입력 순서와 반드시 일치해야 한다.
        public static float[] ObsToInput(Phase1Observation obs)
        {
            return new float[]
            {
                obs.distanceToTarget,
                obs.dotToTarget,
                obs.crossToTarget,
                obs.h0, obs.h1, obs.h2, obs.h3, obs.h4, obs.h5,
            };
        }

        // 신경망 출력: 6방향에 대한 확률/로짓 -> argmax로 방향 결정
        public static Phase1Action OutputToAction(float[] output)
        {
            int bestIndex = 0;
            float bestValue = output[0];
            for (int i = 1; i < output.Length; i++)
            {
                if (output[i] > bestValue)
                {
                    bestValue = output[i];
                    bestIndex = i;
                }
            }
            return new Phase1Action { direction = bestIndex };
        }
    }
}
