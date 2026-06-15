using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(MoveToTarget))]
public partial struct ReceiveSystem : ISystem
{
    private EntityQuery _unitQuery;

    public void OnCreate(ref SystemState state)
    {
        _unitQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<LocalTransform, UnitComponent>()
            .Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<RLConfig>(out var config)) return;
        if (!SystemAPI.TryGetSingleton<MapConfig>(out var MapConfig)) return;
        if (!SystemAPI.TryGetSingleton<EpisodeState>(out var episodeState)) return;
        if (!SystemAPI.TryGetSingleton<BaseComponent>(out var basement)) return;

        if (episodeState.SkipAction)
        {
            // 리셋 프레임: 액션 적용 스킵, 플래그만 해제
            episodeState.SkipAction = false;
            SystemAPI.SetSingleton(episodeState);
            var manager0 = SystemAPI.ManagedAPI.GetSingleton<WebSocketManagerComponent>();
            manager0.ActionQueue.Clear(); // 밀린 액션 비우기
            return;
        }

        var manager = SystemAPI.ManagedAPI.GetSingleton<WebSocketManagerComponent>();
        if (manager.ActionQueue.Count == 0) return;

        // 유닛 수 먼저 세기
        int unitCount = _unitQuery.CalculateEntityCount();




        var actionMap = new NativeHashMap<int, int>(unitCount, Allocator.TempJob);

        while (manager.ActionQueue.TryDequeue(out var a))
            actionMap.TryAdd(a.UnitId, a.Action);

        state.Dependency = new ApplyActionJob
        {

            ActionMap = actionMap,
            Col = MapConfig.Height,
            Row = MapConfig.Width,
            basement = basement.Position
        }
            .ScheduleParallel(state.Dependency);

        actionMap.Dispose(state.Dependency);
    }
}

[BurstCompile]
partial struct ApplyActionJob : IJobEntity
{
    [ReadOnly] public NativeHashMap<int, int> ActionMap;
    public int Col;
    public int Row;
    public float3 basement;

    void Execute(ref MoveTargetComponent moveTarget, in UnitComponent unit, in LocalTransform transform)
    {
        if (!ActionMap.TryGetValue(unit.Id, out int action)) return;

        float yaw = action * 60f;
        var targetCell = HexMetrics.WorldToOffsetWithYaw(transform.Position, yaw);

        if (!HexMetrics.IsValidCell(targetCell, 0, Col - 1, 0, Row - 1)) return;


        moveTarget.Target = HexMetrics.OffsetToWorld(targetCell);
        moveTarget.PrevBaseDist = math.distance(transform.Position, basement);
    }
}