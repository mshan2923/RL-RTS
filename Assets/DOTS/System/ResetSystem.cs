using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SendSystem))]
[UpdateBefore(typeof(ReceiveSystem))]
public partial struct ResetSystem : ISystem
{
    private EntityQuery _tileQuery;
    private EntityQuery _unitQuery;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EpisodeState>();
        _tileQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<HexTile>().Build(ref state);
        _unitQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<LocalTransform, UnitComponent, DetectWallNormalize, MoveTargetComponent>().Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<EpisodeState>(out var episodeState)) return;
        if (!SystemAPI.TryGetSingleton<MapConfig>(out var config)) return;
        if (!episodeState.NeedsReset) return;

        // 1. 타일 OwnerID 초기화 + JustCaptured 리셋
        var tiles = _tileQuery.ToComponentDataArray<HexTile>(Allocator.Temp);
        for (int i = 0; i < tiles.Length; i++)
        {
            var t = tiles[i];
            t.OwnerID = tiles[i].OwnerID == GroupType.Wall ? GroupType.Wall : GroupType.None; // HexTile에 InitialOwnerID 필요
            t.JustCaptured = false;
            tiles[i] = t;
        }
        _tileQuery.CopyFromComponentDataArray(tiles);
        tiles.Dispose();

        // 2. 유닛 랜덤 스폰 (사용자 구현 호출 지점)
        // SpawnRandomUnits(ref state);
        state.Dependency = new ResetUnitJob
        {
            Width = config.Width,
            Height = config.Height,
            Radius = config.Radius,
            seed = 14341u + (uint)(SystemAPI.Time.ElapsedTime * 1000f)
        }.ScheduleParallel(state.Dependency);

        episodeState.NeedsReset = false;
        episodeState.EpisodeCount++;
        SystemAPI.SetSingleton(episodeState);

        UnityEngine.Debug.Log($"[Reset] Episode {episodeState.EpisodeCount} 시작");
    }

    partial struct ResetUnitJob : IJobEntity
    {
        public int Width;
        public int Height;
        public float Radius;
        public uint seed;

        public void Execute([EntityIndexInQuery] int index, Entity entity, ref LocalTransform transform, ref MoveTargetComponent moveTarget)
        {
            var random = new Unity.Mathematics.Random(seed + (uint)index);

            var pos = random.NextFloat3(new float3(1, 0, 1), new float3(Width - 1, 0, Height - 1) * Radius * 0.86666f);

            transform.Position = pos;
            moveTarget.Target = pos;
            moveTarget.PrevBaseDist = 0f;
            moveTarget.PrevTargetDist = 0f; // PrevTargetDist 초기화
        }
    }
}