using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

partial struct RespawnSystem : ISystem
{
    EntityQuery respawnParamQuery;
    Random random;

    // [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using var build = new EntityQueryBuilder(Allocator.Temp);
        build.WithAll<RLParmCompoenent>();
        respawnParamQuery = build.Build(ref state);

        random = new Random((uint)System.DateTime.Now.Ticks); // 최초 1회만 시드 생성
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<RLMapSetting>(out var mapSetting)) return;

        // // 팀별 스폰 파라미터를 맵으로 미리 구성
        // var paramMap = new NativeHashMap<UnitEnumComponent, RLParmCompoenent>(4, Allocator.TempJob);
        // using (var paramArray = respawnParamQuery.ToComponentDataArray<RLParmCompoenent>(Allocator.Temp))
        // {
        //     foreach (var p in paramArray)
        //         paramMap.TryAdd(new UnitEnumComponent { type = p.TeamType }, p);
        // }

        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        var color = SystemAPI.GetSingleton<CUnitPrefab>();

        state.Dependency = new taggingJob
        {
            ecb = ecb
        }.ScheduleParallel(state.Dependency);

        var job = new RespawnJob
        {
            // paramMap = paramMap,
            random = random,
            ecb = ecb,
            unitPrefab = color,
            rLMapSetting = mapSetting
        };

        state.Dependency = job.Schedule(state.Dependency); // Random 상태 갱신 때문에 병렬화 안 함
        // paramMap.Dispose(state.Dependency);

        // 다음 프레임을 위해 랜덤 상태 진행 (job 안에서 값 복사로 쓰였으니 여기서 한 번 더 굴려줌)
        random.NextUInt();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }

    [BurstCompile]
    public partial struct taggingJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ecb;

        public void Execute([EntityIndexInQuery] int index, Entity entity, in CHealth health)
        {
            if (health.Current > 0) return;

            ecb.SetComponentEnabled<UnitRespawnTag>(index, entity, true);
        }
    }

    [BurstCompile]
    [WithAll(typeof(UnitRespawnTag))]
    public partial struct RespawnJob : IJobEntity
    {
        // [ReadOnly] public NativeHashMap<UnitEnumComponent, RLParmCompoenent> paramMap;
        public Random random;
        public EntityCommandBuffer.ParallelWriter ecb;

        public CUnitPrefab unitPrefab;

        public RLMapSetting rLMapSetting;

        public void Execute([EntityIndexInQuery] int index, Entity entity,
            ref LocalTransform transform, ref MoveTargetComponent moveTo, in UnitEnumComponent team, in UnitRespawnTag tag, in CHealth health)
        {
            // if (!paramMap.TryGetValue(team, out var rLParm)) return;

            var size = new float3(rLMapSetting.Size.x, 0, rLMapSetting.Size.y);
            var offset = new float3(rLMapSetting.SpawnRandomOffset, 0, rLMapSetting.SpawnRandomOffset);

            // 병렬 job에서 안전한 랜덤: index로 섞어서 엔티티마다 다른 결과 보장
            var localRandom = Random.CreateFromIndex((uint)(random.NextUInt() + index));
            var pos = localRandom.NextFloat3(-size + offset, size - offset) + size * 0.5f;

            transform.Position = pos;
            moveTo.MoveTo = pos;

            ecb.SetComponentEnabled<UnitRespawnTag>(index, entity, false);

            var result = health;
            result.Prev = result.Max;
            result.Current = result.Max;

            ecb.SetComponent<CHealth>(index, entity, result);

            switch (team.type)
            {
                case UnitEnum.Nature:
                    ecb.SetComponent(index, entity, new URPMaterialPropertyBaseColor {Value = new float4(1,1,1,1)});
                    break;
                case UnitEnum.Ally:
                    ecb.SetComponent(index, entity, new URPMaterialPropertyBaseColor {Value = unitPrefab.AllyColor});
                    break;
                case UnitEnum.Enmy:
                    ecb.SetComponent(index, entity, new URPMaterialPropertyBaseColor {Value = unitPrefab.EnmyColor});
                    break;
                default:
                    break;
            }
        }
    }
}