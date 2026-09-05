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
        public float currentPhi;
    }

    public static BuildResult Build(
        int unitIndex, Entity entity, float3 selfPos,
        CHealth selfHealth, Entity target, EntityManager em, RLManager rLManager)
    {
        float distToEdgeX = math.min(selfPos.x, rLManager.Size.x - selfPos.x) / (rLManager.Size.x / 2f);
        float distToEdgeZ = math.min(selfPos.z, rLManager.Size.y - selfPos.z) / (rLManager.Size.y / 2f);
        float distToEdge = math.min(distToEdgeX, distToEdgeZ);

        var shaping = em.GetComponentData<CRLShaping>(entity);
        var Parms = em.GetComponentData<CUnitParams>(entity);

        // 타겟 없음 (랜덤 폴백 제거된 상태 기준)
        if (target == Entity.Null || !em.Exists(target))
        {
            float phiNoTarget = (distToEdge - 1f) * RewardCalculator.Config.EdgePenalty;
            float deltaNoTarget = phiNoTarget - shaping.PrevPhi;

            var obs = new CObservation
            {
                unit_id = unitIndex,
                dx = 0f, dy = 0f,
                delta = deltaNoTarget,
                selfHp = 0f,
                targetHp = 0f,
                InAttackRange = 0,
                distToEdge = distToEdge,
                AttackTendency = Parms.AttackTendency,
                alive = em.IsEnabled(entity) ? 1 : 0,
                reward = 0f,
                done = selfHealth.Current > 0 ? 0 : 1
            };
            return new BuildResult { obs = obs, isOutOfPerception = true, attackDistNormalized = 0f, currentPhi = phiNoTarget };
        }

        float detectDistance = 0.0001f;
        float attackDistance = 0.0001f;

        if (em.HasComponent<UnitEnumComponent>(target))
        {
            var team = em.GetComponentData<UnitEnumComponent>(target);
            switch (team.type)
            {
                case UnitEnum.Nature:
                case UnitEnum.Ally:
                    detectDistance = math.max(rLManager.AllyData.DetectDistance, 0.0001f);
                    attackDistance = math.max(rLManager.AllyData.AttackDistance, 0.0001f);
                    break;
                case UnitEnum.Enmy:
                    detectDistance = math.max(rLManager.EnmyData.DetectDistance, 0.0001f);
                    attackDistance = math.max(rLManager.EnmyData.AttackDistance, 0.0001f);
                    break;
            }
        }

        var targetPos = em.GetComponentData<LocalTransform>(target).Position;
        var targetHealth = em.GetComponentData<CHealth>(target);

        float actualDist = math.length(targetPos - selfPos);
        bool isOutOfPerception = actualDist > detectDistance;

        var dxy = (targetPos - selfPos) / detectDistance;
        var attackDistNormalized = math.length((targetPos - selfPos) / attackDistance);

        // 인지거리~공격거리~0 세 구간 기준으로 phi 계산
        float currentPhi = RewardCalculator.ComputePhi(actualDist, detectDistance, attackDistance, distToEdge);
        float delta = currentPhi - shaping.PrevPhi;

        float selfMax = math.max(selfHealth.Max, 1f);
        float targetMax = math.max(targetHealth.Max, 1f);

        var result = new CObservation
        {
            unit_id = unitIndex,
            dx = Mathf.Clamp(dxy.x, -1f, 1f),
            dy = Mathf.Clamp(dxy.z, -1f, 1f),
            delta = delta,
            selfHp = (selfHealth.Prev - selfHealth.Current) / selfMax,
            targetHp = (targetHealth.Prev - targetHealth.Current) / targetMax,
            InAttackRange = actualDist < attackDistance ? 1 : 0,
            distToEdge = distToEdge,
            AttackTendency = Parms.AttackTendency,
            alive = em.IsEnabled(entity) ? 1 : 0,
            reward = 0f,
            done = selfHealth.Current > 0 ? 0 : 1
        };

        return new BuildResult { obs = result, isOutOfPerception = isOutOfPerception, attackDistNormalized = attackDistNormalized, currentPhi = currentPhi };
    }
}