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
        foreach (var (move, trans, entity) in SystemAPI.Query<RefRW<MoveTargetComponent>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            move.ValueRW.PrevPosition = trans.ValueRO.Position;

            if (math.distance(trans.ValueRO.Position, move.ValueRO.MoveTo) < moveLength)
            {
                trans.ValueRW.Position = move.ValueRO.MoveTo;
            }
            else
            {
                trans.ValueRW.Position += math.normalize(move.ValueRO.MoveTo - trans.ValueRO.Position) * moveLength;
                trans.ValueRW.Rotation = quaternion.LookRotationSafe(
                    math.normalize(move.ValueRO.MoveTo - trans.ValueRO.Position), math.up());
            }
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }
}
