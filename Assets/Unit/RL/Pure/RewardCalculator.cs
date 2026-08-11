using Unity.Mathematics;

public static class RewardCalculator
{
    public static CObservation Apply(CObservation parm, bool isOutOfPerception, float attackDistNormalized)
    {
        if (isOutOfPerception)
        {
            parm.reward = 0f;
            return parm;
        }

        float distError = math.abs(attackDistNormalized - 1f);
        float distScore;
        if (distError < 0)
            distScore = distError * 1.0f; // 너무 가까우면 선형 페널티 (마이너스로 떨어짐)
        else
            distScore = (1f - math.saturate(distError)) * 0.5f; // 멀면 기존처럼

        float score = distScore;
        score -= parm.selfHp * 1f;
        score += parm.targetHp * 1f;
        score += parm.alive == 1 ? 0.05f : -1f;
        score += parm.InAttackRange * 1f;

        parm.reward = score;
        return parm;
    }
}