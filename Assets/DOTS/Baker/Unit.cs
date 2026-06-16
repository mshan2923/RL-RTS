using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

class Unit : MonoBehaviour
{
    public int Id;
}

class UnitBaker : Baker<Unit>
{
    public override void Bake(Unit authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

        AddComponent(entity, new UnitComponent
        {
            Id = authoring.Id
        });
        AddComponent(entity, new MoveTargetComponent
        {
            Target = authoring.transform.position
        });
        AddComponent(entity, new DetectWallNormalize
        {
            n0 = 1,
            n1 = 1,
            n2 = 1,
            n3 = 1,
            n4 = 1,
            n5 = 1
        });
        AddComponent(entity, new TargetInfo
        {
            Position = float3.zero,
            IsActive = false,
        });
    }
}
