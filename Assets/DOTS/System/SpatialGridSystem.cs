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
    [System.Obsolete]public NativeParallelHashMap<Entity, Entity> NearTarget;
    public EntityQuery unitParmQuery;
    public JobHandle handle;

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
        /*
        ! 셀 단위로 한 번만 계산해서 공유: "이 셀 반경 안에 뭐가 있는지"를 유닛별이 아니라 셀별로 캐싱하면, 같은 셀에 있는 유닛들이 계산을 공유할 수 있음
        ! DetectDistance를 매 프레임 다시 계산하지 않고, 몇 프레임에 한 번씩만 갱신 (타겟이 매 프레임 급격히 안 바뀌는 게임 특성상 충분할 수 있음)
        */

        Grid.Clear();

        var unitParamMap = new NativeHashMap<UnitEnumComponent, CUnitParams>(4, Allocator.TempJob);

        var unitParm = DOTS_Mecro.GetUnitParm(unitParmQuery, UnitEnum.Ally);
        DOTS_Mecro.GetUnitParm(unitParmQuery, ref unitParamMap);

        // 1. 그리드 채우기 잡 스케줄
        Dependency = new AddJob
        {
            Grid = Grid.AsParallelWriter()
        }.ScheduleParallel(Dependency);

        // Dependency.Complete();

        // 2. 타겟 찾기 잡 스케줄 (Dependency 체이닝 필요)
        Dependency = new FindJob
        {
            Grid = Grid.AsReadOnly(),
            transLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            parmMap = unitParamMap.AsReadOnly()
        }.ScheduleParallel(Dependency);
        
        handle = Dependency;
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

        public NativeHashMap<UnitEnumComponent, CUnitParams>.ReadOnly parmMap;

        public void Execute([EntityIndexInQuery]int index, Entity entity, in LocalTransform transform, in UnitComponent unit, in UnitEnumComponent unitEnum, ref CNearTarget nearTarget)
        {
            // Allocator.Temp를 사용하여 잡 내부에서 안전하게 할당하고 해제
            using var result = new NativeList<Entity>(Allocator.Temp);

            parmMap.TryGetValue(unitEnum, out var unitParams);

            FindNearby(Grid, transform.Position, unitParams.DetectDistance, result);

            Entity closest = Entity.Null;
            float closestDist = float.MaxValue;
            

            for (int t = 0; t < result.Length; t++)
            {
                var candidate = result[t];
                if (candidate == entity) continue;

                var candidatePos = transLookup.GetRefRO(candidate).ValueRO.Position;
                float dist = math.distancesq(transform.Position, candidatePos);

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = candidate;
                }
            }


            nearTarget = new CNearTarget
            {
                entity = closest
            };
        }
    }

    public static void FindNearby(
        NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
        int2 center, int maxRadius,
        NativeList<Entity> result)
    {
        using var visited = new NativeHashSet<Entity>(16, Allocator.Temp);

        for (int radius = 0; radius <= maxRadius; radius++)
        {
            // "정확히 이번 radius인 링"만 훑음 (0~radius 전체 아님)
            for (int q = -radius; q <= radius; q++)
            {
                int r1 = math.max(-radius, -q - radius);
                int r2 = math.min(radius, -q + radius);

                // q가 -radius나 +radius 끝(변의 양끝)일 때만 r 전체를 훑고,
                // 그 외엔 r1, r2(양 끝단)만 훑어서 "테두리"만 남김
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

            if (result.Length > 0) return;
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
        NativeList<Entity> result)
    {
        var posInt = HexMetrics.WorldToOffset(center);
        int cell = (int)(radius / HexMetrics.outerRadius);
        FindNearby(grid, posInt, cell, result);
    }
}