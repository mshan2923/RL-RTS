using Unity.Entities;
using UnityEngine;

class Direction : MonoBehaviour
{
    [LayerField]
    public int Layer;
}

class DirectionBaker : Baker<Direction>
{
    public override void Bake(Direction authoring)
    {
        AddComponent(GetEntity(authoring, TransformUsageFlags.None), new CDirectionRequest
        {
            CollisionLayer = authoring.Layer
        });

        AddComponent<CDirectionResponse>(GetEntity(authoring, TransformUsageFlags.None));
        AddComponent<CDirectionRequestPending>(GetEntity(authoring, TransformUsageFlags.None));
    }
}
