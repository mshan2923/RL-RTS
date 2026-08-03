using Unity.Entities;
using UnityEngine;

class UnitStateAuthring : MonoBehaviour
{
    public UnitState unitState;
}

class UnitStateBaker : Baker<UnitStateAuthring>
{
    public override void Bake(UnitStateAuthring authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
        AddComponent(entity, new CUnitState
        {
            Debug = "Bake",
            unitState = authoring.unitState
        });

        AddComponent<CCommandPending>(entity);
        SetComponentEnabled<CCommandPending>(entity, false);
    }
}
