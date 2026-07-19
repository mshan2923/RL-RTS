using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

//[UpdateAfter(typeof())]
// partial struct ObstacleSystem : ISystem
// {
//     EntityQuery query;
//     EntityQuery TileQuery;

//     public NativeParallelHashMap<int2, HexTile> data;

//     [BurstCompile]
//     public void OnCreate(ref SystemState state)
//     {
//         using var build = new EntityQueryBuilder(Allocator.Temp);
//         build.WithAll<ObstacleTag>();
//         query = build.Build(ref state);

//         using var tilebuild = new EntityQueryBuilder(Allocator.Temp);
//         tilebuild.WithAll<HexTile>();
//         TileQuery = tilebuild.Build(ref state);

//     }

//     [BurstCompile]
//     public void OnUpdate(ref SystemState state)
//     {
        
//         var offsets = new NativeParallelHashSet<int2>(query.CalculateEntityCount(), Allocator.TempJob);
//         var tiles = new NativeArray<GroupType>(TileQuery.CalculateEntityCount(), Allocator.TempJob);

//         var count = TileQuery.CalculateEntityCount();

//         if (count != 0 && data.IsEmpty)
//             data = new NativeParallelHashMap<int2, HexTile>(count, Allocator.Persistent);


//         state.Dependency = new Convert
//         {
//             offsets = offsets.AsParallelWriter()
//         }.ScheduleParallel(state.Dependency);

//         state.Dependency = new Combine
//         {
//             offsets = offsets.AsReadOnly(),
//             types = tiles,

//             IsCreated = data.IsCreated,
//             data = data.AsParallelWriter()
//         }.ScheduleParallel(TileQuery, state.Dependency);

//         state.Dependency = new Apply
//         {
//             types = tiles.AsReadOnly()
//         }.ScheduleParallel(TileQuery, state.Dependency);
        

//         offsets.Dispose(state.Dependency);
//         tiles.Dispose(state.Dependency);
//     }

//     [BurstCompile]
//     public void OnDestroy(ref SystemState state)
//     {
//         data.Dispose();
//     }

//     partial struct Convert : IJobEntity
//     {
//         public NativeParallelHashSet<int2>.ParallelWriter offsets;
//         public void Execute([EntityIndexInQuery] int index, in ObstacleTag tag, in LocalTransform transform)
//         {
//             offsets.Add(HexMetrics.WorldToOffset(transform.Position));
//         }
//     }

//     partial struct Combine : IJobEntity
//     {
//         [ReadOnly] public NativeParallelHashSet<int2>.ReadOnly offsets;
//         public NativeArray<GroupType> types;

//         public bool IsCreated;
//         public NativeParallelHashMap<int2, HexTile>.ParallelWriter data;

//         public void Execute([EntityIndexInQuery] int index, in HexTile tile)
//         {
//             int2 offset = new int2 (tile.X, tile.Z);

//             if (tile.IsBorder)
//                 types[index] = GroupType.Wall;
//             else
//                 types[index] = offsets.Contains(offset) ? GroupType.Wall : GroupType.None;


//             if (IsCreated)
//                 data.TryAdd(offset, tile);
//         }
//     }

//     partial struct Apply : IJobEntity
//     {
//         public NativeArray<GroupType>.ReadOnly types;

//         public void Execute([EntityIndexInQuery] int index, ref HexTile tile)
//         {
//             tile.OwnerID = types[index];
//         }
//     }
// }
public partial class ObstacleSystem : SystemBase
{
    EntityQuery query;
    EntityQuery TileQuery;

    // 데이터 공개 (다른 시스템에서 접근 가능)
    public NativeParallelHashMap<int2, HexTile> data;

    protected override void OnCreate()
    {
        query = SystemAPI.QueryBuilder().WithAll<ObstacleTag>().Build();
        TileQuery = SystemAPI.QueryBuilder().WithAll<HexTile>().Build();
        
        // 데이터 초기화 (초기에 0으로)
        data = new NativeParallelHashMap<int2, HexTile>(1024, Allocator.Persistent);
    }

    protected override void OnUpdate()
    {
        var offsets = new NativeParallelHashSet<int2>(query.CalculateEntityCount(), Allocator.TempJob);
        var tiles = new NativeArray<GroupType>(TileQuery.CalculateEntityCount(), Allocator.TempJob);

        var count = TileQuery.CalculateEntityCount();

        if (count != 0 && data.IsEmpty)
            data = new NativeParallelHashMap<int2, HexTile>(count, Allocator.Persistent);


        Dependency = new Convert
        {
            offsets = offsets.AsParallelWriter()
        }.ScheduleParallel(Dependency);

        Dependency = new Combine
        {
            offsets = offsets.AsReadOnly(),
            types = tiles,

            IsCreated = data.IsCreated,
            data = data.AsParallelWriter()
        }.ScheduleParallel(TileQuery, Dependency);

        Dependency = new Apply
        {
            types = tiles.AsReadOnly()
        }.ScheduleParallel(TileQuery, Dependency);
        

        offsets.Dispose(Dependency);
        tiles.Dispose(Dependency);
    }

    protected override void OnDestroy()
    {
        if(data.IsCreated) data.Dispose();
    }

        partial struct Convert : IJobEntity
    {
        public NativeParallelHashSet<int2>.ParallelWriter offsets;
        public void Execute([EntityIndexInQuery] int index, in ObstacleTag tag, in LocalTransform transform)
        {
            offsets.Add(HexMetrics.WorldToOffset(transform.Position));
        }
    }

    partial struct Combine : IJobEntity
    {
        [ReadOnly] public NativeParallelHashSet<int2>.ReadOnly offsets;
        public NativeArray<GroupType> types;

        public bool IsCreated;
        public NativeParallelHashMap<int2, HexTile>.ParallelWriter data;

        public void Execute([EntityIndexInQuery] int index, in HexTile tile)
        {
            int2 offset = new int2 (tile.X, tile.Z);

            if (tile.IsBorder)
                types[index] = GroupType.Wall;
            else
                types[index] = offsets.Contains(offset) ? GroupType.Wall : GroupType.None;


            if (IsCreated)
                data.TryAdd(offset, tile);
        }
    }

    partial struct Apply : IJobEntity
    {
        public NativeArray<GroupType>.ReadOnly types;

        public void Execute([EntityIndexInQuery] int index, ref HexTile tile)
        {
            tile.OwnerID = types[index];
        }
    }
}