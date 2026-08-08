
using Unity.Entities;

public struct RLParmCompoenent : IComponentData
{
    public int Amount;
    public UnitEnum TeamType;
    public float Width;
    public float Height;
    public float SpawnRandomOffset;
}