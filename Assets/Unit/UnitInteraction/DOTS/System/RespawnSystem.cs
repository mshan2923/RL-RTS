using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

partial struct RespawnSystem : ISystem
{
    EntityQuery respawnQuery;
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using var build = new EntityQueryBuilder(Allocator.Temp);
        build.WithAll<RLParmCompoenent>();
        respawnQuery = build.Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {

        uint seed = (uint)System.Guid.NewGuid().GetHashCode();
        var random = new Random(seed);

        using var unitParamArray = respawnQuery.ToComponentDataArray<RLParmCompoenent>(Allocator.TempJob);

        foreach(var rLParm in unitParamArray)
        {
            var size = new float3(rLParm.Width, 0 , rLParm.Height);
            var offset = new float3(rLParm.SpawnRandomOffset, 0 , rLParm.SpawnRandomOffset);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

            foreach(var (_, transform, moveTo, entity) in SystemAPI.Query<RefRO<UnitRespawnTag>, RefRW<LocalTransform >, RefRW<MoveTargetComponent>>().WithEntityAccess())
            {
                    var pos = random.NextFloat3(-size + offset, size - offset) + size * 0.5f;

                    transform.ValueRW.Position = pos;
                    moveTo.ValueRW.MoveTo = pos;

                    ecb.SetComponentEnabled<UnitRespawnTag>(entity, false);
            }
        }


    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }
}
