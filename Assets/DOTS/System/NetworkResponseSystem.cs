using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public partial struct NetworkResponseSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        // NetworkResponse를 Id → (NewX, NewZ) 로 매핑
        var responseMap = new NativeHashMap<int, float2>(16, Allocator.TempJob);
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (resp, entity) in SystemAPI.Query<RefRO<NetworkResponseComponent>>().WithEntityAccess())
        {
            responseMap.Add(resp.ValueRO.UnitId, new float2(resp.ValueRO.NewX, resp.ValueRO.NewZ));
            ecb.DestroyEntity(entity);
        }

        state.Dependency = new ApplyMoveTargetJob { ResponseMap = responseMap }
            .ScheduleParallel(state.Dependency);

        responseMap.Dispose(state.Dependency);


    }
}

[BurstCompile]
partial struct ApplyMoveTargetJob : IJobEntity
{
    [ReadOnly] public NativeHashMap<int, float2> ResponseMap;

    void Execute(ref MoveTargetComponent moveTarget, in UnitComponent unit, in LocalTransform transform)
    {
        if (!ResponseMap.TryGetValue(unit.Id, out float2 dir)) return;

        float yaw = math.degrees(math.atan2(dir.x, dir.y));
        var targetCell = HexMetrics.WorldToOffsetWithYaw(transform.Position, yaw);
        moveTarget.Target = HexMetrics.OffsetToWorld(targetCell);
    }
}