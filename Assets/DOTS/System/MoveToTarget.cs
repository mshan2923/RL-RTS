using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

partial struct MoveToTarget : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {

    }

    // [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        
        var moveLength = 50f * MapConfig.FixedStepSize;

        var data = state.World.GetExistingSystemManaged<ObstacleSystem>().data.AsReadOnly();

        foreach (var (move, trans, entity) in SystemAPI.Query<RefRW<MoveTargetComponent>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            var currentPos = trans.ValueRO.Position;
            var targetPos = move.ValueRO.MoveTo;
            
            // 1. 목표지점에 이미 도착했는지 체크 (이걸 먼저 해야 덜덜거림이 사라짐)
            var dis = math.distance(currentPos, targetPos);
            if (dis < 0.1f) continue; 

            // 2. 가려는 방향 계산
            var direction = math.normalize(targetPos - currentPos);
            var nextPos = currentPos + direction * math.min(moveLength, dis);
            
            // 3. 벽 체크
            var offset = HexMetrics.WorldToOffset(nextPos);
            bool isBlocked = false;

            if (data.TryGetValue(offset, out var tile))
            {
                if (tile.OwnerID == GroupType.Wall)
                {
                    isBlocked = true;
                }
            }

            // 4. 벽이 아니면 이동
            if (!isBlocked)
            {
                move.ValueRW.PrevPosition = currentPos;
                trans.ValueRW.Position = nextPos;
                trans.ValueRW.Rotation = quaternion.LookRotationSafe(direction, math.up());
            }
            else
            {
                // 5. [핵심] 벽에 막혔을 때, 제자리에서 회전만 하거나
                // AI 모델이 "막혔다"는 걸 알게 하려면 여기 특정 컴포넌트(isBlockedFlag)를 넣어줘.
                // 그러면 AI가 "아, 이 방향은 막혔으니 다른 방향을 찾아야지"라고 학습함.
            }
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }
}
