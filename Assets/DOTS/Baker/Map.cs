using Unity.Entities;
using UnityEngine;

class Map : MonoBehaviour
{
    public GameObject HexPrefab;
    public int Width;
    public int Height;
    public float Radius = 1f;
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
            Radius = authoring.Radius
        });

    }
}
