using Unity.Entities;
using UnityEngine;

class Map : MonoBehaviour
{
    public GameObject HexPrefab;
    public int Width;
    public int Height;
    //Radius -> HexMetrics.outterRadius 으로 대체
}

class MapBaker : Baker<Map>
{
    public override void Bake(Map authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.None);
        AddComponent(entity, new MapConfig
        {
            Width = authoring.Width,
            Height = authoring.Height,
            HexPrefab = GetEntity(authoring.HexPrefab, TransformUsageFlags.Dynamic),
        });


    }
}
