using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Rendering;

public partial class MapSpawnerSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // 1. 설정이 있는지 확인
        if (!SystemAPI.TryGetSingleton<MapConfig>(out var config)) return;


        float horizontalDistance = config.Radius * math.sqrt(3f);
        float verticalDistance = config.Radius * 1.5f;

        var ecb = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        for (int x = 0; x < config.Width; x++)
        {
            for (int z = 0; z < config.Height; z++)
            {
                var tile = ecb.Instantiate(config.HexPrefab);

                float xPos = x * horizontalDistance + (z % 2 == 1 ? horizontalDistance * 0.5f : 0f);
                float zPos = z * verticalDistance;

                var pos = HexMetrics.OffsetToWorld(new int2(x, z)) - new float3(0, 0.5f, 0);

                // 2offset
                // 3. 문법 수정: SetComponentData 사용
                ecb.SetComponent(tile, LocalTransform.FromPosition(pos));
                ecb.SetComponent(tile, new HexTile { X = x, Z = z });
                ecb.AddComponent(tile, new URPMaterialPropertyBaseColor { Value = new float4(1, 1, 1, 1) });
            }
        }


        Enabled = false;
    }
}