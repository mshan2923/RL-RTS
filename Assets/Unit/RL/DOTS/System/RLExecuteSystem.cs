using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(SpatialGridSystem))]
partial struct RLExecuteSystem : ISystem
{
    EntityQuery unitQuery;
    EntityQuery unitParmQuery;
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        unitQuery = BuildQuery(ref state);
        unitParmQuery = DOTS_Mecro.UnitParmQuery(ref state);
    }


    public void OnUpdate(ref SystemState state)
    {
        var unitParamMap = new NativeHashMap<UnitEnumComponent, CUnitParams>(4, Allocator.Temp);

        // var Near = state.World.GetExistingSystemManaged<SpatialGridSystem>().NearTarget.AsReadOnly();

        
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        DOTS_Mecro.GetUnitParm(unitParmQuery, ref unitParamMap);

        //var handle = JobHandle.CombineDependencies(state.World.GetExistingSystemManaged<SpatialGridSystem>().handle, state.Dependency);

        state.Dependency = new ExecuteJob
        {
            transLookup = state.GetComponentLookup<LocalTransform>(true),
            r2aLookup = state.GetComponentLookup<CanToAttackTag>(true),
            ecb = ecb.AsParallelWriter(),
            DetectDistance = 5f
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }

    EntityQuery BuildQuery(ref SystemState state)
    {
        var em = state.EntityManager;
        var build = new EntityQueryBuilder(Allocator.Temp).WithAll<RLParm, CUnitState, LocalTransform>().WithAny<Disabled>();
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
            , ref MoveTargetComponent moveTarget, ref CWeaponCooldown weaponCooldown, ref DynamicBuffer<CActionCooldown> Actions)
        {
            //가까이 있는 타겟을 찾고 ... 
            // if (!Near.TryGetValue(entity, out var Target)) return;
            var Target = nearTarget.entity;

            if (Target == Entity.Null) return;

            switch (unitState.unitState)
            {
                case UnitState.None:
                    //moveTarget.MoveTo = transform.Position;
                    break;
                case UnitState.Move:
                    //지금은 적절 범위 유지 하기위해 이동
                    Move(transform, Target, ref moveTarget);
                    break;
                case UnitState.Stop:
                    moveTarget.MoveTo = transform.Position;
                    break;
                case UnitState.Attack:
                    //인식 범위 안에 타겟 있으면 수행 -> 컴포넌트 활성화 or 값만
                    // 일반 공격 쿨타임 , 스킬 쿨타임 (다이네믹 버퍼)
                    

                    break;
                case UnitState.Action:
                //쿨다운 완료시 마다 이벤트시 수행 
                //일단 액션의 가치가 없게 먼저 해보자
                    break;
                default:
                    break;
            }



            //UnitState.Attack 이면 컴포넌트 활성화     
            if (r2aLookup.IsComponentEnabled(Target) != (unitState.unitState == UnitState.Attack))
                ecb.SetComponentEnabled<CanToAttackTag>(index, Target, unitState.unitState == UnitState.Attack);

        }

        public void Move(LocalTransform trans, Entity target, ref MoveTargetComponent moveTarget)
        {
            var targetPos = transLookup[target].Position;
            var toTarget = targetPos - trans.Position;
            var currentDist = math.length(toTarget);

            // 이미 원하는 거리 근처면 (데드존) 목표 재계산 안 함 — 진동 방지
            float tolerance = 0.5f; // 여유 범위, 필요시 조정
            if (math.abs(currentDist - DetectDistance) < tolerance)
            {
                moveTarget.MoveTo = trans.Position; // 제자리 유지
                return;
            }

            var dir = math.normalize(toTarget);
            // 타겟으로부터 DetectDistance만큼 "떨어진" 지점 = 타겟 - dir * DetectDistance
            var desiredPos = targetPos - dir * DetectDistance;

            moveTarget.MoveTo = desiredPos;
        }
    }
}
