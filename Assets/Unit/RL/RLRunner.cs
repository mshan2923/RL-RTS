using System.Collections.Generic;
using RL_StepByStep;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class RLRunner : MonoBehaviour
{
    public RLManager rLManager;
    PythonTrainingPolicy<CObservation, CActionData> policy;
    EntityQuery unitQuery;

    NativeArray<CObservation> obsArray;
    NativeArray<CActionData> actionArray;

    async void Start()
    {
        policy = new PythonTrainingPolicy<CObservation, CActionData>("127.0.0.1", 5555);
        unitQuery = BuildQuery();
        await Loop();
    }

    EntityQuery BuildQuery()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
    var build = new EntityQueryBuilder(Allocator.Temp)
        .WithAll<RLParm, CHealth, CNearTarget, LocalTransform>()
        .WithOptions(EntityQueryOptions.IncludeDisabledEntities);
        
        var query = em.CreateEntityQuery(build);
        build.Dispose();
        return query;
    }

    private async System.Threading.Tasks.Task Loop()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        var grid = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<SpatialGridSystem>().Grid.AsReadOnly();
        

        while (true)
        {
            if (unitQuery.CalculateEntityCount() == 0)
            {
                Debug.Log("Empty Unit");

                await Awaitable.NextFrameAsync();
                continue;
            }


            var entities = unitQuery.ToEntityArray(Allocator.TempJob);
            var parmArray = unitQuery.ToComponentDataArray<RLParm>(Allocator.TempJob);
            var transArray = unitQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            var healthArray = unitQuery.ToComponentDataArray<CHealth>(Allocator.TempJob);
            var nearTargetArray = unitQuery.ToComponentDataArray<CNearTarget>(Allocator.TempJob);

            int count = parmArray.Length;

            if (!obsArray.IsCreated || obsArray.Length != count)
            {
                if (obsArray.IsCreated) obsArray.Dispose();
                obsArray = new NativeArray<CObservation>(count, Allocator.Persistent);
            }
            if (!actionArray.IsCreated || actionArray.Length != count)
            {
                if (actionArray.IsCreated) actionArray.Dispose();
                actionArray = new NativeArray<CActionData>(count, Allocator.Persistent);
            }

            for (int i = 0; i < count; i++)
            {
                var entity = entities[i];
                var selfPos = transArray[i].Position;

                var targetList = new NativeList<Entity>(Allocator.Temp);
                SpatialGridSystem.FindNearby(grid, selfPos, rLManager.AllyData.DetectDistance, targetList);

                CObservation obs;

                var closest = nearTargetArray[i].entity;
                if (closest == Entity.Null)
                {
                    obs = new CObservation
                    {
                        unit_id = i,
                        dx = 0f,
                        dy = 0f,
                        selfHp = healthArray[i].Current / healthArray[i].Max,
                        targetHp = 0f,
                        alive = em.IsEnabled(entity) ? 1 : 0,
                        reward = 0f,
                        done = 0
                    };
                }
                else
                {
                    var targetPos = em.GetComponentData<LocalTransform>(closest).Position;
                    var targetHealth = em.GetComponentData<CHealth>(closest);

                    var dxy = (targetPos - selfPos) / rLManager.AllyData.DetectDistance;

                    obs = new CObservation
                    {
                        unit_id = i,
                        dx = Mathf.Clamp(dxy.x, -1f, 1f),
                        dy = Mathf.Clamp(dxy.z, -1f, 1f),
                        selfHp = healthArray[i].Current / healthArray[i].Max,
                        targetHp = targetHealth.Current / targetHealth.Max,
                        alive = em.IsEnabled(entity) ? 1 : 0,
                        reward = 0f,//Todo 행동 적절성
                        done = 0
                    };
                    obs = Reward(obs);
                }

                obsArray[i] = obs;
                targetList.Dispose();
            }

            entities.Dispose();
            parmArray.Dispose();
            transArray.Dispose();
            healthArray.Dispose();
            nearTargetArray.Dispose();

            if (count > 0)
            {
                await policy.UpdateTrainingAsync(obsArray, actionArray);

                entities = unitQuery.ToEntityArray(Allocator.TempJob);

                for (int i = 0; i < count; i++)
                {
                    var entity = entities[i];

                    em.SetComponentData(entity, new CUnitState
                    {
                        Debug = "RL Runner.cs",
                        unitState = (UnitState)actionArray[i].action_index
                    });

                }

                entities.Dispose();
            }

            await Awaitable.NextFrameAsync();
        }
    }

    CObservation Reward(CObservation parm)
    {
        float distance = math.length(new float2(parm.dx, parm.dy)); // dx=x축, dy=z축 성분 (필드 이름이 dy지만 실제론 z)
        float score = (1f - distance) * 5f;
        
        score += parm.selfHp * 10f;
        // score += parm.targetHp * -10f;
        score += (1 - parm.alive) * -50f;
        score += parm.alive * 1f;

        //변화량에 따른 결과로 만들기
        parm.reward = score;
        return parm;
    }

    void OnDestroy()
    {
        policy?.Dispose();
        if (obsArray.IsCreated) obsArray.Dispose();
        if (actionArray.IsCreated) actionArray.Dispose();
    }
}