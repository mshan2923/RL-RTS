using System;

namespace RL_StepByStep
{
    /// <summary>
    /// 학습 모드. 관측을 CommsManager(소켓)를 통해 Python 서버로 보내고,
    /// 서버(정책 네트워크 학습/추론)가 계산한 액션을 받아온다.
    /// </summary>
    public class RemotePolicyProvider<TObs, TAction> : IPolicyProvider<TObs, TAction>
        where TObs : struct
        where TAction : struct
    {
        public event Action<int, TAction> OnAction;

        private readonly CommsManager<TObs, TAction> comms;

        public RemotePolicyProvider(CommsManager<TObs, TAction> comms)
        {
            this.comms = comms;
            this.comms.OnActionReceived += HandleActionReceived;
        }

        public void Submit(int unitId, TObs observation)
        {
            // unitId는 TObs 구조체 내부에 이미 포함되어 있다고 가정 (Phase1Observation.unitId 등)
            comms.Enqueue(observation);
        }

        public void Flush()
        {
            comms.Flush();
        }

        private void HandleActionReceived(int unitId, TAction action)
        {
            OnAction?.Invoke(unitId, action);
        }

        public void Dispose()
        {
            comms.OnActionReceived -= HandleActionReceived;
        }
    }
}
