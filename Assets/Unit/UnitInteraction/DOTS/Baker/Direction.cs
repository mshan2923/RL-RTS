using Unity.Entities;
using UnityEngine;

class Direction : MonoBehaviour
{
    [LayerField]
    public int Layer;
    public Color SelectColor = Color.yellow;
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
        AddComponent<CSelectColor>(GetEntity(authoring, TransformUsageFlags.None), new CSelectColor
        {
            color = new Unity.Mathematics.float4(authoring.SelectColor.r, authoring.SelectColor.g, authoring.SelectColor.b, authoring.SelectColor.a)
        });
    }
}
