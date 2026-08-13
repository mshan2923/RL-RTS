
using JetBrains.Annotations;
using Unity.Entities;
using Unity.Mathematics;

public struct RLMapSetting : IComponentData
{
    public float2 Size;
    public float SpawnRandomOffset;
}
public struct RLParmCompoenent : IComponentData
{
    public int Amount;
    public UnitEnum TeamType;
}