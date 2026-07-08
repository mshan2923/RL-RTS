using System;

namespace RL_StepByStep
{
    public class PureMoveCommsManager : CommsManager<PureMoveObservation, PureMoveAction>
    {
    }

    public static class PureMoveConverters
    {
        public static float[] ObsToInput(PureMoveObservation obs) => new float[] { obs.dx, obs.dy };

        public static PureMoveAction OutputToAction(float[] output)
        {
            int best = 0;
            float bestVal = output[0];
            for (int i = 1; i < output.Length; i++)
                if (output[i] > bestVal) { bestVal = output[i]; best = i; }
            return new PureMoveAction { direction = best };
        }
    }


}