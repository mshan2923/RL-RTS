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

    // 팀별 전체 엔티티 목록 (랜덤 타겟 폴백용)
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
        RandomSeed = (uint)UnityEngine.Time.frameCount + 1 // 0 방지
    }.ScheduleParallel(Dependency);

    unitParamMap.Dispose(Dependency);
    allyEntities.Dispose(Dependency);
    enemyEntities.Dispose(Dependency);
}

    partial struct AddJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int2, Entity>.ParallelWriter Grid;

        public void Execute([EntityIndexInQuery]int index, Entity entity, in LocalTransform transform, in UnitComponent unit)
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
            FindNearby(Grid, transform.Position, unitParams.DetectDistance, result, entity);

            Entity closest = Entity.Null;
            float closestDist = float.MaxValue;

            for (int t = 0; t < result.Length; t++)
            {
                var candidate = result[t];
                if (candidate == entity) continue;

                var candidatePos = transLookup.GetRefRO(candidate).ValueRO.Position;
                float dist = math.distancesq(transform.Position, candidatePos);

                if (dist < closestDist && unitEnum.type != TeamLookup.GetRefRO(candidate).ValueRO.type)
                {
                    closestDist = dist;
                    closest = candidate;
                }
            }


            if (closest == Entity.Null)
            {
                // 인식범위 안에 적이 없음 -> 랜덤 강제 배정
                var opponents = unitEnum.type == UnitEnum.Ally ? EnemyEntities : AllyEntities;

                if (opponents.Length > 0)
                {
                    var rand = new Unity.Mathematics.Random(RandomSeed + (uint)index + 1);
                    int pick = rand.NextInt(0, opponents.Length);
                    closest = opponents[pick];
                }
            }

            nearTarget = new CNearTarget
            {
                entity = closest,
            };
        }
    }

    public static void FindNearby(
        NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
        int2 center, int maxRadius,
        NativeList<Entity> result,
        Entity self) // 자기 자신을 알아야 정확히 판단 가능
    {
        using var visited = new NativeHashSet<Entity>(16, Allocator.Temp);

        for (int radius = 0; radius <= maxRadius; radius++)
        {
            for (int q = -radius; q <= radius; q++)
            {
                int r1 = math.max(-radius, -q - radius);
                int r2 = math.min(radius, -q + radius);

                if (q == -radius || q == radius)
                {
                    for (int r = r1; r <= r2; r++)
                        TryAdd(grid, center + new int2(q, r), visited, result);
                }
                else
                {
                    TryAdd(grid, center + new int2(q, r1), visited, result);
                    if (r2 != r1)
                        TryAdd(grid, center + new int2(q, r2), visited, result);
                }
            }

            // 자기 자신 말고 "진짜 다른 엔티티"가 있어야 멈춤
            bool hasOther = false;
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] != self) { hasOther = true; break; }
            }
            if (hasOther) return;
        }
    }
    private static void TryAdd(
        NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
        int2 coord, NativeHashSet<Entity> visited, NativeList<Entity> result)
    {
        if (grid.TryGetFirstValue(coord, out var entity, out var it))
        {
            do { if (visited.Add(entity)) result.Add(entity); }
            while (grid.TryGetNextValue(out entity, ref it));
        }
    }

    public static void FindNearby(
        NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
        float3 center, float radius,
        NativeList<Entity> result, Entity self)
    {
        var posInt = HexMetrics.WorldToOffset(center);
        int cell = (int)(radius / HexMetrics.outerRadius);
        FindNearby(grid, posInt, cell, result, self);
    }
}