using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct BaseComponent : IComponentData
{
    public GroupType Team;
    public float3 Position;
}

public class Base : MonoBehaviour
{
    public GroupType Team = GroupType.None;

    class Baker : Baker<Base>
    {
        public override void Bake(Base authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new BaseComponent
            {
                Position = authoring.transform.position,
                Team = authoring.Team,
            });
        }
    }
}