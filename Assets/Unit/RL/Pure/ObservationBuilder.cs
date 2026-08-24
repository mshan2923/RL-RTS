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
        if (target == Entity.Null)
        {
            var obs = new CObservation
            {
                unit_id = unitIndex,
                dx = 0f, dy = 0f,
                delta = 0,
                selfHp = selfHealth.Current / selfHealth.Max,
                targetHp = 0f,
                InAttackRange = 0,
                distToEdge = 0,
                alive = em.IsEnabled(entity) ? 1 : 0,
                reward = 0f,
                done = 0
            };
            // 타겟 없을 땐 phi도 의미 없으니 0으로
            return new BuildResult { obs = obs, isOutOfPerception = true, attackDistNormalized = 0f, currentPhi = 0f };
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

        var dxy = (targetPos - selfPos) / detectDistance;

        // 원래 공식으로 되돌림 (0 = 완전근접, 1 = 딱 사거리 경계, 1보다 커질수록 더 멂)
        var attackDistNormalized = math.length((targetPos - selfPos) / attackDistance);

        float distToEdgeX = math.min(selfPos.x, rLManager.Size.x - selfPos.x) / (rLManager.Size.x / 2f);
        float distToEdgeZ = math.min(selfPos.z, rLManager.Size.y - selfPos.z) / (rLManager.Size.y / 2f);
        float distToEdge = math.min(distToEdgeX, distToEdgeZ);

        // phi: 사거리 경계(1)에 가까울수록 0(최댓값), 멀거나 가까울수록 음수
        float currentPhi = -math.abs(attackDistNormalized - 1f);

        // PrevPhi는 RLRunner에서 읽어서 넘겨주는 대신, 여기서 직접 읽어도 됨
        var shaping = em.GetComponentData<CRLShaping>(entity);
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