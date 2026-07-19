using Unity.Entities;
using UnityEngine;

class Obstacle : MonoBehaviour
{
    
}
public struct ObstacleTag : IComponentData {}
class ObstacleBaker : Baker<Obstacle>
{
    public override void Bake(Obstacle authoring)
    {
        AddComponent<ObstacleTag>(GetEntity(authoring, TransformUsageFlags.Dynamic));
    }
}
