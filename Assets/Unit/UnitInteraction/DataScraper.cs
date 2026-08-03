using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class DataScraper : MonoBehaviour
{
    public RLManager rLManager;
    EntityManager em;

        /*
        - 상대 방향 <- Lookup
        - 상대 거리 (N / 상대거리) <- Lookup
        - 자기 그룹 dps
        - 타겟 그룹 dps
        - 자신의 hp 비율 <- Lookup
        - 타겟의 hp 비율 <- Lookup
        - 자신 쿨다운 , FireRate 상태 <- Lookup
        - 아군 그룹 점유 셀 수
        - 타겟 그룹 점유 셀 수 (인지거리안 셀중 배치되어있는 칸수로?)
        */

    [Serializable]
    public struct RLParm
    {
        public float3 Direction;
        /// <summary>
        /// 거리 / 인지거리
        /// </summary>
        public float Distance;
        public float SelfHP;
        public float TargetHP;

        //? 우선 모델을 작게 시작
        // public float AllyDPS;
        // public float EnmyDPS;
        // public float Cooltime;
        // public float AllyShells;
        // public float EnmyShells;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        em = World.DefaultGameObjectInjectionWorld.EntityManager;
    }
    void OnEnable()
    {
        
    }
    void OnDestroy()
    {
        
    }
    // Update is called once per frame
    void Update()
    {
        var spatialGrid = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<SpatialGridSystem>().Grid.AsReadOnly();
        // using var result = new NativeList<Entity>(Allocator.TempJob);
        // SpatialGridSystem.FindNearby(spatialGrid, float3.zero, 2f, result);

        // Debug.Log($"Find : {result.Length}");

        // 아군 유닛 마다  가장 가까운 적을 대상으로 선택지를 선택
        // 아군 유닛 마다 주위의 적중 가장 적합한 대상을 찾는건 스크립트로

        //UnitEnum 이 Ally가 아닌 모두 리스트를 모와
        //엔티티 마다 가장 가까운 대상을 저장
        //엔티티 마다  체력도 
        //

        //총괄 메니져가 mono에 존재하고 유닛 갯수 , 체력 , 감지거리 등 설정 , dots에 전달
        //보상을 줄 목표는? -> 교전후 생존
        //희귀한 큰 보상보단 작은 보상 여러개

        using var result = new NativeArray<RLParm>(spatialGrid.Count(), Allocator.TempJob);
        JobHandle handle = default;
        handle = new Scrapper
        {
            spatialGrid = spatialGrid,
            TargetUnitEnum = UnitEnum.Ally,
            SearchRadius = rLManager.AllyData.DetectDistance,
            transformLookup = new ComponentLookup<LocalTransform>(),
            unitEnumLookup = new ComponentLookup<UnitEnumComponent>(),
            hpLookup = new ComponentLookup<CHealth>(),

            Result = result

        }.ScheduleParallel(handle);



        result.Dispose(handle);
    }

}

    public partial struct Scrapper : IJobEntity
    {
        public NativeParallelMultiHashMap<int2, Entity>.ReadOnly spatialGrid;
        public UnitEnum TargetUnitEnum;
        public float SearchRadius;
        public ComponentLookup<LocalTransform> transformLookup;
        public ComponentLookup<UnitEnumComponent> unitEnumLookup;
        public ComponentLookup<CHealth> hpLookup;

        public NativeArray<DataScraper.RLParm> Result;
        

        public void Execute([EntityIndexInQuery]int index, Entity entity, in UnitEnumComponent unitEnum, in LocalTransform transform, in CHealth health)
        {
            if (TargetUnitEnum != unitEnum.type) return;

            using var entities = new NativeList<Entity>(Allocator.Temp);
            SpatialGridSystem.FindNearby(spatialGrid, transform.Position, SearchRadius, entities);

            var data = new DataScraper.RLParm();

            Entity target = Entity.Null;
            foreach(var v in entities)
            {
                if (unitEnumLookup.GetRefRO(v).ValueRO.type == TargetUnitEnum) return;

                float resultDis = float.MaxValue;
                float dis = math.distance(transform.Position, transformLookup[v].Position);
                if (resultDis < dis)
                {
                    resultDis = dis;
                    target = v;
                }
            }

            {
                var targethp = hpLookup.GetRefRO(target).ValueRO;
                data.Direction = math.normalize(transformLookup[target].Position - transform.Position);
                data.Distance = math.distance(transform.Position, transformLookup[target].Position) / SearchRadius;
                data.SelfHP = health.Current / health.Max;
                data.TargetHP = targethp.Current / targethp.Max;
            }

            Result[index] = data;
            
        }
    }
