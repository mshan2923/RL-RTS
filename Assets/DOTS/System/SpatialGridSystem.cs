using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateBefore(typeof(UnitStateSystem))]
public partial class SpatialGridSystem : SystemBase
{
    public NativeParallelMultiHashMap<int2, Entity> Grid;
    public EntityQuery unitParmQuery;

    protected override void OnCreate()
    {
        int unitCapital = 1024;
        Grid = new NativeParallelMultiHashMap<int2, Entity>(unitCapital, Allocator.Persistent);
        unitParmQuery = DOTS_Mecro.UnitParmQuery(EntityManager);
    }

    protected override void OnDestroy()
    {
        if (Grid.IsCreated) Grid.Dispose();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        Grid.Clear();

        var unitParamMap = new NativeHashMap<UnitEnumComponent, CUnitParams>(4, Allocator.TempJob);
        DOTS_Mecro.GetUnitParm(unitParmQuery, ref unitParamMap);

        var allyEntities = DOTS_Mecro.GetTeamEntities(EntityManager, UnitEnum.Ally, Allocator.TempJob);
        var enemyEntities = DOTS_Mecro.GetTeamEntities(EntityManager, UnitEnum.Enmy, Allocator.TempJob);

        Dependency = new AddJob { Grid = Grid.AsParallelWriter() }.ScheduleParallel(Dependency);
        Dependency.Complete();

        Dependency = new FindJob
        {
            Grid = Grid.AsReadOnly(),
            transLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            TeamLookup = SystemAPI.GetComponentLookup<UnitEnumComponent>(true),
            parmMap = unitParamMap.AsReadOnly(),
            AllyEntities = allyEntities,
            EnemyEntities = enemyEntities,
            RandomSeed = (uint)UnityEngine.Time.frameCount + 1
        }.ScheduleParallel(Dependency);

        unitParamMap.Dispose(Dependency);
        allyEntities.Dispose(Dependency);
        enemyEntities.Dispose(Dependency);
    }

    partial struct AddJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int2, Entity>.ParallelWriter Grid;

        public void Execute([EntityIndexInQuery] int index, Entity entity, in LocalTransform transform, in UnitComponent unit)
        {
            var coord = HexMetrics.WorldToOffset(transform.Position);
            Grid.Add(coord, entity);
        }
    }

    [BurstCompile]
    partial struct FindJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int2, Entity>.ReadOnly Grid;
        [ReadOnly] public ComponentLookup<LocalTransform> transLookup;
        [ReadOnly] public ComponentLookup<UnitEnumComponent> TeamLookup;
        public NativeHashMap<UnitEnumComponent, CUnitParams>.ReadOnly parmMap;

        [ReadOnly] public NativeArray<Entity> AllyEntities;
        [ReadOnly] public NativeArray<Entity> EnemyEntities;
        public uint RandomSeed;

        public void Execute([EntityIndexInQuery] int index, Entity entity, in LocalTransform transform, in UnitComponent unit, in UnitEnumComponent unitEnum, ref CNearTarget nearTarget)
        {
            using var result = new NativeList<Entity>(Allocator.Temp);
            parmMap.TryGetValue(unitEnum, out var unitParams);

            // 팀 정보를 넘겨주어 "적"을 발견할 때까지 탐색하도록 변경
            FindNearbyEnemies(Grid, TeamLookup, unitEnum.type, transform.Position, unitParams.DetectDistance, result, entity);

            Entity closest = Entity.Null;
            float closestDist = float.MaxValue;

            for (int t = 0; t < result.Length; t++)
            {
                var candidate = result[t];
                if (candidate == entity) continue;

                // 적 팀만 필터링하여 최단 거리 비교
                if (TeamLookup.HasComponent(candidate) && unitEnum.type != TeamLookup.GetRefRO(candidate).ValueRO.type)
                {
                    var candidatePos = transLookup.GetRefRO(candidate).ValueRO.Position;
                    float dist = math.distancesq(transform.Position, candidatePos);

                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = candidate;
                    }
                }
            }

            if (closest == Entity.Null)
            {
                // 랜덤 폴백 제거 - 그냥 없는 채로 둠
            }

            nearTarget = new CNearTarget { entity = closest }; // closest가 Null이면 그대로 Null
        }
    }

    public static void FindNearbyEnemies(
        NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
        ComponentLookup<UnitEnumComponent> teamLookup,
        UnitEnum selfTeamType,
        float3 centerPos, float radius,
        NativeList<Entity> result,
        Entity self)
    {
        var centerCoord = HexMetrics.WorldToOffset(centerPos);
        int maxRadius = (int)(radius / HexMetrics.outerRadius);

        using var visited = new NativeHashSet<Entity>(16, Allocator.Temp);

        for (int r = 0; r <= maxRadius; r++)
        {
            int countBeforeRing = result.Length;

            // 특정 반경 r 범위의 셀 수집
            for (int q = -r; q <= r; q++)
            {
                int r1 = math.max(-r, -q - r);
                int r2 = math.min(r, -q + r);

                if (q == -r || q == r)
                {
                    for (int rIdx = r1; rIdx <= r2; rIdx++)
                        TryAdd(grid, centerCoord + new int2(q, rIdx), visited, result);
                }
                else
                {
                    TryAdd(grid, centerCoord + new int2(q, r1), visited, result);
                    if (r2 != r1)
                        TryAdd(grid, centerCoord + new int2(q, r2), visited, result);
                }
            }

            // 이번 반경(r) 내에 '적 유닛'이 한 명이라도 수집되었는지 확인
            bool foundEnemyInThisRadius = false;
            for (int i = countBeforeRing; i < result.Length; i++)
            {
                Entity e = result[i];
                if (e != self && teamLookup.HasComponent(e) && teamLookup[e].type != selfTeamType)
                {
                    foundEnemyInThisRadius = true;
                    break;
                }
            }

            // 적을 찾았다면 해당 반경까지의 엔티티만 가진 채 종료 (더 먼 반경은 안 뒤짐)
            if (foundEnemyInThisRadius) return;
        }
    }

    private static void TryAdd(
        NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
        int2 coord, NativeHashSet<Entity> visited, NativeList<Entity> result)
    {
        if (grid.TryGetFirstValue(coord, out var entity, out var it))
        {
            do
            {
                if (visited.Add(entity)) result.Add(entity);
            }
            while (grid.TryGetNextValue(out entity, ref it));
        }
    }
}