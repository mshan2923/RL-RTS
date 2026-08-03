using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateBefore(typeof(UnitStateSystem))]
public partial class SpatialGridSystem : SystemBase
{
    public NativeParallelMultiHashMap<int2, Entity> Grid;

    //! 셀마다 dps , 계산

    protected override void OnCreate()
    {
        Grid = new NativeParallelMultiHashMap<int2, Entity>(1024, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (Grid.IsCreated) Grid.Dispose();
    }

    protected override void OnUpdate()
    {
        Grid.Clear();

        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<UnitComponent>().WithEntityAccess())
        {
            var coord = HexMetrics.WorldToOffset(transform.ValueRO.Position);
            Grid.Add(coord, entity);
        }
    }

    public static void FindNearby(
    NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
    int2 center, int radius,
    NativeList<Entity> result)
    {
        for (int q = -radius; q <= radius; q++)
        {
            int r1 = math.max(-radius, -q - radius);
            int r2 = math.min(radius, -q + radius);
            for (int r = r1; r <= r2; r++)
            {
                var coord = center + new int2(q, r);
                if (grid.TryGetFirstValue(coord, out var entity, out var it))
                {
                    do { result.Add(entity); }
                    while (grid.TryGetNextValue(out entity, ref it));
                }
            }
        }
    }

    public static void FindNearby(
    NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
    float3 center, float radius,
    NativeList<Entity> result)
    {
        var posInt = HexMetrics.WorldToOffset(center);
        int Cell = (int)(radius / HexMetrics.outerRadius);
        FindNearby(grid, posInt, Cell, result);
    }
}