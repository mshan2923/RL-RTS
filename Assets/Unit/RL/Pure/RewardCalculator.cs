using Unity.Mathematics;
using UnityEngine;

[System.Serializable]
public struct PhiConfig
{
    [Tooltip("맵 가장자리 페널티 weight")]
    public float EdgePenalty;
    [Tooltip("delta에 곱할 weight")]
    public float DeltaWeight;
    [Tooltip("생존 보너스 (매 스텝) - 0으로 두었을 때 특정 액션 고착 문제 해소됨을 확인")]
    public float AliveBonus;
    [Tooltip("사망 페널티")]
    public float DeathPenalty;

    public static PhiConfig Default => new PhiConfig
    {
        EdgePenalty = 0.3f,
        DeltaWeight = 5.0f,
        AliveBonus = 0f,
        DeathPenalty = -0.3f,
    };
}

public static class RewardCalculator
{
    public static PhiConfig Config = PhiConfig.Default;

    // 4개 고정점만 정의: 0(완전근접,-1) -> attackDistance(최적,0) -> detectDistance(경계,-1) -> 그 이상(고정,-1)
    public static float ComputePhi(
        float actualDist,
        float desiredDistance,
        float detectDistance,
        float distToEdge)
    {
        const float DEAD_ZONE = 0.2f;

        float error =
            math.max(
                math.abs(actualDist - desiredDistance) - DEAD_ZONE,
                0f);

        float normalizer =
            math.max(
                math.max(detectDistance, desiredDistance),
                0.0001f);

        float normalizedError =
            error / normalizer;

        // 목표거리에서 0, 멀어질수록 부드럽게 감소
        float distancePhi =
            -normalizedError * normalizedError;

        float edgePhi =
            (distToEdge - 1f) * Config.EdgePenalty;

        return distancePhi + edgePhi;
    }

    public static CObservation Apply(
        CObservation parm,
        bool isOutOfPerception,
        float distanceNormalized,
        float desiredDistanceNormalized)
    {
        float score = parm.delta * Config.DeltaWeight;
        if (isOutOfPerception)
            score -= 0.01f;

        score += parm.alive == 1 ? 0 : Config.DeathPenalty;
        score += -parm.selfHp * 1.0f;
        score += parm.targetHp * 1.0f;

        parm.reward = score;
        return parm;
    }
}
