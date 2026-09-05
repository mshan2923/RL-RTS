using Unity.Mathematics;
using UnityEngine;

[System.Serializable]
public struct PhiConfig
{
    [Tooltip("맵 가장자리 페널티 weight")]
    public float EdgePenalty;
    [Tooltip("delta에 곱할 weight")]
    public float DeltaWeight;
    [Tooltip("생존 보너스 (매 스텝)")]
    public float AliveBonus;
    [Tooltip("사망 페널티")]
    public float DeathPenalty;

    public static PhiConfig Default => new PhiConfig
    {
        EdgePenalty = 0.3f,
        DeltaWeight = 5.0f,
        AliveBonus = 0.05f,
        DeathPenalty = -0.3f,
    };
}

public static class RewardCalculator
{
    public static PhiConfig Config = PhiConfig.Default;

    // 4개 고정점만 정의: 0(완전근접,-1) -> attackDistance(최적,0) -> detectDistance(경계,-1) -> 그 이상(고정,-1)
    public static float ComputePhi(float actualDist, float detectDistance, float attackDistance, float distToEdge)
    {
        const float PHI_ZERO = -1f;
        const float PHI_BEST = 0f;
        const float PHI_EDGE = -1f;

        float distPhi;
        if (actualDist <= attackDistance)
        {
            float t = actualDist / math.max(attackDistance, 0.0001f);
            distPhi = math.lerp(PHI_ZERO, PHI_BEST, math.saturate(t));
        }
        else
        {
            float t = (actualDist - attackDistance) / math.max(detectDistance - attackDistance, 0.0001f);
            distPhi = math.lerp(PHI_BEST, PHI_EDGE, math.saturate(t));
        }

        float edgePhi = (distToEdge - 1f) * Config.EdgePenalty;
        return distPhi + edgePhi;
    }

    public static CObservation Apply(CObservation parm, bool isOutOfPerception, float attackDistNormalized)
    {
        if (isOutOfPerception)
        {
            parm.reward = (parm.delta * Config.DeltaWeight) - 0.01f;
            return parm;
        }

        float score = parm.delta * Config.DeltaWeight;
        score += parm.alive == 1 ? 0 : Config.DeathPenalty;
        score += -parm.selfHp * 1.0f;
        score += parm.targetHp * 1.0f;

        parm.reward = score;

        Debug.Log($"unit={parm.unit_id}, delta={parm.delta}, selfHp={parm.selfHp}, targetHp={parm.targetHp}, aliveTerm={(parm.alive==1?Config.AliveBonus:Config.DeathPenalty)}, totalScore={parm.reward}");
        return parm;
    }
}