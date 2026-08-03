using Unity.Entities;

public struct CHealth : IComponentData
{
    public float Current;
    public float Max;
}
public struct CDamage : IComponentData
{
    public float Damage;
    public float FireRate;
}
public struct CUnitParams : IComponentData
{
    public float Damage;
    public float FireRate;
    public float DetectDistance;
    public float AttackTendency;
}

public struct UnitRespawnTag : IComponentData , IEnableableComponent {}