using System.Collections;
using UnityEngine;

namespace RL_StepByStep
{
    public class Phase2UnitManager : MonoBehaviour, IStepAgent<Phase2Observation, Phase2Action>
    {
        private static Phase2UnitManager _instance;
        public static Phase2UnitManager Instace {get => _instance;}

        protected void Awake()
        {
            if (Instace == null) _instance = this;
        }


        public void CollectObservation()
        {

        }

        public void SetPolicyProvider(IPolicyProvider<Phase2Observation, Phase2Action> provider)
        {

        }
    }
}