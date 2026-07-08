namespace RL_StepByStep
{
    /// <summary>
    /// 액션을 어디서 받아오는가를 추상화. Unit은 이 인터페이스만 알고,
    /// 학습 모드(RemotePolicyProvider, Python 서버)와 추론 모드(LocalInferencePolicyProvider,
    /// Unity Inference Engine)를 몰라도 된다.
    ///
    /// 모드 전환 시 GameStepManager나 부트스트랩에서 구현체만 바꿔 끼우면 됨.
    /// </summary>
    public interface IPolicyProvider<TObs, TAction>
        where TObs : struct
        where TAction : struct
    {
        /// <summary>이번 스텝 관측을 등록. 실제 처리는 Flush에서 일괄 수행.</summary>
        void Submit(int unitId, TObs observation);

        /// <summary>등록된 관측들을 처리하여 액션을 계산하고 OnAction으로 통지.</summary>
        void Flush();

        /// <summary>액션 계산 완료 시 발생 (unitId, action).</summary>
        event System.Action<int, TAction> OnAction;
    }
}
