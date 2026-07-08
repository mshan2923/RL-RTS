using System;

namespace RL_StepByStep
{
    /// <summary>
    /// CommsManager&lt;TObs,TAction&gt;에서 타입 파라미터와 무관한 부분(연결, 상태)만 뽑은 인터페이스.
    /// GameStepManager 같은 상위 매니저는 TObs/TAction을 몰라도 되므로, 이 인터페이스만 참조하면
    /// Phase마다 다른 CommsManager&lt;...&gt; 구체 타입(Phase1CommsManager, Phase0CommsManager 등)에
    /// 코드 수정 없이 대응할 수 있다.
    /// </summary>
    public interface ICommsManager
    {
        bool IsConnected { get; }
        void Connect(string host = null, int port = -1);

        /// <summary>연결 상태가 바뀔 때 발생.</summary>
        event Action<bool> OnConnectionChanged;
    }
}