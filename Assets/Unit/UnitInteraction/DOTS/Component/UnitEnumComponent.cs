using System;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;

public struct UnitEnumComponent : IComponentData , IEquatable<UnitEnumComponent>
{
    public UnitEnum type;

    public UnitEnumComponent(UnitEnum data)
    {
        type = data;   
    }
    public bool Equals(UnitEnumComponent other)
    {
        return type == other.type;
    }

    public override int GetHashCode()
    {
        return (int)type;
    }
}


public struct UnitIntilizeTag : IComponentData , IEnableableComponent {}
public struct CUnitPrefab : IComponentData
{
    public Entity Ally;
    public Entity Enmy;
}