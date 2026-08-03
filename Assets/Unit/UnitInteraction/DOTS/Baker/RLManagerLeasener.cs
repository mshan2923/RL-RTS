using Unity.Entities;
using UnityEngine;

class RLManagerLeasener : MonoBehaviour
{
    public GameObject AllyUnit;
    public GameObject EnmyUnit;
}

class RLManagerLeasenerBaker : Baker<RLManagerLeasener>
{
    public override void Bake(RLManagerLeasener authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.None);
        AddComponent(entity, new CUnitPrefab
        {
            Ally = GetEntity(authoring.AllyUnit,TransformUsageFlags.Dynamic),
            Enmy = GetEntity(authoring.EnmyUnit, TransformUsageFlags.Dynamic)
        });

        AddComponent<UnitIntilizeTag>(entity);
        SetComponentEnabled<UnitIntilizeTag>(entity, false);
    }
}
