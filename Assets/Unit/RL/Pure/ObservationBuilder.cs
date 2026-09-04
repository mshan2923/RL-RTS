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
        public float currentPhi; // RLRunner에서 PrevPhi 갱신할 때 씀
    }

    public static BuildResult Build(
        int unitIndex, Entity entity, float3 selfPos,
        CHealth selfHealth, Entity target, EntityManager em, RLManager rLManager)
    {
        // 공통: distToEdge는 타겟 유무와 무관하게 항상 계산 가능
        float distToEdgeX = math.min(selfPos.x, rLManager.Size.x - selfPos.x) / (rLManager.Size.x / 2f);
        float distToEdgeZ = math.min(selfPos.z, rLManager.Size.y - selfPos.z) / (rLManager.Size.y / 2f);
        float distToEdge = math.min(distToEdgeX, distToEdgeZ);

        var shaping = em.GetComponentData<CRLShaping>(entity);

        if (target == Entity.Null)
        {
            // 타겟 자체가 없으면 거리 항은 의미 없음 -> distToEdge만으로 phi 구성
            float phiNoTarget = (distToEdge - 1f) * 0.3f;
            float deltaNoTarget = phiNoTarget - shaping.PrevPhi;

            var obs = new CObservation
            {
                unit_id = unitIndex,
                dx = 0f, dy = 0f,
                delta = deltaNoTarget,
                selfHp = 0,
                targetHp = 0f,
                InAttackRange = 0,
                distToEdge = distToEdge,
                alive = em.IsEnabled(entity) ? 1 : 0,
                reward = 0f,
                done = 0
            };
            return new BuildResult { obs = obs, isOutOfPerception = true, attackDistNormalized = 0f, currentPhi = phiNoTarget };
        }

        float detectDistance = 0;
        float attackDistance = 0;
        var team = em.GetComponentData<UnitEnumComponent>(target);
        switch (team.type)
        {
            case UnitEnum.Nature:
            case UnitEnum.Ally:
                detectDistance = rLManager.AllyData.DetectDistance;
                attackDistance = rLManager.AllyData.AttackDistance;
                break;
            case UnitEnum.Enmy:
                detectDistance = rLManager.EnmyData.DetectDistance;
                attackDistance = rLManager.EnmyData.AttackDistance;
                break;
            default:
                break;
        }

        var targetPos = em.GetComponentData<LocalTransform>(target).Position;
        var targetHealth = em.GetComponentData<CHealth>(target);

        float actualDist = math.length(targetPos - selfPos);
        bool isOutOfPerception = actualDist > detectDistance;

        // dx,dy는 인지거리 안/밖 상관없이 항상 실시간 방향(치트 허용) - 구석 쏠림 방지
        var dxy = (targetPos - selfPos) / detectDistance;
        var attackDistNormalized = math.length((targetPos - selfPos) / attackDistance);

        float currentPhi = RewardCalculator.ComputePhi(attackDistNormalized, distToEdge);
        float delta = currentPhi - shaping.PrevPhi;
    

        var result = new CObservation
        {
            unit_id = unitIndex,
            dx = Mathf.Clamp(dxy.x, -1f, 1f),
            dy = Mathf.Clamp(dxy.z, -1f, 1f),
            delta = delta,
            selfHp = (selfHealth.Prev - selfHealth.Current) / selfHealth.Max,
            targetHp = (targetHealth.Prev - targetHealth.Current) / targetHealth.Max,
            InAttackRange = actualDist < attackDistance ? 1 : 0,
            distToEdge = distToEdge,
            alive = em.IsEnabled(entity) ? 1 : 0,
            reward = 0f,
            done = selfHealth.Current > 0 ? 0 : 1
        };

        return new BuildResult { obs = result, isOutOfPerception = isOutOfPerception, attackDistNormalized = attackDistNormalized, currentPhi = currentPhi };
    }
}