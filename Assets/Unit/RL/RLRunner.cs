using System.Collections.Generic;
using RL_StepByStep;
using Unity.Collections;
using Unity.Entities;
using Unity.InferenceEngine;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class RLRunner : MonoBehaviour
{
    public RLManager rLManager;
    public ModelAsset model;
    public RunMode mode; // Training / Inference

    PythonTrainingPolicy<CObservation, CActionData> trainingPolicy; // Training 전용
    OnnxInferenceRunner<UnitState> inferenceRunner; // Inference 전용, 기존 InferenceRunner 재사용

    EntityQuery unitQuery;
    NativeArray<CObservation> obsArray;
    NativeArray<CActionData> actionArray;

    async void Start()
    {
        if (mode == RunMode.Training)
            trainingPolicy = new PythonTrainingPolicy<CObservation, CActionData>("127.0.0.1", 5555);
        else
            inferenceRunner = new OnnxInferenceRunner<UnitState>(model); // 예시

        unitQuery = BuildQuery();
        await Loop();
    }

    private async System.Threading.Tasks.Task Loop()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        while (true)
        {
            if (unitQuery.CalculateEntityCount() == 0)
            {
                await Awaitable.NextFrameAsync();
                continue;
            }

            var entities = unitQuery.ToEntityArray(Allocator.TempJob);
            var transArray = unitQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            var healthArray = unitQuery.ToComponentDataArray<CHealth>(Allocator.TempJob);
            var nearTargetArray = unitQuery.ToComponentDataArray<CNearTarget>(Allocator.TempJob);

            int count = entities.Length;
            EnsureArrays(count);

            for (int i = 0; i < count; i++)
            {
                var result = ObservationBuilder.Build(
                    i, entities[i], transArray[i].Position, healthArray[i],
                    nearTargetArray[i].entity, em, rLManager);

                // target Prev 갱신 (기존 코드)
                if (em.Exists(nearTargetArray[i].entity))
                {
                    var targetHealth = em.GetComponentData<CHealth>(nearTargetArray[i].entity);
                    targetHealth.Prev = targetHealth.Current;
                    em.SetComponentData(nearTargetArray[i].entity, targetHealth);
                }

                // self HP Prev 갱신 (이전에 고친 부분)
                var selfHealthCurrent = healthArray[i];
                selfHealthCurrent.Prev = selfHealthCurrent.Current;
                em.SetComponentData(entities[i], selfHealthCurrent);

                // PrevPhi 갱신 (새로 추가)
                if (!result.isOutOfPerception)
                {
                    var shaping = em.GetComponentData<CRLShaping>(entities[i]);
                    shaping.PrevPhi = result.currentPhi;
                    em.SetComponentData(entities[i], shaping);
                }

                var obs = result.obs;

                if (float.IsNaN(obs.dx) || float.IsInfinity(obs.dx) ||
                    float.IsNaN(obs.selfHp) || float.IsInfinity(obs.selfHp) ||
                    float.IsNaN(obs.distToEdge) || float.IsInfinity(obs.distToEdge) ||
                    float.IsNaN(obs.delta) || float.IsInfinity(obs.delta))
                {
                    Debug.LogError($"[NaN 감지] unit={obs.unit_id}, dx={obs.dx}, selfHp={obs.selfHp}, distToEdge={obs.distToEdge}, delta={obs.delta}");
                }

                if (mode == RunMode.Training)
                    obs = RewardCalculator.Apply(obs, result.isOutOfPerception, result.attackDistNormalized);

                obsArray[i] = obs;
            }

            entities.Dispose();
            transArray.Dispose();
            healthArray.Dispose();
            nearTargetArray.Dispose();

            if (count > 0)
            {
                if (mode == RunMode.Training)
                    await trainingPolicy.UpdateTrainingAsync(obsArray, actionArray);
                else
                    await inferenceRunner.InferAsync(obsArray, actionArray); // 동기 or 비동기, ONNX 러너 시그니처에 맞춰

                ApplyActions();
            }

            await Awaitable.NextFrameAsync();
        }
    }

    void ApplyActions()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        var entities = unitQuery.ToEntityArray(Allocator.TempJob);

        for (int i = 0; i < entities.Length; i++)
        {
            
            em.SetComponentData(entities[i], new CUnitState
            {
                Debug = "RL Runner.cs",
                unitState = (UnitState)actionArray[i].action_index
            });
        }

        entities.Dispose();
    }

    void EnsureArrays(int count)
    {
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
    }

    EntityQuery BuildQuery()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        var build = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<CHealth, CNearTarget, LocalTransform>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities);
        var query = em.CreateEntityQuery(build);
        build.Dispose();
        return query;
    }

    void OnDestroy()
    {
        trainingPolicy?.Dispose();
        if (obsArray.IsCreated) obsArray.Dispose();
        if (actionArray.IsCreated) actionArray.Dispose();
    }
}

public enum RunMode { Training, Inference }