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
        var unitParamMap = new NativeHashMap<UnitEnumComponent, CUnitParams>(4, Allocator.Temp);

        // var Near = state.World.GetExistingSystemManaged<SpatialGridSystem>().NearTarget.AsReadOnly();

        
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        DOTS_Mecro.GetUnitParm(unitParmQuery, ref unitParamMap);



        state.Dependency = new ExecuteJob
        {
            transLookup = state.GetComponentLookup<LocalTransform>(true),//! Create 에서 만들기
            r2aLookup = state.GetComponentLookup<CanToAttackTag>(true),
            ecb = ecb.AsParallelWriter(),
            DetectDistance = 5f //! ============ 하드코딩
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
        public float DetectDistance;

        public void Execute([EntityIndexInQuery]int index, Entity entity, in LocalTransform transform, in CUnitState unitState, in CNearTarget nearTarget
            , ref MoveTargetComponent moveTarget)//, ref CWeaponCooldown weaponCooldown, ref DynamicBuffer<CActionCooldown> Actions
        {
            //가까이 있는 타겟을 찾고 ... 
            // if (!Near.TryGetValue(entity, out var Target)) return;
            var Target = nearTarget.entity;

            if (Target == Entity.Null) return;
            var targetPos = transLookup[Target].Position;
            var dir = math.normalize(targetPos - transform.Position);


            switch (unitState.unitState)
            {
                case UnitState.MoveToward:
                    moveTarget.MoveTo = transform.Position + dir;
                    break;
                case UnitState.Retreat:
                    moveTarget.MoveTo = transform.Position - dir;//! 임시!
                    break;
                case UnitState.HoldPosition:
                    moveTarget.MoveTo = transform.Position;
                    break;                   
                case UnitState.Action:
                //쿨다운 완료시 마다 이벤트시 수행 
                //일단 액션의 가치가 없게 먼저 해보자
                    break;
                default:
                    break;
            }



            //UnitState.Attack 이면 컴포넌트 활성화     
            if (r2aLookup.IsComponentEnabled(Target) != (unitState.unitState == UnitState.Action))
                ecb.SetComponentEnabled<CanToAttackTag>(index, Target, unitState.unitState == UnitState.Action);

        }

    }
}
