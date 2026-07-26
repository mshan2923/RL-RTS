using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

public class SelectUnitEnum : MonoBehaviour
{
    EntityManager em;
    EntityQueryDesc desc;

    public int enumAmount;
    public UnitEnumDB unitEnumDB;
    public float dragInterval = 0.1f;

    public static event Action<UnitEnum, NativeArray<Entity>, bool> OnInvoke;
    public static event Action<int> OnUpdater;
    public static event Action<List<UnitEnum>> OnEndInvoke;

    public EntityQuery query;

    enum DragState { Idle, Pressed, Dragging }
    DragState state = DragState.Idle;
    float dragTimer;
    bool isGathering = false;
    Dictionary<UnitEnum, HashSet<Entity>> previousSets = new();


    void Start()
    {
        em = World.DefaultGameObjectInjectionWorld.EntityManager;
        desc = new EntityQueryDesc
        {
            All = new ComponentType[] { typeof(UnitEnumComponent), typeof(UnitComponent), typeof(SelectComponent) }
        };
        query = em.CreateEntityQuery(desc);

        enumAmount = Enum.GetNames(typeof(UnitEnum)).Length;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            state = DragState.Pressed;
            dragTimer = 0f;
            GatherAndInvoke(true); // 클릭 시작 시점 1회
        }
        else if (Input.GetMouseButton(0))
        {
            state = DragState.Dragging;
            dragTimer += Time.deltaTime;
            if (dragTimer >= dragInterval)
            {
                dragTimer = 0f;
                GatherAndInvoke(false); // 드래그 중 일정 간격마다
            }
        }
        else if (Input.GetMouseButtonUp(0) && state != DragState.Idle)
        {
            state = DragState.Idle;

            // previousSets 중 실제로 1개 이상 들어있던 타입만 추림
            var activeTypes = new List<UnitEnum>();
            foreach (var kv in previousSets)
            {
                if (kv.Value.Count > 0)
                    activeTypes.Add(kv.Key);
            }

            previousSets.Clear();
            OnEndInvoke?.Invoke(activeTypes);
        }

    }

    /// <summary>
    /// 너무 빨리 바뀌면 반영이 안되는 버그? 있음
    /// </summary>
    /// <param name="isStart"></param>
    async void GatherAndInvoke(bool isStart)
    {
        if (isGathering) return;
        isGathering = true;

        await Awaitable.NextFrameAsync();

        var data = query.ToComponentDataArray<UnitEnumComponent>(Allocator.TempJob);
        var entitiesArr = query.ToEntityArray(Allocator.TempJob);

        var unitMap = new NativeParallelMultiHashMap<UnitEnumComponent, Entity>(query.CalculateEntityCount(), Allocator.TempJob);
        new MakeSet
        {
            data = data.AsReadOnly(),
            unitMap = unitMap.AsParallelWriter(),
            Entities = entitiesArr.AsReadOnly()
        }.Schedule(data.Length, JobsUtility.MaxJobThreadCount).Complete();

        OnUpdater?.Invoke(data.Length);

        foreach (var v in unitEnumDB.Types)
        {
            var searchKey = new UnitEnumComponent(v.unitType);
            int count = unitMap.CountValuesForKey(searchKey);

            var currentSet = new HashSet<Entity>();
            if (count > 0 && unitMap.TryGetFirstValue(searchKey, out Entity entity, out var iterator))
            {
                do { currentSet.Add(entity); }
                while (unitMap.TryGetNextValue(out entity, ref iterator));
            }

            bool hasPrevious = previousSets.TryGetValue(v.unitType, out var prevSet);
            bool changed = isStart || !hasPrevious || !currentSet.SetEquals(prevSet);

            if (changed)
            {
                var targetEntities = new NativeArray<Entity>(currentSet.Count, Allocator.Temp);

                int idx = 0;
                foreach (var e in currentSet) targetEntities[idx++] = e;

                try
                {
                    OnInvoke?.Invoke(v.unitType, targetEntities, isStart);
                }
                finally
                {
                    targetEntities.Dispose();
                }
            }

            previousSets[v.unitType] = currentSet; // 없어졌으면 빈 셋으로 갱신 → 다음 비교 기준
        }

        data.Dispose();
        entitiesArr.Dispose();
        unitMap.Dispose();
        isGathering = false;
    }

    public struct MakeSet : IJobParallelFor
    {
        public NativeArray<UnitEnumComponent>.ReadOnly data;
        public NativeArray<Entity>.ReadOnly Entities;
        public NativeParallelMultiHashMap<UnitEnumComponent, Entity>.ParallelWriter unitMap;

        public void Execute(int index)
        {
            unitMap.Add(data[index], Entities[index]);
        }
    }
}