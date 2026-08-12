using System;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;
using Unity.Mathematics;

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
    public float4 AllyColor;
    public float4 EnmyColor;
}