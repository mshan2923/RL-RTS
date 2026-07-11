namespace RL_StepByStep
{
    // 실제로 씬에 붙는 구체 타입. Phase가 바뀌면 이런 래퍼를 하나 더 만들면 됨.
    public class Phase1CommsManager : CommsManager<Phase1Observation, Phase1Action>
    {
    }
}