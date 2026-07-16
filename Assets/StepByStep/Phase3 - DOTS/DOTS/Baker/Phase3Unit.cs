using Unity.Entities;
using UnityEngine;

class Phase3UnitManager : MonoBehaviour
{
    public GameObject Prefab;
}

class Phase3UnitManagerBaker : Baker<Phase3UnitManager>
{
    public override void Bake(Phase3UnitManager authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
        AddComponent(entity, new Phase3UnitManagerComponent
        {
           Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic) 
        });
    }
}
