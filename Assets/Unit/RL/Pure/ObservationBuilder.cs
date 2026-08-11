using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public static class ObservationBuilder
{
    public struct BuildResult
    {
        public CObservation obs;
        public bool isOutOfPerception;
        public float attackDistNormalized;
    }

    public static BuildResult Build(
        int unitIndex, Entity entity, float3 selfPos,
        CHealth selfHealth, Entity target, EntityManager em,
        float detectDistance, float attackDistance)
    {
        if (target == Entity.Null)
        {
            var obs = new CObservation
            {
                unit_id = unitIndex,
                dx = 0f, dy = 0f,
                selfHp = selfHealth.Current / selfHealth.Max,
                targetHp = 0f,
                InAttackRange = 0,
                alive = em.IsEnabled(entity) ? 1 : 0,
                reward = 0f,
                done = 0
            };
            return new BuildResult { obs = obs, isOutOfPerception = true, attackDistNormalized = 0f };
        }

        var targetPos = em.GetComponentData<LocalTransform>(target).Position;
        var targetHealth = em.GetComponentData<CHealth>(target);

        float actualDist = math.length(targetPos - selfPos);
        bool isOutOfPerception = actualDist > detectDistance;

        var dxy = (targetPos - selfPos) / detectDistance;
        var attackDistNormalized = math.length((targetPos - selfPos) / attackDistance);

        var result = new CObservation
        {
            unit_id = unitIndex,
            dx = Mathf.Clamp(dxy.x, -1f, 1f),
            dy = Mathf.Clamp(dxy.z, -1f, 1f),
            selfHp = (selfHealth.Prev - selfHealth.Current) / selfHealth.Max,
            targetHp = (targetHealth.Prev - targetHealth.Current) / targetHealth.Max,
            InAttackRange = actualDist < attackDistance ? 1 : 0,
            alive = em.IsEnabled(entity) ? 1 : 0,
            reward = 0f,
            done = selfHealth.Current > 0 ? 0 : 1
        };

        return new BuildResult { obs = result, isOutOfPerception = isOutOfPerception, attackDistNormalized = attackDistNormalized };
    }
}