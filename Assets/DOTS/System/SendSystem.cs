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

        // 유닛 수집 (리셋 처리를 위해 위로 이동)
        var transforms = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        var units = _unitQuery.ToComponentDataArray<UnitComponent>(Allocator.Temp);
        var unitDetectWall = _unitQuery.ToComponentDataArray<DetectWallNormalize>(Allocator.Temp);
        var moveTarget = _unitQuery.ToComponentDataArray<MoveTargetComponent>(Allocator.Temp);
        var targetInfo = _unitQuery.ToComponentDataArray<TargetInfo>(Allocator.Temp);
        var entities = _unitQuery.ToEntityArray(Allocator.Temp);

        float maxDist = math.sqrt(MapConfig.Width * MapConfig.Width + MapConfig.Height * MapConfig.Height);

        // [수정] 유니티 맵이 리셋되는 타이밍에 유닛의 내부 거리 데이터도 강제 싱크
        if (episodeState.NeedsReset)
        {
            _step = 0;
            _prevCaptureRatio = 0f;

            // 에피소드 시작 시점의 실제 거리를 Prev 값에 강제로 꽂아넣음 (억까 패널티 방지)
            float3 startBasePos = basement.Position;
            foreach (var b in _baseQuery.ToComponentDataArray<BaseComponent>(Allocator.Temp))
            {
                if (b.Team == GroupType.Ally) { startBasePos = b.Position; break; }
            }

            for (int i = 0; i < units.Length; i++)
            {
                var t = moveTarget[i];
                t.PrevBaseDist = math.length(startBasePos - transforms[i].Position) / maxDist;
                t.PrevTargetDist = math.length(targetInfo[i].Position - transforms[i].Position) / maxDist;
                state.EntityManager.SetComponentData(entities[i], t);
            }

            // 데이터 변경되었으니 원본 배열 다시 갱신
            moveTarget.Dispose();
            moveTarget = _unitQuery.ToComponentDataArray<MoveTargetComponent>(Allocator.Temp);
        }

        _step++;

        // 타일 수집 (미사용하는 NativeHashMap 제거로 경량화)
        var tileArray = _tileQuery.ToComponentDataArray<HexTile>(Allocator.Temp);
        int total = 0, captured = 0;
        foreach (var tile in tileArray)
        {
            total++;
            if (tile.OwnerID == GroupType.Ally) captured++;
        }
        float captureRatio = total > 0 ? (float)captured / total : 0f;
        float captureGain = _step == 1 ? 0f : captureRatio - _prevCaptureRatio;

        // 기지 위치 최신화
        float3 basePos = basement.Position;
        foreach (var b in _baseQuery.ToComponentDataArray<BaseComponent>(Allocator.Temp))
        {
            if (b.Team == GroupType.Ally) { basePos = b.Position; break; }
        }

        // ── Pass 1: 도착 판정 ──
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
            _prevCaptureRatio = 0f;
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
            float baseDist = math.length(toBase) / maxDist;

            float3 toTarget = targetInfo[i].Position - transforms[i].Position;
            float targetDist = math.length(toTarget) / maxDist;

            float rawTargetDir = math.degrees(math.atan2(toTarget.x, toTarget.z));
            float targetDir = (rawTargetDir + 180f) / 360f;

            float rawBaseDir = math.degrees(math.atan2(toBase.x, toBase.z));
            float baseDir = (rawBaseDir + 180f) / 360f;

            float rewardExploration = 0f;
            float rewardReturn = 0f;
            float rewardSurvival = 0f;

            float targetDelta = moveTarget[i].PrevTargetDist - targetDist;

            // 첫 프레임(_step == 1)에는 강제 순간이동 여파가 남아있을 수 있으므로 델타 보상을 0으로 마스킹
            if (_step > 1)
            {
                if (targetInfo[i].IsActive)
                {
                    if (targetDelta > 0) rewardExploration = targetDelta * 150.0f;
                    else rewardExploration = targetDelta * 200.0f;
                }
                else
                {
                    float baseDelta = moveTarget[i].PrevBaseDist - baseDist;
                    if (baseDelta > 0) rewardReturn = baseDelta * 40.0f;
                    else rewardReturn = baseDelta * 60.0f;
                }
            }

            // 벽/맵 경계 페널티
            float[] wallSensors = { unitDetectWall[i].n0, unitDetectWall[i].n1, unitDetectWall[i].n2,
                                    unitDetectWall[i].n3, unitDetectWall[i].n4, unitDetectWall[i].n5 };

            int lastAction = moveTarget[i].command;
            if (lastAction >= 0 && lastAction < 6)
            {
                rewardSurvival -= (1.0f - wallSensors[lastAction]) / 0.5f * 0.8f;
            }

            if (unitDetectWall[i].isWall) rewardSurvival -= 1.0f;
            else rewardSurvival += 0.005f;

            float globalReward = captureGain * 40.0f;
            float reward = rewardExploration + rewardReturn + rewardSurvival + globalReward;

            if (math.length(toBase) < 1.0f) reward += 10.0f;
            reward -= 0.02f;

            // 변수 업데이트
            var target = moveTarget[i];
            target.PrevBaseDist = baseDist;
            target.PrevTargetDist = targetDist;
            state.EntityManager.SetComponentData(entities[i], target);

            manager.StateQueue.Enqueue(new WebSocketManager.StateData
            {
                // ... (Enqueue 데이터 구조는 기존과 동일) ...
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
        transforms.Dispose();
        units.Dispose();
        tiles.Dispose();
        unitDetectWall.Dispose();
        moveTarget.Dispose();
        targetInfo.Dispose();
        entities.Dispose();
    }
}