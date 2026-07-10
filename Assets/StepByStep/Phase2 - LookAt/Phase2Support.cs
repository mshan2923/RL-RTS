
namespace RL_StepByStep
{
    public class Phase2CommsManager : CommsManager<Phase2Observation, Phase2Action>
    {
    }

    public static class Phase2Converters
    {
        public static float[] ObsToInput(Phase2Observation obs) => new float[] { obs.dx, obs.dy };

        public static Phase2Action OutputToAction(float[] output)
        {
            return new Phase2Action
            {
                dx = output[0],
                dy = output[1]
            };
        }
    }


}