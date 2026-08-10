using Unity.Collections;
using Unity.Entities;

public struct CHealth : IComponentData
{
    public float Prev;
    public float Current;
    public float Max;
}
public struct CDamage : IComponentData
{
    public float Damage;
    public float FireRate;
}
public struct CWeaponCooldown : IComponentData
{
    public float cooldown;
}
public struct CanToAttackTag : IComponentData, IEnableableComponent { }
public struct ReadyToShotTag : IComponentData, IEnableableComponent { }
public struct ReadyToActionTag : IComponentData, IEnableableComponent { }

[InternalBufferCapacity(8)] // 초기 인라인 용량, 필요시 자동 확장됨
public struct CActionCooldown : IBufferElementData
{
    public FixedString32Bytes ActionName;
    public float RemainingTime;
    public float MaxTime;
}

public struct CUnitParams : IComponentData
{
    public float Damage;
    public float FireRate;
    public float DetectDistance;
    public float AttackDistance;
    public float AttackTendency;
}

public struct UnitRespawnTag : IComponentData , IEnableableComponent {}
public struct CNearTarget : IComponentData
{
    public Entity entity;
}

