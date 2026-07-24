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
}
