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
        return $"{ctx.unitEnum} Move";
    }

    private async void MoveUnitsAsync(Entity[] targets)
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        // 각 유닛별 목표 지점 계산 (랜덤 이동 테스트)
        Dictionary<Entity, float3> targetPositions = new Dictionary<Entity, float3>();
        foreach (var entity in targets)
        {
            if (em.Exists(entity) && em.HasComponent<LocalTransform>(entity))
            {
                var tr = em.GetComponentData<LocalTransform>(entity);
                float3 targetPos = tr.Position + new float3(UnityEngine.Random.Range(-MoveRadius, MoveRadius), 0, UnityEngine.Random.Range(-MoveRadius, MoveRadius));
                targetPositions[entity] = targetPos;
            }
        }

        // Awaitable 기반 비동기 프레임 루프
        bool isMoving = true;
        while (isMoving)
        {
            isMoving = false;
            float dt = Time.deltaTime;

            foreach (var entity in targets)
            {
                if (!em.Exists(entity) || !em.HasComponent<LocalTransform>(entity)) continue;

                var transform = em.GetComponentData<LocalTransform>(entity);
                bool hasMoveTarget = em.HasComponent<MoveTargetComponent>(entity);
                
                float3 currentPos = transform.Position;
                float3 targetPos = targetPositions[entity];

                float3 dir = targetPos - currentPos;
                dir.y = 0;
                float dist = math.length(dir);

                if (dist > math.max(0.1f, moveSpeed * dt))
                {
                    isMoving = true;
                    dir = math.normalize(dir);
                    transform.Position += dir * moveSpeed * dt;

                    em.SetComponentData(entity, transform);

                    if (hasMoveTarget)
                    {
                        var moveTarget = em.GetComponentData<MoveTargetComponent>(entity);
                        moveTarget.MoveTo = transform.Position;
                        em.SetComponentData(entity, moveTarget);
                    }
                }
            }

            if (!isMoving) break;
            await Awaitable.NextFrameAsync();
        }

        Debug.Log("Move Execution Finished");
    }
}