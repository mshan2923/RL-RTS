using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>
    /// RL 페이즈 추상화. 구조(관측 필드, 액션 스페이스)가 바뀌면
    /// 이 클래스를 상속한 구체 페이즈 클래스(예: Phase1_MoveToTarget)만 새로 작성한다.
    ///
    /// TAgent도 제네릭으로 뺐다 — 이전엔 Encode/Apply가 구체 클래스 Unit을 하드코딩해서,
    /// Phase0Unit처럼 다른 유닛 클래스를 쓰는 Phase가 아예 이 베이스를 못 썼다.
    /// Phase마다 유닛 클래스 자체가 다를 수 있으므로(필요한 필드가 다름) TAgent로 열어둔다.
    /// </summary>
    public abstract class RLPhaseBase<TAgent, TObs, TAction>
        where TObs : struct
        where TAction : struct
    {
        /// <summary>유닛 상태로부터 관측(TObs)을 계산한다. 여기가 매 페이즈마다 다시 짜는 핵심 부분.</summary>
        public abstract TObs Encode(TAgent self);

        /// <summary>수신된 액션(TAction)을 유닛에 적용한다.</summary>
        public abstract void Apply(TAgent self, TAction action);

        /// <summary>이 페이즈에서 사용할 CommsManager 인스턴스.</summary>
        public CommsManager<TObs, TAction> Comms => CommsManager<TObs, TAction>.Instance;
    }
}