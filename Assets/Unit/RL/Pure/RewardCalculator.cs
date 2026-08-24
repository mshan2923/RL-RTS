using Unity.Mathematics;
using UnityEngine;

public static class RewardCalculator
{
    public static CObservation Apply(CObservation parm, bool isOutOfPerception, float attackDistNormalized)
    {
        if (isOutOfPerception)
        {
            parm.reward = -0.8f;
            return parm;
        }

        float score = -Mathf.Abs(attackDistNormalized - 1f) * 1.0f;
        score += parm.delta * 1.0f; // shaping 반영, weight는 실험 필요

        score += parm.alive == 1 ? 0.05f : -1f;

        parm.reward = score;
        return parm;
    }
}