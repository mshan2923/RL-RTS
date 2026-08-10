using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

partial struct RLAttackSystem : ISystem
{
    public EntityQuery attackUnitQuery;
    public EntityQuery unitParmQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using var build = new EntityQueryBuilder(Allocator.Temp);
        attackUnitQuery = build.WithAll<UnitEnumComponent, CanToAttackTag>().Build(ref state);
        unitParmQuery = DOTS_Mecro.UnitParmQuery(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var parmMap = new NativeHashMap<UnitEnumComponent, CUnitParams>(4, Allocator.TempJob);
        DOTS_Mecro.GetUnitParm(unitParmQuery, ref parmMap);

        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        // 1. 쿨타임 갱신 job
        var cooldownJob = new CooldownJob
        {
            parmMap = parmMap,
            deltaTime = SystemAPI.Time.DeltaTime,
            ecb = ecb
        };
        state.Dependency = cooldownJob.ScheduleParallel(state.Dependency);

        // 2. 공격 실행 job — HP를 건드리는 엔티티(near.entity)와 순회 대상(entity)이 달라서
        //    병렬 처리 시 같은 타겟에 대한 쓰기 충돌 위험이 있음. 안전하게 Schedule(단일 스레드)로 둠.
        var attackJob = new AttackJob
        {
            parmMap = parmMap,
            hpLookup = SystemAPI.GetComponentLookup<CHealth>(true),
            ecb = ecb
        };
        state.Dependency = attackJob.Schedule(state.Dependency); // ScheduleParallel 아님 — 안전을 위해

        parmMap.Dispose(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }

    [BurstCompile]
    [WithAll(typeof(CanToAttackTag))]
    public partial struct CooldownJob : IJobEntity
    {
        [ReadOnly] public NativeHashMap<UnitEnumComponent, CUnitParams> parmMap;
        public float deltaTime;
        public EntityCommandBuffer.ParallelWriter ecb;

        public void Execute([EntityIndexInQuery] int index, Entity entity, ref CWeaponCooldown cooldown, in UnitEnumComponent team)
        {
            parmMap.TryGetValue(team, out var unitParm);

            var time = cooldown;
            if (time.cooldown < unitParm.FireRate)
            {
                time.cooldown += deltaTime;
            }
            else
            {
                time.cooldown = 0;
                ecb.SetComponentEnabled<ReadyToShotTag>(index, entity, true);
            }
            cooldown = time;
        }
    }

    [BurstCompile]
    [WithAll(typeof(ReadyToShotTag))]
    public partial struct AttackJob : IJobEntity
    {
        [ReadOnly] public NativeHashMap<UnitEnumComponent, CUnitParams> parmMap;
        [ReadOnly] public ComponentLookup<CHealth> hpLookup;
        public EntityCommandBuffer.ParallelWriter ecb;

        public void Execute([EntityIndexInQuery] int index, Entity entity, in UnitEnumComponent team, in CNearTarget near)
        {
            parmMap.TryGetValue(team, out var unitParm);

            if (near.entity != Entity.Null && hpLookup.HasComponent(near.entity))
            {
                var targetHp = hpLookup[near.entity];
                targetHp.Prev = targetHp.Current;
                targetHp.Current -= unitParm.Damage;
                ecb.SetComponent(index, near.entity, targetHp);
            }

            ecb.SetComponentEnabled<ReadyToShotTag>(index, entity, false);
        }
    }
}