using Unity.Entities;
using UnityEngine;

class Tile : MonoBehaviour
{

}

class TileBaker : Baker<Tile>
{
    public override void Bake(Tile authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

        AddComponent(entity, new HexTile
        {
            X = 0,
            Z = 0
        });
    }
}
