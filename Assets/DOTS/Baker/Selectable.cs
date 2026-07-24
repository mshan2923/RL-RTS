using Unity.Entities;
using UnityEngine;

class Selectable : MonoBehaviour
{
    
}

public struct SelectComponent : IComponentData, IEnableableComponent {}
class SelectBaker : Baker<Selectable>
{
    public override void Bake(Selectable authoring)
    {
        AddComponent<SelectComponent>(GetEntity(authoring, TransformUsageFlags.Dynamic));
        
    }
}
