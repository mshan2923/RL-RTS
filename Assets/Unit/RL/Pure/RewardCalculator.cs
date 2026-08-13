using Unity.Mathematics;

public static class RewardCalculator
{
    public static CObservation Apply(CObservation parm, bool isOutOfPerception, float attackDistNormalized)
    {
        if (isOutOfPerception)
        {
            parm.reward = -0.8f;
            return parm;
        }

        float distError = attackDistNormalized - 1f;
        float distScore = distError < 0
            ? distError * 1.0f          // 너무 가까움 -> 마이너스
            : -distError * 0.5f;         // 너무 멂 -> 마이너스 (기존엔 0에서 막혔던 부분)
        
        float score = distScore;
        // score -= parm.selfHp * 1f;
        // score += parm.targetHp * 1f;
        score += parm.alive == 1 ? 0.05f : -1f;
        score += parm.InAttackRange * 1f;

            // 가장자리에 가까울수록(distToEdge가 0에 가까울수록) 페널티
        // score += (parm.distToEdge - 1f) * 0.3f; // distToEdge=1(중앙)이면 0, distToEdge=0(벽)이면 -0.3


        parm.reward = score;
        return parm;
    }
}