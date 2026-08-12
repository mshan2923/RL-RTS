using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

partial struct RLManagerSystem : ISystem
{
    EntityQuery RequestQuery;
    EntityQuery ManagerQuery;
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using var RequestBuild = new EntityQueryBuilder(Allocator.Temp);
        RequestBuild.WithAll<RLParmCompoenent, UnitEnumComponent, CHealth, CUnitParams>();
        RequestQuery = RequestBuild.Build(ref state);   

        // using var ManagerBuild = new EntityQueryBuilder(Allocator.Temp);
        // ManagerBuild.WithAll<UnitSpawnTag, CUnitPrefab>();
        // ManagerQuery = ManagerBuild.Build(ref state);   
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (RequestQuery.CalculateEntityCount() == 0) return;

        if(!SystemAPI.TryGetSingleton<CUnitPrefab>(out var prefab)) return;

        var managerEntity = SystemAPI.GetSingletonEntity<CUnitPrefab>();

        if (state.EntityManager.IsComponentEnabled<UnitIntilizeTag>(managerEntity)) return;

        var ecb  = new EntityCommandBuffer(Allocator.TempJob);


        state.Dependency = new SpawnJob
        {
            ecb = ecb.AsParallelWriter(),
            unitPrefab = prefab,
            random = new Unity.Mathematics.Random(12314u)
        }.ScheduleParallel(RequestQuery, state.Dependency);
        state.Dependency.Complete();

        state.EntityManager.SetComponentEnabled<UnitIntilizeTag>(managerEntity, true);

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }

  [BurstCompile]
    partial struct SpawnJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ecb;
        public CUnitPrefab unitPrefab;
        public Unity.Mathematics.Random random;
        public void Execute([EntityIndexInQuery]int index, Entity entity, in RLParmCompoenent rLParm, in UnitEnumComponent unitEnum, in CHealth health, in CUnitParams unitParams)
        {
            Entity prefabEntity = Entity.Null;
            switch (unitEnum.type)
            {
                case UnitEnum.Nature:
                    break;
                case UnitEnum.Ally:
                    prefabEntity = unitPrefab.Ally;
                    break;
                case UnitEnum.Enmy:
                    prefabEntity = unitPrefab.Enmy;
                    break;
            }


            var size = new float3(rLParm.Width, 0 , rLParm.Height);
            var offset = new float3(rLParm.SpawnRandomOffset, 0 , rLParm.SpawnRandomOffset);

            for(int i = 0 ; i < rLParm.Amount; i++)
            {
                var spawnd = ecb.Instantiate(index , prefabEntity);

                ecb.SetComponent(index, spawnd, unitEnum);
                ecb.SetComponentEnabled<UnitRespawnTag>(index, spawnd, true);


                //todo 일단 여기서 배치 해놓고  나중에 리스폰 태그 활성화 시키기


                // var pos = random.NextFloat3(-size + offset, size - offset) + size * 0.5f;

                // ecb.SetComponent(index , spawnd, new LocalTransform
                // {
                //    Position  = pos,
                //    Rotation = quaternion.identity,
                //    Scale = 1f 
                // });
                // ecb.SetComponent(index, spawnd, new MoveTargetComponent
                // {
                //    MoveTo =  pos
                // });

            }

        }
    }
}


