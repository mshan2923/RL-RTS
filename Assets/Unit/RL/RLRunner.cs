using System.Collections.Generic;
using System.Text;
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

    int debugFrame;
const int DebugLogInterval = 30;


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

            int count = entities.Length;
            EnsureArrays(count);

            // 1단계: Observation 생성 및 보상 계산
            for (int i = 0; i < count; i++)
            {
                var result = ObservationBuilder.Build(
                    i, entities[i], transArray[i].Position, healthArray[i],
                    nearTargetArray[i].entity, em, rLManager);

                // PrevPhi는 인지 범위와 상관없이 항시 currentPhi로 맞춰줘야 델타 오염이 안 생겨
                {
                        // RLRunner에서 PrevPhi 갱신하는 부분
                    var shaping = em.GetComponentData<CRLShaping>(entities[i]);

                    if (shaping.LastTarget != nearTargetArray[i].entity)
                    {
                        shaping.PrevPhi = result.currentPhi;
                        shaping.LastTarget = nearTargetArray[i].entity;
                    }
                    else
                    {
                        shaping.PrevPhi = result.currentPhi;  // isOutOfPerception 여부 상관없이 항상 갱신
                    }

                    em.SetComponentData(entities[i], shaping);
                }

                var obs = result.obs;

                if (float.IsNaN(obs.dx) || float.IsNaN(obs.selfHp) || float.IsNaN(obs.delta))
                {
                    Debug.LogError($"[NaN 감지] unit={obs.unit_id}, dx={obs.dx}, selfHp={obs.selfHp}, delta={obs.delta}");
                }

                if (mode == RunMode.Training)
                {
                    obs = RewardCalculator.Apply(
                        obs,
                        result.isOutOfPerception,
                        result.attackDistNormalized,
                        result.desiredDistanceNormalized);
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

                LogRlSnapshot(entities, count);
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
            var entity = entities[i];
            var selectedAction =
                (UnitState)actionArray[i].action_index;

            if (mode == RunMode.Inference &&
                TryGetDistanceState(
                    entity,
                    obsArray[i].AttackTendency,
                    out float actualDistance,
                    out float desiredDistance))
            {
                const float DeadZone = 0.25f;
                const float Hysteresis = 0.5f;

                float error =
                    math.abs(actualDistance - desiredDistance);

                var previousState =
                    em.GetComponentData<CUnitState>(entity).unitState;

                if (error <= DeadZone ||
                    (previousState == UnitState.HoldPosition &&
                    error <= Hysteresis))
                {
                    selectedAction = UnitState.HoldPosition;
                }
            }

            em.SetComponentData(entities[i], new CUnitState
            {
                Debug = "RL Runner.cs",
                unitState = selectedAction
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
            .WithAll<CHealth, CNearTarget, LocalTransform>()
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

    void LogRlSnapshot(NativeArray<Entity> entities, int count)
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        debugFrame++;

        bool hasDamage = false;

        for (int i = 0; i < count; i++)
        {
            if (obsArray[i].targetHp > 0f)
            {
                hasDamage = true;
                break;
            }
        }

        if (debugFrame % DebugLogInterval != 0 && !hasDamage)
            return;

        var sb = new StringBuilder();

        sb.AppendLine(
            $"[RL Snapshot] frame={debugFrame}, mode={mode}, units={count}");

        for (int i = 0; i < count; i++)
        {
            var o = obsArray[i];
            var action = (UnitState)actionArray[i].action_index;

            bool canAttack =
                em.IsComponentEnabled<CanToAttackTag>(entities[i]);

            sb.AppendLine(
                $"unit={i} " +
                $"tendency={o.AttackTendency:F2} " +
                $"dx={o.dx:F2} dy={o.dy:F2} " +
                $"delta={o.delta:F3} " +
                $"inRange={o.InAttackRange} " +
                $"canAttack={canAttack} " +
                $"selfDmg={o.selfHp:F2} " +
                $"targetDmg={o.targetHp:F2} " +
                $"reward={o.reward:F3} " +
                $"action={action}({actionArray[i].action_index})");
        }

        Debug.Log(sb.ToString());
    }

    bool TryGetDistanceState(
    Entity entity,
    float attackTendency,
    out float actualDistance,
    out float desiredDistance)
    {
        actualDistance = 0f;
        desiredDistance = 0f;

        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        var target = em.GetComponentData<CNearTarget>(entity).entity;

        if (target == Entity.Null || !em.Exists(target))
            return false;

        var selfPos =
            em.GetComponentData<LocalTransform>(entity).Position;

        var targetPos =
            em.GetComponentData<LocalTransform>(target).Position;

        actualDistance =
            math.distance(selfPos, targetPos);

        var targetTeam =
            em.GetComponentData<UnitEnumComponent>(target).type;

        float detectDistance;
        float attackDistance;

        if (targetTeam == UnitEnum.Enmy)
        {
            detectDistance = rLManager.EnmyData.DetectDistance;
            attackDistance = rLManager.EnmyData.AttackDistance;
        }
        else
        {
            detectDistance = rLManager.AllyData.DetectDistance;
            attackDistance = rLManager.AllyData.AttackDistance;
        }

        float t =
            math.saturate(( attackTendency + 1f) * 0.5f);

        if (t < 0.5f)
        {
            desiredDistance = math.lerp(
                detectDistance * 1.2f,
                attackDistance,
                t * 2f);
        }
        else
        {
            desiredDistance = math.lerp(
                attackDistance,
                0f,
                (t - 0.5f) * 2f);
        }

        return true;
    }
}

public enum RunMode { Training, Inference }
