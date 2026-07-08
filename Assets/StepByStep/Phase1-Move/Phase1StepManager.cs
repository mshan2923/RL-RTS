using System;

namespace RL_StepByStep
{
    /// <summary>Phase1 구체화. 씬에는 이 클래스를 붙인다. Phase2가 생기면 Phase2StepManager만 새로 만들면 된다.</summary>
    public class Phase1StepManager : GameStepManagerBase<Unit, Phase1Observation, Phase1Action>
    {
        public static Phase1StepManager Instance { get; private set; }

        protected override void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
            base.Awake();
        }

        protected override Func<Phase1Observation, float[]> ObsToInput => Phase1Converters.ObsToInput;
        protected override Func<float[], Phase1Action> OutputToAction => Phase1Converters.OutputToAction;
    }
}