using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;

class RLManagerLeasener : MonoBehaviour
{
    public GameObject AllyUnit;
    public GameObject EnmyUnit;
    public Color AllyColor = Color.green;
    public Color EnmyColor = Color.red;
}

class RLManagerLeasenerBaker : Baker<RLManagerLeasener>
{
    public override void Bake(RLManagerLeasener authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.None);
        AddComponent(entity, new CUnitPrefab
        {
            Ally = GetEntity(authoring.AllyUnit,TransformUsageFlags.Dynamic),
            Enmy = GetEntity(authoring.EnmyUnit, TransformUsageFlags.Dynamic),
            AllyColor = new float4(authoring.AllyColor.r, authoring.AllyColor.g,  authoring.AllyColor.b,  authoring.AllyColor.a),
            EnmyColor = new float4( authoring.EnmyColor.r, authoring.EnmyColor.g, authoring.EnmyColor.b, authoring.EnmyColor.a)
        });

        AddComponent<UnitIntilizeTag>(entity);
        SetComponentEnabled<UnitIntilizeTag>(entity, false);
    }
}
