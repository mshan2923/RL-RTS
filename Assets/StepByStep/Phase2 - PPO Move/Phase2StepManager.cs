using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace RL_StepByStep
{
        public class Phase2StepManager : GameStepManagerBase<Phase2Unit, Phase2Observation, Phase2Action>
    {

        public static Phase2StepManager Instance { get; private set; }
        public float Speed;

        public override void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
            base.Awake();
        }

        protected override Func<Phase2Observation, float[]> ObsToInput => Phase2Converters.ObsToInput;

        //LocalInferencePolicyProvider 에서 InferencEngine 동작 , 
        protected override Func<float[], Phase2Action> OutputToAction => Phase2Converters.OutputToAction;
    }
}
