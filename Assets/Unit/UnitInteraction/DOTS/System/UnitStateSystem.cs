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
            if (unitState.ValueRO.unitState != UnitState.None)
            {
                moveto.ValueRW.MoveTo = Target;
            }

                switch (unitState.ValueRO.unitState)
                {
                    case UnitState.None:
                        break;
                    case UnitState.Move:
                        moveto.ValueRW.MoveTo = Target;
                        break;
                    case UnitState.Stop:
                        moveto.ValueRW.MoveTo = trans.ValueRO.Position;
                        break;
                    case UnitState.Attack:
                        moveto.ValueRW.MoveTo = Target;
                        break;
                    case UnitState.Action:
                        moveto.ValueRW.MoveTo = Target;
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

    [BurstCompile]
    public partial struct UnitExecute : IJobEntity
    {
        public float3 TargetPos;
        public float Speed;
        public float deltaTime;

        public void Execute([EntityIndexInQuery]int index, Entity entity, ref CUnitState unitState , in LocalTransform transform, ref MoveTargetComponent move)
        {
            // if (math.distancesq(TargetPos , transform.Position) < (Speed * Speed) * (deltaTime * deltaTime))
            // {
            //     unitState = new CUnitState { unitState = UnitState.Stop};
            //     return;
            // }

            switch (unitState.unitState)
            {
                case UnitState.None:
                    break;
                case UnitState.Move:
                    move.MoveTo = TargetPos;//transform.Position + Move(transform);
                    break;
                case UnitState.Stop:
                    break;
                case UnitState.Attack:
                    move.MoveTo = transform.Position + Move(transform);
                    break;
                case UnitState.Action:
                    // move.MoveTo = transform.Position + Move(transform);
                    break;
            }

        }

        public float3 Move(in LocalTransform transform)
        {
            return Speed * deltaTime * math.normalizesafe(TargetPos - transform.Position);
        }
    }
}
