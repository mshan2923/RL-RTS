using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

partial struct UnitStateSystem : ISystem
{
    float3 Target;
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        
    }

[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    // raycast 응답이 온 프레임에만 실행
    foreach (var v in SystemAPI.Query<CDirectionResponse>().WithChangeFilter<CDirectionResponse>())
    {
        Target = v.TargetPos;

        // 이 순간에만 대기 중인 유닛들 소비
        foreach (var (unitState, moveto, trans, entity) in SystemAPI.Query<RefRO<CUnitState>, RefRW<MoveTargetComponent>, RefRO<LocalTransform>>()
            .WithAll<CCommandPending>()
            .WithEntityAccess())
        {
                moveto.ValueRW.MoveTo = Target;

                switch (unitState.ValueRO.unitState)
                {
                    case UnitState.MoveToward:
                    case UnitState.Retreat:
                        moveto.ValueRW.MoveTo = Target;
                        break;
                    case UnitState.HoldPosition:
                        moveto.ValueRW.MoveTo = trans.ValueRO.Position;
                        break;
                }

                SystemAPI.SetComponentEnabled<CCommandPending>(entity, false);
            }
        }
    }
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }

}
