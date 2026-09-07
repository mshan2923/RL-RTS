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
    public EntityQuery UnitQuery;

    protected override void OnCreate()
    {
        unitParmQuery = DOTS_Mecro.UnitParmQuery(EntityManager);


        using var build = new EntityQueryBuilder(Allocator.Temp);
        UnitQuery = build.WithAll<UnitComponent>().Build(EntityManager);
    }

    protected override void OnDestroy()
    {
        if (Grid.IsCreated) Grid.Dispose();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        if (UnitQuery.CalculateEntityCount() == 0 ) return;

        if (Grid.IsCreated == false)
        {
            Debug.Log($"[SpatialGridSystem] Grid is not created. Creating new grid. -> {UnitQuery.CalculateEntityCount()}");
            Grid = new NativeParallelMultiHashMap<int2, Entity>(UnitQuery.CalculateEntityCount(), Allocator.Persistent);
        }

        Grid.Clear();

        var unitParamMap = new NativeHashMap<UnitEnumComponent, CUnitParams>(2, Allocator.TempJob);
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

    [BurstCompile]
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
            parmMap.TryGetValue(unitEnum, out var unitParams);

            Entity closest = FindNearestEnemy(Grid, TeamLookup, transLookup, unitEnum.type, transform.Position, unitParams.DetectDistance, entity);

            // 인지거리 안에서 못 찾았을 때: 전체 맵에서 가장 가까운 적으로 폴백 (isOutOfPerception=true로 계속 표시됨)
            if (closest == Entity.Null)
            {
                var opponents = unitEnum.type == UnitEnum.Ally ? EnemyEntities : AllyEntities;
                float minDist = float.MaxValue;
                for (int i = 0; i < opponents.Length; i++)
                {
                    if (!transLookup.HasComponent(opponents[i])) continue;
                    float d = math.distancesq(transform.Position, transLookup[opponents[i]].Position);
                    if (d < minDist) { minDist = d; closest = opponents[i]; }
                }
            }

            nearTarget = new CNearTarget { entity = closest };
        }

        /// <summary>
        /// 링(반경) 단위로 그리드를 탐색하면서, 적 팀 엔티티를 만나는 즉시 최근접 갱신까지 끝낸다.
        /// 기존처럼 NativeList에 후보를 모았다가 다시 순회하지 않는다 (이중 순회 제거).
        /// 적을 한 명이라도 찾은 반경에서 탐색을 종료한다 (기존 동작과 동일).
        /// </summary>
        static Entity FindNearestEnemy(
            NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
            ComponentLookup<UnitEnumComponent> teamLookup,
            ComponentLookup<LocalTransform> transLookup,
            UnitEnum selfTeamType,
            float3 centerPos, float radius,
            Entity self)
        {
            var centerCoord = HexMetrics.WorldToOffset(centerPos);
            int maxRadius = (int)(radius / HexMetrics.outerRadius);

            using var visited = new NativeHashSet<Entity>(16, Allocator.Temp);

            Entity closest = Entity.Null;
            float closestDist = float.MaxValue;

            for (int r = 0; r <= maxRadius; r++)
            {
                bool foundEnemyInThisRadius = false;

                for (int q = -r; q <= r; q++)
                {
                    int r1 = math.max(-r, -q - r);
                    int r2 = math.min(r, -q + r);

                    if (q == -r || q == r)
                    {
                        for (int rIdx = r1; rIdx <= r2; rIdx++)
                        {
                            CheckCell(grid, centerCoord + new int2(q, rIdx), self, selfTeamType,
                                teamLookup, transLookup, visited, centerPos,
                                ref closest, ref closestDist, ref foundEnemyInThisRadius);
                        }
                    }
                    else
                    {
                        CheckCell(grid, centerCoord + new int2(q, r1), self, selfTeamType,
                            teamLookup, transLookup, visited, centerPos,
                            ref closest, ref closestDist, ref foundEnemyInThisRadius);
                        if (r2 != r1)
                        {
                            CheckCell(grid, centerCoord + new int2(q, r2), self, selfTeamType,
                                teamLookup, transLookup, visited, centerPos,
                                ref closest, ref closestDist, ref foundEnemyInThisRadius);
                        }
                    }
                }

                // 이번 반경에서 적을 하나라도 찾았으면 더 먼 반경은 보지 않고 종료 (기존과 동일한 동작)
                if (foundEnemyInThisRadius) return closest;
            }

            return closest;
        }

        static void CheckCell(
            NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
            int2 coord, Entity self, UnitEnum selfTeamType,
            ComponentLookup<UnitEnumComponent> teamLookup,
            ComponentLookup<LocalTransform> transLookup,
            NativeHashSet<Entity> visited, float3 centerPos,
            ref Entity closest, ref float closestDist, ref bool foundEnemyInThisRadius)
        {
            if (!grid.TryGetFirstValue(coord, out var e, out var it)) return;

            do
            {
                if (e == self || !visited.Add(e)) continue;
                if (!teamLookup.HasComponent(e) || teamLookup[e].type == selfTeamType) continue;

                foundEnemyInThisRadius = true;

                float d = math.distancesq(centerPos, transLookup[e].Position);
                if (d < closestDist)
                {
                    closestDist = d;
                    closest = e;
                }
            }
            while (grid.TryGetNextValue(out e, ref it));
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