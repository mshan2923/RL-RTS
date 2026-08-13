using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateAfter(typeof(SpatialGridSystem))]
partial struct RLExecuteSystem : ISystem
{
    // EntityQuery unitQuery;
    EntityQuery unitParmQuery;

    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // unitQuery = BuildQuery(ref state);
        unitParmQuery = DOTS_Mecro.UnitParmQuery(ref state);
    }


    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<RLMapSetting>(out var mapSetting)) return;

        // var unitParamMap = new NativeHashMap<UnitEnumComponent, CUnitParams>(4, Allocator.Temp);

        // var Near = state.World.GetExistingSystemManaged<SpatialGridSystem>().NearTarget.AsReadOnly();

        
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        // DOTS_Mecro.GetUnitParm(unitParmQuery, ref unitParamMap);

        //var size = SystemAPI.GetSingleton<RLParmCompoenent>();

        state.Dependency = new ExecuteJob
        {
            transLookup = state.GetComponentLookup<LocalTransform>(true),
            r2aLookup = state.GetComponentLookup<CanToAttackTag>(true),
            ecb = ecb.AsParallelWriter(),
            MapSize = mapSetting.Size,
            RandomJitterAngle = 15
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }

    EntityQuery BuildQuery(ref SystemState state)
    {
        var em = state.EntityManager;
        var build = new EntityQueryBuilder(Allocator.Temp).WithAll<CUnitState, LocalTransform>().WithAny<Disabled>();
        var query = em.CreateEntityQuery(build);
        build.Dispose();
        return query;
    }

    [BurstCompile]
    partial struct ExecuteJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> transLookup;
        [ReadOnly] public ComponentLookup<CanToAttackTag> r2aLookup;
        public EntityCommandBuffer.ParallelWriter ecb;
        public float2 MapSize;
        public float RandomJitterAngle; // 예: 15도 정도

        public void Execute([EntityIndexInQuery] int index, Entity entity, in LocalTransform transform, in CUnitState unitState, in CNearTarget nearTarget
            , ref MoveTargetComponent moveTarget)
        {
            var Target = nearTarget.entity;
            if (Target == Entity.Null) return;
            var targetPos = transLookup[Target].Position;
            var dir = math.normalize(targetPos - transform.Position);

            // 매 프레임 다른 시드로 랜덤값 생성
            var random = Unity.Mathematics.Random.CreateFromIndex((uint)(index + 1));
            float jitterDeg = random.NextFloat(-RandomJitterAngle, RandomJitterAngle);
            var rot = quaternion.RotateY(math.radians(jitterDeg));
            var jitteredDir = math.mul(rot, dir);

            switch (unitState.unitState)
            {
                case UnitState.MoveToward:
                    moveTarget.MoveTo = transform.Position + jitteredDir;
                    break;
                case UnitState.Retreat:
                    moveTarget.MoveTo = transform.Position - jitteredDir;
                    break;
                case UnitState.HoldPosition:
                    moveTarget.MoveTo = transform.Position;
                    break;
                case UnitState.Action:
                    break;
                default:
                    break;
            }

            moveTarget.MoveTo = new float3(
                math.clamp(moveTarget.MoveTo.x, 0, MapSize.x),
                moveTarget.MoveTo.y,
                math.clamp(moveTarget.MoveTo.z, 0, MapSize.y) // .y가 아니라 .z여야 함 (아래 참고)
            );

            if (r2aLookup.IsComponentEnabled(Target) != (unitState.unitState == UnitState.Action))
                ecb.SetComponentEnabled<CanToAttackTag>(index, Target, unitState.unitState == UnitState.Action);
        }
    }
}
