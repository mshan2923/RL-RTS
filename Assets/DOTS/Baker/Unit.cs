using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

class Unit : MonoBehaviour
{
        public int Id;
        public float Health = 10f;
        public float Damage = 1f;
        public float FireRate = 1f;
}

class UnitBaker : Baker<Unit>
{
    public override void Bake(Unit authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

        AddComponent(entity, new UnitComponent
        {
            Id = authoring.Id
        });
        AddComponent(entity, new MoveTargetComponent
        {
            MoveTo = authoring.transform.position
        });
        AddComponent(entity, new URPMaterialPropertyBaseColor
        {
            Value = new Unity.Mathematics.float4(1,1,1,1)
        });

        AddComponent(entity, new CHealth
        {
            Prev = authoring.Health,
            Current = authoring.Health,
            Max = authoring.Health
        });
        AddComponent(entity, new CDamage
        {
            Damage = authoring.Damage,
            FireRate = authoring.FireRate
        });
        AddComponent<UnitRespawnTag>(entity);
        AddComponent<CNearTarget>(entity);


        AddComponent<CWeaponCooldown>(entity);
        var buffer = AddBuffer<CActionCooldown>(entity);
        AddComponent<CanToAttackTag>(entity);
        SetComponentEnabled<CanToAttackTag>(entity, false);

        AddComponent<ReadyToShotTag>(entity);
        SetComponentEnabled<ReadyToShotTag>(entity, false);
        AddComponent<ReadyToActionTag>(entity);
        SetComponentEnabled<ReadyToActionTag>(entity, false);
    }
}
