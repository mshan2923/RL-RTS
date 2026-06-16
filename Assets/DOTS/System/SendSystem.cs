using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public partial struct SendSystem : ISystem
{
    private int _step;
    private float _prevCaptureRatio;

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
            .WithAll<LocalTransform, UnitComponent, DetectWallNormalize, MoveTargetComponent, TargetInfo>().Build(ref state);
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
        float captureGain = captureRatio - _prevCaptureRatio; // 이번 프레임 점령 증가량

        Debug.Log($"Capture : {captureGain}");

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
        var targetInfo = _unitQuery.ToComponentDataArray<TargetInfo>(Allocator.Temp);
        var entities = _unitQuery.ToEntityArray(Allocator.Temp);


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

            float3 toTarget = targetInfo[i].Position - transforms[i].Position;
            float targetDist = math.length(toTarget) / maxDist;
            float targetDir = math.degrees(math.atan2(toTarget.x, toTarget.z)) / 360f;

            float currentDistToBase = math.length(toBase);
            float reward = 0f;

            // Reward for moving towards the capture target
            if (targetInfo[i].IsActive)
            {
                // 목표 타일에 가까워질수록 보상 (더 높은 가중치로 개별 탐사 유도)
                reward += (moveTarget[i].PrevTargetDist - targetDist) * 120.0f;
            }
            else
            {
                // 목표 타일이 없을 경우, 기지에 가까워질수록 보상 (상대적으로 낮은 가중치)
                reward += (moveTarget[i].PrevBaseDist - currentDistToBase) * 20.0f;
            }

            // 벽/맵 경계 페널티: 선택한 방향의 벽 센서(n0~n5)가 너무 가까우면 페널티
            float[] wallSensors = { unitDetectWall[i].n0, unitDetectWall[i].n1, unitDetectWall[i].n2,
                                    unitDetectWall[i].n3, unitDetectWall[i].n4, unitDetectWall[i].n5 };

            int lastAction = moveTarget[i].command;
            if (lastAction >= 0 && lastAction < 6)
            {
                // 해당 방향의 센서값이 0.2 미만(거의 앞이 벽)일 때 페널티를 줄여서 덜 튕기도록
                if (wallSensors[lastAction] < 0.2f) reward -= 1.0f;
            }
            if (unitDetectWall[i].isWall) reward -= 0.5f; // 현재 벽 위에 있어도 페널티를 줄임
            else reward += 0.005f; // 벽이 아닌 곳에 있으면 아주 작은 보상 (움직임 장려)

            // 타일 점령 증가량에 대한 보상 (전역적이지만, 학습에 도움)
            reward += captureGain * 100.0f; // 미탐사 지역 탐사 시 보상을 줄여 개별 유닛의 목표 추구 유도

            // 기지 도착 시 큰 보상 (여전히 중요한 목표일 경우)
            if (currentDistToBase < 1.0f)
                reward += 10.0f;

            // 시간 페널티
            reward -= 0.01f;

            // 다음 스텝을 위해 현재 거리 정보 업데이트
            var target = moveTarget[i];
            target.PrevBaseDist = currentDistToBase;
            target.PrevTargetDist = targetDist;
            state.EntityManager.SetComponentData(entities[i], target);


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
                N5 = unitDetectWall[i].n5,

                TargetActive = targetInfo[i].IsActive ? 1f : 0f,
                TargetDist = math.clamp(targetDist, 0, 1f),
                TargetDir = math.clamp(targetDir, 0, 1f)
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
        _prevCaptureRatio = captureRatio;

        tileArray.Dispose();
        tileMap.Dispose();
        transforms.Dispose();
        units.Dispose();
        tiles.Dispose();
        unitDetectWall.Dispose();
        moveTarget.Dispose();
        targetInfo.Dispose();
        entities.Dispose();
    }
}