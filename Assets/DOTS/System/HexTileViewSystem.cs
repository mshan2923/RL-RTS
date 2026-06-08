using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;


public partial class HexTileViewSystem : SystemBase
{
    protected override void OnUpdate()
    {
        foreach (var (tile, colorRef, entity) in SystemAPI.Query<HexTile, RefRW<URPMaterialPropertyBaseColor>>().WithEntityAccess())
        {
            colorRef.ValueRW.Value = tile.OwnerID switch
            {
                GroupType.Ally => new float4(0.2f, 0.6f, 1.0f, 1f),
                GroupType.Enmy => new float4(1.0f, 0.3f, 0.3f, 1f),
                _ => new float4(0.5f, 0.5f, 0.5f, 1f)
            };

        }
    }

    // ECS에서 GameObject 참조용
    public class HexTileViewReference : IComponentData
    {
        public HexTileView Value;
    }
}