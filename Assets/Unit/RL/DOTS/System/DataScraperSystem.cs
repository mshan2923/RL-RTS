using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

partial struct DataScraperSystem : ISystem
{
    EntityQuery unitQuery;
    bool isIntilize;
    CUnitParams unitParam;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {

        unitQuery = DOTS_Mecro.UnitParmQuery(ref state);

    }
    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }


    public void OnUpdate(ref SystemState state)
    {
        if (!isIntilize)
        {
            if (unitQuery.CalculateEntityCount() > 0)
            {
                isIntilize = true;
                var parms = unitQuery.ToComponentDataArray<CUnitParams>(Allocator.TempJob);
                var unitenums = unitQuery.ToComponentDataArray<UnitEnumComponent>(Allocator.TempJob);

                for(int i = 0; i < unitQuery.CalculateEntityCount(); i++)
                {
                    if (unitenums[i].type == UnitEnum.Ally)
                    {
                        unitParam = parms[i];
                    }
                }

                if (parms.IsCreated) parms.Dispose();
                if (unitenums.IsCreated) unitenums.Dispose();
            }
        }

        var spatialGrid = state.World.GetExistingSystemManaged<SpatialGridSystem>().Grid.AsReadOnly();

        var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var unitEnumLookup = SystemAPI.GetComponentLookup<UnitEnumComponent>(true);
        var hpLookup = SystemAPI.GetComponentLookup<CHealth>(true);

        transformLookup.Update(ref state);
        unitEnumLookup.Update(ref state);
        hpLookup.Update(ref state);

        var job = new Scrapper
        {
            spatialGrid = spatialGrid,
            TargetUnitEnum = UnitEnum.Ally, // 필요 시 파라미터화
            SearchRadius = unitParam.DetectDistance, 
            transformLookup = transformLookup,
            unitEnumLookup = unitEnumLookup,
            hpLookup = hpLookup
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);


                // 아군 유닛 마다  가장 가까운 적을 대상으로 선택지를 선택
        // 아군 유닛 마다 주위의 적중 가장 적합한 대상을 찾는건 스크립트로

        //UnitEnum 이 Ally가 아닌 모두 리스트를 모와
        //엔티티 마다 가장 가까운 대상을 저장
        //엔티티 마다  체력도 
        //

        //총괄 메니져가 mono에 존재하고 유닛 갯수 , 체력 , 감지거리 등 설정 , dots에 전달
        //보상을 줄 목표는? -> 교전후 생존
        //희귀한 큰 보상보단 작은 보상 여러개

    }

    [BurstCompile]
    public partial struct Scrapper : IJobEntity
    {
        public NativeParallelMultiHashMap<int2, Entity>.ReadOnly spatialGrid;
        public UnitEnum TargetUnitEnum;
        public float SearchRadius;
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public ComponentLookup<UnitEnumComponent> unitEnumLookup;
        [ReadOnly] public ComponentLookup<CHealth> hpLookup;
        

        public void Execute([EntityIndexInQuery]int index, Entity entity, in UnitEnumComponent unitEnum, in LocalTransform transform, in CHealth health, ref RLParm rLParm)
        {
            if (TargetUnitEnum != unitEnum.type) return;

            using var entities = new NativeList<Entity>(Allocator.Temp);
            SpatialGridSystem.FindNearby(spatialGrid, transform.Position, SearchRadius, entities, entity);

            var data = new RLParm();

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

            rLParm = data;
            
        }
    }
}