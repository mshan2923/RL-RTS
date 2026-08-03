using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


partial struct TestFindUnit : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        
    }

    public void OnUpdate(ref SystemState state)
    {
        //todo 특정 좌표 기준으로 일정 범위 탐색 하게 

        var spatialGrid = state.World.GetExistingSystemManaged<SpatialGridSystem>().Grid.AsReadOnly();

        using var result = new NativeList<Entity>(Allocator.TempJob);
        SpatialGridSystem.FindNearby(spatialGrid, float3.zero, 2f, result);

        Debug.Log($"Find : {result.Length}");
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }
}
