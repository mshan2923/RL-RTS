using Unity.Entities;
using Unity.Rendering;
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
            MoveTo = authoring.transform.position
        });
        AddComponent(entity, new URPMaterialPropertyBaseColor
        {
            Value = new Unity.Mathematics.float4(1,1,1,1)
        });
    }
}
