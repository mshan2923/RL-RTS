using Unity.Entities;
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
    }
}
