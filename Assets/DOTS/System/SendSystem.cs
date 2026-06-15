using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public partial struct SendSystem : ISystem
{
    private int _step;
    private EntityQuery _tileQuery;
    private EntityQuery _baseQuery;
    private EntityQuery _unitQuery;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WebSocketManagerComponent>();
        state.RequireForUpdate<RLConfig>();

        _tileQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<HexTile>().Build(ref state);
        _baseQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<BaseComponent>().Build(ref state);
        _unitQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<LocalTransform, UnitComponent, DetectWallNormalize, MoveTargetComponent>().Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<RLConfig>(out var config)) return;
        if (config.IsInferenceMode) return;

        var manager = SystemAPI.ManagedAPI.GetSingleton<WebSocketManagerComponent>();
        if (!SystemAPI.TryGetSingleton<MapConfig>(out var MapConfig)) return;
        if (!SystemAPI.TryGetSingleton<EpisodeState>(out var episodeState)) return;
        if (!SystemAPI.TryGetSingleton<BaseComponent>(out var basement)) return;

        _step++;

        // 타일 수집
        var tileArray = _tileQuery.ToComponentDataArray<HexTile>(Allocator.Temp);
        var tileMap = new NativeHashMap<int2, GroupType>(tileArray.Length, Allocator.Temp);

        int total = 0, captured = 0, justCaptured = 0;
        foreach (var tile in tileArray)
        {
            total++;
            if (tile.OwnerID == GroupType.Ally) captured++;
            if (tile.JustCaptured) justCaptured++;
            tileMap.TryAdd(new int2(tile.X, tile.Z), tile.OwnerID);
        }
        float captureRatio = total > 0 ? (float)captured / total : 0f;

        // 기지 위치
        float3 basePos = basement.Position;
        float maxDist = math.sqrt(MapConfig.Width * MapConfig.Width +
                                    MapConfig.Height * MapConfig.Height);
        foreach (var b in _baseQuery.ToComponentDataArray<BaseComponent>(Allocator.Temp))
        {
            if (b.Team == GroupType.Ally) { basePos = b.Position; break; }
        }

        // 유닛 수집
        var transforms = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        var units = _unitQuery.ToComponentDataArray<UnitComponent>(Allocator.Temp);
        var unitDetectWall = _unitQuery.ToComponentDataArray<DetectWallNormalize>(Allocator.Temp);
        var moveTarget = _unitQuery.ToComponentDataArray<MoveTargetComponent>(Allocator.Temp);

        // ── Pass 1: 도착 판정 (전체 유닛 기준 done 결정) ──
        bool arrived = false;
        for (int i = 0; i < units.Length; i++)
        {
            float3 toBase = basePos - transforms[i].Position;
            if (math.length(toBase) < 1.0f)
            {
                arrived = true;
                break;
            }
        }

        bool timeOut = _step >= config.MaxSteps;
        bool done = timeOut || arrived;

        if (done)
        {
            _step = 0;
            episodeState.NeedsReset = true;
            episodeState.SkipAction = true;
            SystemAPI.SetSingleton(episodeState);
        }

        // ── Pass 2: reward 계산 + Enqueue ──
        for (int i = 0; i < units.Length; i++)
        {
            var cell = HexMetrics.WorldToOffset(transforms[i].Position);
            var euler = math.EulerXYZ(transforms[i].Rotation.value);
            int yaw = HexMetrics.WorldYawToIndex(math.degrees(euler.y));
            float3 toBase = basePos - transforms[i].Position;
            float baseDist = math.length(toBase) / maxDist; // 정규화
            float baseDir = math.degrees(math.atan2(toBase.x, toBase.z)) / 360f; // 정규화

            #region Reward
            float currentDist = math.length(toBase);

            // 1. 방향 일치 (기지를 바라보는가?)
            float3 forward = math.forward(transforms[i].Rotation);
            float alignment = math.dot(forward, math.normalize(toBase));

            // 2. 거리 페널티 (정규화된 baseDist 사용)
            // float reward = (alignment * 1.0f) - (baseDist * 1.0f);
            float reward = (moveTarget[i].PrevBaseDist - currentDist) * 50.0f;

            if (i == 0)
                Debug.Log($"{moveTarget[i].PrevBaseDist} - {currentDist} = {reward / 50f}");
            #endregion

            // 도착 시 큰 보상
            if (currentDist < 1.0f)
                reward += 10.0f;

            // 시간 페널티
            reward -= 0.01f;

            manager.StateQueue.Enqueue(new WebSocketManager.StateData
            {
                UnitId = units[i].Id,
                Col = cell.x,
                Row = cell.y,
                Yaw = yaw,
                Reward = reward,
                Done = done,
                CaptureRatio = captureRatio,
                BaseDist = baseDist,
                BaseDir = baseDir,
                N0 = unitDetectWall[i].n0,
                N1 = unitDetectWall[i].n1,
                N2 = unitDetectWall[i].n2,
                N3 = unitDetectWall[i].n3,
                N4 = unitDetectWall[i].n4,
                N5 = unitDetectWall[i].n5
            });
        }

        // JustCaptured 리셋
        var tiles = _tileQuery.ToComponentDataArray<HexTile>(Allocator.Temp);
        for (int i = 0; i < tiles.Length; i++)
        {
            var t = tiles[i];
            t.JustCaptured = false;
            tiles[i] = t;
        }
        _tileQuery.CopyFromComponentDataArray(tiles);

        tileArray.Dispose();
        tileMap.Dispose();
        transforms.Dispose();
        units.Dispose();
        tiles.Dispose();
        unitDetectWall.Dispose();
        moveTarget.Dispose();
    }
}