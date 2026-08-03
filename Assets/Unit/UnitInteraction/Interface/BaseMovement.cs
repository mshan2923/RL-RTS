using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class BaseMovement : UnitEnumInterface
{
    public float moveSpeed = 5f;
    public float MoveRadius = 10;

    public bool CanExecute(UnitActionContext ctx)
    {
        return true;// ctx.Entities.IsCreated && ctx.Entities.Length > 0;
    }

    public void Execute(UnitActionContext ctx)
    {
        Debug.Log($"Move Execution Started / {ctx.Entities.Length}");
        
        // Context 내부의 NativeArray가 External에서 Dispose될 수 있으므로 즉시 List로 복사

        if (ctx.Entities.Length > 0)
        {
            MoveUnitsAsync(ctx.Entities);
        }
    }

    public string GetLabel(UnitActionContext ctx)
    {
        return $"{ctx.unitEnum} Test Move";
    }

    private void MoveUnitsAsync(Entity[] targets)
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        foreach (var entity in targets)
        {
            if (!em.Exists(entity) || !em.HasComponent<LocalTransform>(entity)) continue;
            if (!em.HasComponent<MoveTargetComponent>(entity)) continue;

            var tr = em.GetComponentData<LocalTransform>(entity);
            float3 targetPos = tr.Position + new float3(
                UnityEngine.Random.Range(-MoveRadius, MoveRadius), 0,
                UnityEngine.Random.Range(-MoveRadius, MoveRadius));

            var moveTarget = em.GetComponentData<MoveTargetComponent>(entity);
            moveTarget.MoveTo = targetPos;
            em.SetComponentData(entity, moveTarget);
        }

        // 실제 이동은 MoveToTarget ISystem이 전담 — 여기선 목표만 지정하고 끝
    }
}