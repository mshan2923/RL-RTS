using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public partial struct SendSystem : ISystem
{
    private int _step;

    public void OnUpdate(ref SystemState state)
    {
        _step++;

        // 점령률 + 타일 맵 수집
        int total = 0, captured = 0;
        int justCapturedCount = 0;

        // col,row → 점령여부 맵
        var tileMap = new NativeHashMap<int2, GroupType>(256, Allocator.Temp);

        foreach (var tile in SystemAPI.Query<RefRO<HexTile>>())
        {
            total++;
            if (tile.ValueRO.OwnerID == GroupType.Ally) captured++;
            if (tile.ValueRO.JustCaptured) justCapturedCount++;
            tileMap.TryAdd(new int2(tile.ValueRO.X, tile.ValueRO.Z), tile.ValueRO.OwnerID);
        }

        float captureRatio = total > 0 ? (float)captured / total : 0f;
        bool done = captureRatio >= 1.0f || _step >= 200;
        if (done) _step = 0;

        var stateQueue = WebSocketManager.StateQueue;

        foreach (var (transform, unit, moveTarget) in
            SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitComponent>, RefRO<MoveTargetComponent>>())
        {
            var cell = HexMetrics.WorldToOffset(transform.ValueRO.Position);
            var euler = math.EulerXYZ(transform.ValueRO.Rotation.value);
            int yaw = HexMetrics.WorldYawToIndex(math.degrees(euler.y));

            // 보상 계산
            float reward = 0f;
            reward += justCapturedCount * 1.0f;   // 새 점령
            reward -= 0.01f;                       // 시간 패널티

            // 목표 셀 보상/패널티
            var targetCell = HexMetrics.WorldToOffset(moveTarget.ValueRO.Target);
            if (!HexMetrics.IsValidCell(targetCell, 0, HexMetrics.MapWidth - 1, 0, HexMetrics.MapHeight - 1))
                reward -= 0.5f;  // 벽
            else if (tileMap.TryGetValue(new int2(targetCell.x, targetCell.y), out var owner)
                     && owner == GroupType.Ally)
                reward -= 0.3f;  // 재방문

            // 주변 6셀
            float n0 = 0, n1 = 0, n2 = 0, n3 = 0, n4 = 0, n5 = 0;
            float[] neighbors = { n0, n1, n2, n3, n4, n5 };
            for (int i = 0; i < 6; i++)
            {
                var nc = HexMetrics.GetNeighborOffset(cell, i);
                if (!HexMetrics.IsValidCell(nc, 0, HexMetrics.MapWidth - 1, 0, HexMetrics.MapHeight - 1))
                    neighbors[i] = -1f;
                else if (tileMap.TryGetValue(new int2(nc.x, nc.y), out var o) && o == GroupType.Ally)
                    neighbors[i] = 1f;
                else
                    neighbors[i] = 0f;
            }

            stateQueue.Enqueue(new WebSocketManager.StateData
            {
                UnitId = unit.ValueRO.Id,
                Col = cell.x,
                Row = cell.y,
                Yaw = yaw,
                Reward = reward,
                Done = done,
                CaptureRatio = captureRatio,
                N0 = neighbors[0],
                N1 = neighbors[1],
                N2 = neighbors[2],
                N3 = neighbors[3],
                N4 = neighbors[4],
                N5 = neighbors[5]
            });
        }

        tileMap.Dispose();

        // JustCaptured 리셋
        foreach (var tile in SystemAPI.Query<RefRW<HexTile>>())
            tile.ValueRW.JustCaptured = false;
    }
}