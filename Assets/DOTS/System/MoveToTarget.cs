using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

partial struct MoveToTarget : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var moveLength = 5f * SystemAPI.Time.DeltaTime;
        foreach (var (move, trans, entity) in SystemAPI.Query<RefRO<MoveTargetComponent>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            if (math.distance(trans.ValueRO.Position, move.ValueRO.Target) < moveLength)
            {
                trans.ValueRW.Position = move.ValueRO.Target;
            }
            else
            {
                trans.ValueRW.Position += math.normalize(move.ValueRO.Target - trans.ValueRO.Position) * moveLength;
            }
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }
}
