using UnityEngine;

public static class RewardCalculator
{

    public static float ComputePhi(float attackDistNormalized, float distToEdge)
    {
        float distErr = attackDistNormalized - 1f;
        float distPhi = distErr < 0
            ? distErr * 2.0f   // 너무 가까움: 기울기 2배로 강하게 페널티
            : -distErr * 1.0f; // 너무 멂: 기존대로

        float edgePhi = (distToEdge - 1f) * 0.3f; // distToEdge=1(중앙)이면 0, 0(벽)이면 -0.3

        return distPhi + edgePhi;
    }

    public static CObservation Apply(CObservation parm, bool isOutOfPerception, float attackDistNormalized)
    {
        if (isOutOfPerception)
        {
            parm.reward = -0.8f;
            return parm;
        }

        // 통합 phi(거리 + 가장자리)의 변화량(delta) 하나만 메인 신호로 사용.
        // phi 자체는 ObservationBuilder에서 계산되어 parm.delta에 이미 담겨 들어옴.
        float score = parm.delta * 1.0f;

        score += parm.alive == 1 ? 0.05f : -0.3f;

        parm.reward = score;
        return parm;
    }


}