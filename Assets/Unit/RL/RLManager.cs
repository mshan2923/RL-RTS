using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class RLManager : MonoBehaviour
{
    [Header("Ally")]
    public int AllyAmount = 4;
    public UnitData AllyData;

    [Header("Enmy")]
    /// <summary>
    /// 학습시 최대로 배치될 적군 갯수
    /// </summary>
    public int EnmyMaxAmount;
    public UnitData EnmyData;
    public float unitSize = 0.25f;

    public Vector2 Size;
    public float RandomOffset = 1f;

    [System.Serializable]
    public struct UnitData
    {
        public float Damager;
        public float FireRate ;
        public float Health;
        public float DetectDistance;
        public float AttackDistance;
        /// <summary>
        /// 공격 성향
        /// </summary>
        [Range(0 ,1)]// Tooltip("공격 성향")
        public float AttackTendency;
    }

    void Start()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        int nextId = 0;
        SpawnUnits(em, AllyData, AllyAmount, UnitEnum.Ally, ref nextId);
        SpawnUnits(em, EnmyData, EnmyMaxAmount, UnitEnum.Enmy, ref nextId);


        // 생성으로 스폰을 DOTS에게 요청 
    }

    void SpawnUnits(EntityManager em, UnitData data, int amount, UnitEnum teamType, ref int nextId)
    {
            var entity = em.CreateEntity();
            em.AddComponentData(entity, new RLParmCompoenent
            {
                Amount = amount,
                TeamType = teamType,
                Width = Size.x,
                Height = Size.y,
                SpawnRandomOffset = RandomOffset
            });
            em.AddComponentData(entity, new UnitEnumComponent { type = teamType });
            em.AddComponentData(entity, new CHealth 
            {
                Prev = data.Health,
                 Current = data.Health,
                  Max = data.Health 
            });
            em.AddComponentData(entity, new CUnitParams
            {
                Damage = data.Damager,
                FireRate = data.FireRate,
                DetectDistance = data.DetectDistance,
                AttackDistance = data.AttackDistance,
                AttackTendency = data.AttackTendency
            });

    }
}

