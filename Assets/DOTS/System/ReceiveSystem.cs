using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public partial struct ReceiveSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var actionQueue = WebSocketManager.ActionQueue;
        if (actionQueue.Count == 0) return;

        var config = SystemAPI.GetSingleton<MapConfig>();

        Debug.Log($"[Receive] Action 수신: {actionQueue.Count}개");

        // Queue → HashMap
        var actionMap = new NativeHashMap<int, int>(16, Allocator.TempJob);
        while (actionQueue.TryDequeue(out var a))
            actionMap.TryAdd(a.UnitId, a.Action);

        state.Dependency = new ApplyActionJob
        {
            ActionMap = actionMap,
            MinCol = 0,
            MaxCol = config.Height,
            MinRow = 0,
            MaxRow = config.Width
        }
            .ScheduleParallel(state.Dependency);

        actionMap.Dispose(state.Dependency);
    }
}

[BurstCompile]
partial struct ApplyActionJob : IJobEntity
{
    [ReadOnly] public NativeHashMap<int, int> ActionMap;
    public int MinCol, MaxCol, MinRow, MaxRow;

    void Execute(ref MoveTargetComponent moveTarget, in UnitComponent unit, in LocalTransform transform)
    {
        if (!ActionMap.TryGetValue(unit.Id, out int action)) return;

        Debug.Log($"[Action] UnitId={unit.Id} action={action}");

        float yaw = action * 60f;
        var targetCell = HexMetrics.WorldToOffsetWithYaw(transform.Position, yaw);

        // 범위 벗어나면 이동 무시
        if (!HexMetrics.IsValidCell(targetCell, MinCol, MaxCol, MinRow, MaxRow)) return;

        Debug.Log($"[Action] UnitId={unit.Id} action={action} => {HexMetrics.OffsetToWorld(targetCell)}");

        moveTarget.Target = HexMetrics.OffsetToWorld(targetCell);
    }
}