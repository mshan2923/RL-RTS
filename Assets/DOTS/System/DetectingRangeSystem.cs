using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

struct HexTileEntity
{
    public HexTile Tile;
    public Entity Entity;
}
partial struct DetectingRangeSystem : ISystem
{
    EntityQuery _unitQuery;
    EntityQuery _tileQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _unitQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<LocalTransform, UnitComponent>().Build(ref state);

        _tileQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<HexTile>().Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {


        //유닛마다 일정거리만큼 인접해 있는 셀 방향으로 벽인지 확인 , 가장 가까운 점령가능 셀은 따로 계산
        // 거리 = 칸수 * 0.8666f 
        if (!SystemAPI.TryGetSingleton<RLConfig>(out var config)) return;

        var tiles = _tileQuery.ToComponentDataArray<HexTile>(Allocator.TempJob);
        var entities = _tileQuery.ToEntityArray(Allocator.TempJob);
        var tileMap = new NativeParallelHashMap<int2, HexTileEntity>(tiles.Length, Allocator.TempJob);

        state.Dependency = new MakeMapJob
        {
            Tiles = tiles.AsReadOnly(),
            Entities = entities.AsReadOnly(),
            TileMap = tileMap.AsParallelWriter()
        }.Schedule(tiles.Length, JobsUtility.MaxJobThreadCount, state.Dependency);


        state.Dependency = new DetectWallJob
        {
            range = config.DetectionRange,
            TileMap = tileMap.AsReadOnly()
        }.ScheduleParallel(state.Dependency);


        tiles.Dispose(state.Dependency);
        entities.Dispose(state.Dependency);
        tileMap.Dispose(state.Dependency);
    }


    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    struct MakeMapJob : IJobParallelFor
    {
        public NativeArray<HexTile>.ReadOnly Tiles;
        public NativeArray<Entity>.ReadOnly Entities;

        public NativeParallelHashMap<int2, HexTileEntity>.ParallelWriter TileMap;

        public void Execute(int index)
        {
            TileMap.TryAdd(new int2(Tiles[index].X, Tiles[index].Z), new HexTileEntity { Tile = Tiles[index], Entity = Entities[index] });
        }
    }
    [BurstCompile]
    public partial struct DetectWallJob : IJobEntity
    {
        public float range;

        public NativeParallelHashMap<int2, HexTileEntity>.ReadOnly TileMap;
        public void Execute([EntityIndexInQuery] int index, Entity entity, in UnitComponent unit, in LocalTransform transform, ref DetectWallNormalize detectWall)
        {
            var PosInt = HexMetrics.WorldToOffset(transform.Position);

            int RangeAmount = (int)math.ceil(range / 0.8666f);

            if (TileMap.TryGetValue(PosInt, out var originTile))
            {
                detectWall.isWall = originTile.Tile.OwnerID == GroupType.Wall;
            }

            for (int i = 0; i < 6; i++)
            {
                bool foundWall = false;
                var currentTemp = PosInt; // 매 방향마다 다시 원점에서 시작해야 함

                for (int j = 0; j < RangeAmount; j++)
                {
                    currentTemp = HexMetrics.GetNeighborOffset(currentTemp, i * 60f);

                    if (TileMap.TryGetValue(currentTemp, out var tileEntity))
                    {
                        if (tileEntity.Tile.OwnerID == GroupType.Wall)
                        {
                            EditDetectWall(i, ref detectWall, math.clamp(math.distance(transform.Position, HexMetrics.OffsetToWorld(currentTemp)) / range, 0f, 1f));
                            foundWall = true;
                            break; // 벽 찾았으니 종료
                        }
                    }
                }

                if (!foundWall)
                {
                    EditDetectWall(i, ref detectWall, 1f);
                }
            }
        }
        public void EditDetectWall(int i, ref DetectWallNormalize detectWall, float value)
        {
            switch (i)
            {
                case 0:
                    detectWall.n0 = value;
                    break;
                case 1:
                    detectWall.n1 = value;
                    break;
                case 2:
                    detectWall.n2 = value;
                    break;
                case 3:
                    detectWall.n3 = value;
                    break;
                case 4:
                    detectWall.n4 = value;
                    break;
                case 5:
                    detectWall.n5 = value;
                    break;
            }
        }
    }
}
