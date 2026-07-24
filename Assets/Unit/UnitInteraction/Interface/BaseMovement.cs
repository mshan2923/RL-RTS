using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;


public class BaseMovement :  UnitEnumInterface
{
    private List<Entity> cachedUnits = new List<Entity>();
    public float moveSpeed = 5f;
    public void Invoke(UnitEnum unitEnum, NativeArray<Entity> unitArray)
    {
        cachedUnits.Clear();
        for (int i = 0; i < unitArray.Length; i++)
        {
            cachedUnits.Add(unitArray[i]);
        }
    }

    public async void EndInvoke(UnitEnum unitEnum)
    {
        if (cachedUnits.Count == 0) return;

        // 비동기 도중에 리스트가 비워지는 걸 막기 위해 복사
        var targets = new List<Entity>(cachedUnits);
        cachedUnits.Clear();

        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        
        // 각 유닛별로 목표 지점 미리 계산 (테스트용 랜덤)
        Dictionary<Entity, float3> targetPositions = new Dictionary<Entity, float3>();
        foreach (var entity in targets)
        {
            if (em.Exists(entity) && em.HasComponent<LocalTransform>(entity))
            {
                var tr = em.GetComponentData<LocalTransform>(entity);
                float3 targetPos = tr.Position + new float3(UnityEngine.Random.Range(-10f, 10f), 0, UnityEngine.Random.Range(-10f, 10f));
                targetPositions[entity] = targetPos;
            }
        }

        // Awaitable로 매 프레임 일정 속도 이동 처리
        bool isMoving = true;
        while (isMoving )
        {



            isMoving = false;
            float dt = Time.deltaTime;

            foreach (var entity in targets)
            {
                if (!em.Exists(entity) || !em.HasComponent<LocalTransform>(entity)) continue;

                var transform = em.GetComponentData<LocalTransform>(entity);
                var moveTarget = em.GetComponentData<MoveTargetComponent>(entity);

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
                    moveTarget.MoveTo = transform.Position;

                    em.SetComponentData(entity, transform);
                    em.SetComponentData(entity, moveTarget);
                }else
                {

                }
            }

            if (!isMoving) break;
            await Awaitable.NextFrameAsync();
        }
    }
}