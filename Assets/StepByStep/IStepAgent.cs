namespace RL_StepByStep
{
    /// <summary>
    /// GameStepManagerBase가 유닛에게 요구하는 최소 계약.
    /// Unit, Phase0Unit 등 Phase마다 다른 유닛 클래스가 이것만 구현하면
    /// GameStepManagerBase&lt;TAgent,TObs,TAction&gt;에 그대로 꽂힌다.
    /// </summary>
    public interface IStepAgent<TObs, TAction>
        where TObs : struct
        where TAction : struct
    {
        void SetPolicyProvider(IPolicyProvider<TObs, TAction> provider);
        void CollectObservation();
    }
}