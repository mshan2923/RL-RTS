using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public partial struct SendSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        int total = 0, captured = 0;
        foreach (var tile in SystemAPI.Query<RefRO<HexTile>>())
        {
            total++;
            if (tile.ValueRO.OwnerID == GroupType.Ally) captured++;
        }
        float captureRatio = total > 0 ? (float)captured / total : 0f;
        bool done = captureRatio >= 1.0f;

        // JustCaptured 셀 수
        int justCapturedCount = 0;
        foreach (var tile in SystemAPI.Query<RefRO<HexTile>>())
            if (tile.ValueRO.JustCaptured) justCapturedCount++;

        float reward = justCapturedCount * 1.0f - 0.01f;

        var stateQueue = WebSocketManager.StateQueue;

        foreach (var (transform, unit) in
            SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitComponent>>())
        {
            var cell = HexMetrics.WorldToOffset(transform.ValueRO.Position);

            // Quaternion → Yaw
            var euler = math.EulerXYZ(transform.ValueRO.Rotation.value);
            float yawDeg = math.degrees(euler.y);
            int yaw = HexMetrics.WorldYawToIndex(yawDeg);

            stateQueue.Enqueue(new WebSocketManager.StateData
            {
                UnitId = unit.ValueRO.Id,
                Col = cell.x,
                Row = cell.y,
                Yaw = yaw,
                Reward = reward,
                Done = done,
                CaptureRatio = captureRatio
            });
        }

        // JustCaptured 리셋
        foreach (var tile in SystemAPI.Query<RefRW<HexTile>>())
            tile.ValueRW.JustCaptured = false;
    }
}