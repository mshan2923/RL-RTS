using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public partial struct CaptureSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var unitCellMap = new NativeHashMap<int2, int>(16, Allocator.TempJob);

        foreach (var (transform, unit) in
            SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitComponent>>())
        {
            var cell = HexMetrics.WorldToOffset(transform.ValueRO.Position);
            unitCellMap.TryAdd(new int2(cell.x, cell.y), unit.ValueRO.Id);
        }

        state.Dependency = new CaptureJob { UnitCellMap = unitCellMap }
            .ScheduleParallel(state.Dependency);

        unitCellMap.Dispose(state.Dependency);
    }
}

[BurstCompile]
partial struct CaptureJob : IJobEntity
{
    [ReadOnly] public NativeHashMap<int2, int> UnitCellMap;

    void Execute(ref HexTile tile)
    {
        var key = new int2(tile.X, tile.Z);
        if (!UnitCellMap.ContainsKey(key)) return;
        if (tile.OwnerID == GroupType.Ally) return;

        tile.OwnerID = GroupType.Ally;
        tile.IsOccupied = true;
        tile.JustCaptured = true;
    }
}