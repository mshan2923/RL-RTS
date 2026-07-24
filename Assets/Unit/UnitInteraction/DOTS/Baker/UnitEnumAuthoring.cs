using Unity.Entities;
using UnityEngine;

class UnitEnumAuthoring : MonoBehaviour
{
    public UnitEnum type;
}

class UnitEnumBaker : Baker<UnitEnumAuthoring>
{
    public override void Bake(UnitEnumAuthoring authoring)
    {
        AddComponent(GetEntity(authoring, TransformUsageFlags.Dynamic) ,new UnitEnumComponent
        {
            type = authoring.type
        });
    }
}
