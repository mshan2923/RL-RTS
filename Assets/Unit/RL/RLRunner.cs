using System.Collections.Generic;
using System.Threading;
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

    CancellationTokenSource cts;

    async void Start()
    {
        cts = new CancellationTokenSource();

        if (mode == RunMode.Training)
            trainingPolicy = new PythonTrainingPolicy<CObservation, CActionData>("127.0.0.1", 5555);
        else
            inferenceRunner = new OnnxInferenceRunner<UnitState>(model); // 예시

        unitQuery = BuildQuery();
        RewardCalculator.Config = rLManager.PhiConfig;

        try
        {
            await Loop(cts.Token);
        }
        catch (System.OperationCanceledException)
        {
            Debug.Log("RLRunner loop canceled.");
        }finally
        {
            DisposeResources();
        }
    }

    private async System.Threading.Tasks.Task Loop(CancellationToken token)
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        while (!token.IsCancellationRequested)
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

            var AllyTendency = rLManager.AllyData.AttackTendency;
            var EnmyTendency = rLManager.EnmyData.AttackTendency;

            int count = entities.Length;
            EnsureArrays(count);

            // 1단계: Observation 생성 및 보상 계산
            for (int i = 0; i < count; i++)
            {
                var result = ObservationBuilder.Build(
                    i, entities[i], transArray[i].Position, healthArray[i],
                    nearTargetArray[i].entity, em, rLManager);

                // PrevPhi는 인지 범위와 상관없이 항시 currentPhi로 맞춰줘야 델타 오염이 안 생겨
                var shaping = em.GetComponentData<CRLShaping>(entities[i]);
                shaping.PrevPhi = result.currentPhi;
                em.SetComponentData(entities[i], shaping);

                var obs = result.obs;

                if (float.IsNaN(obs.dx) || float.IsNaN(obs.selfHp) || float.IsNaN(obs.delta))
                {
                    Debug.LogError($"[NaN 감지] unit={obs.unit_id}, dx={obs.dx}, selfHp={obs.selfHp}, delta={obs.delta}");
                }

                if (mode == RunMode.Training)
                {
                    obs = RewardCalculator.Apply(obs, result.isOutOfPerception, result.attackDistNormalized);
                }
                else
                {
                    if (rLManager.TendencyForEach)
                    {
                        var unitParams = em.GetComponentData<CUnitParams>(entities[i]);
                        obs.AttackTendency = unitParams.AttackTendency;
                    }
                    else
                    {
                        var team = em.GetComponentData<UnitEnumComponent>(entities[i]).type;
                        obs.AttackTendency = team == UnitEnum.Ally ? AllyTendency : EnmyTendency;
                    }
                }
                
                obsArray[i] = obs;
            }

            // 2단계: 관측이 전부 끝난 후, Prev 체력 일괄 갱신 (중복 참조 오염 방지)
            for (int i = 0; i < count; i++)
            {
                var selfHealthCurrent = healthArray[i];
                selfHealthCurrent.Prev = selfHealthCurrent.Current;
                em.SetComponentData(entities[i], selfHealthCurrent);
            }

            if (count > 0)
            {
                if (mode == RunMode.Training)
                    await trainingPolicy.UpdateTrainingAsync(obsArray, actionArray);
                else
                    await inferenceRunner.InferAsync(obsArray, actionArray);

                token.ThrowIfCancellationRequested();
                
                // 기존 entities 배열 그대로 전달
                ApplyActions(entities, count);
            }

            entities.Dispose();
            transArray.Dispose();
            healthArray.Dispose();
            nearTargetArray.Dispose();

            await Awaitable.NextFrameAsync();
        }
    }

    void ApplyActions(NativeArray<Entity> entities, int count)
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        for (int i = 0; i < count; i++)
        {
            em.SetComponentData(entities[i], new CUnitState
            {
                Debug = "RL Runner.cs",
                unitState = (UnitState)actionArray[i].action_index
            });
        }
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
            .WithAll<CHealth, CNearTarget, LocalTransform , CUnitParams>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities);
        var query = em.CreateEntityQuery(build);
        build.Dispose();
        return query;
    }

    void DisposeResources()
    {
        trainingPolicy?.Dispose();
        if (obsArray.IsCreated) obsArray.Dispose();
        if (actionArray.IsCreated) actionArray.Dispose();
        inferenceRunner?.Dispose();
    }

    void OnDestroy()
    {
        cts?.Cancel();
        // DisposeResources()는 Start()의 finally에서 호출되므로 여기서 다시 부르지 않음
    }
}

public enum RunMode { Training, Inference }