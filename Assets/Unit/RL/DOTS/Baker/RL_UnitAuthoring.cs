using Unity.Entities;
using UnityEngine;

class RL_UnitAuthoring : MonoBehaviour
{
    
}

class RL_UnitAuthoringBaker : Baker<RL_UnitAuthoring>
{
    public override void Bake(RL_UnitAuthoring authoring)
    {
        AddComponent<RLParm>(GetEntity(authoring, TransformUsageFlags.Dynamic));
    }
}
