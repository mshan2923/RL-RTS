using Unity.Entities;

public struct NetworkResponseComponent : IComponentData
{
    public int UnitId;
    public int NewX;
    public int NewZ;
}