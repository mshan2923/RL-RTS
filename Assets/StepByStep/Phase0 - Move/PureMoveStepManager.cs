using System;

namespace RL_StepByStep
{
        public class PureMoveStepManager : GameStepManagerBase<PureMoveUnit, PureMoveObservation, PureMoveAction>
    {
        public static PureMoveStepManager Instance { get; private set; }

        protected override void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
            base.Awake();
        }

        protected override Func<PureMoveObservation, float[]> ObsToInput => PureMoveConverters.ObsToInput;
        protected override Func<float[], PureMoveAction> OutputToAction => PureMoveConverters.OutputToAction;
    }
}
