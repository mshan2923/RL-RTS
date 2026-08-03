using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

public class SelectUnitEnum : MonoBehaviour
{
    EntityManager em;
    public UnitEnumDB unitEnumDB;
    public EntityQuery query;

    Dictionary<UnitEnum, HashSet<Entity>> previousSets = new();

    void Start()
    {
        em = World.DefaultGameObjectInjectionWorld.EntityManager;
        var desc = new EntityQueryDesc
        {
            All = new ComponentType[] { typeof(UnitEnumComponent), typeof(UnitComponent), typeof(SelectComponent) }
        };
        query = em.CreateEntityQuery(desc);
    }

    void Update()
    {
        if (Input.GetMouseButtonUp(0))
        {
            GatherAndInvoke();
        }
    }

    // 리스너 (MonoBehaviour 여러 개)
    void OnEnable() => SelectionEvents.OnNothingSelected += HandleNothingSelected;

    void OnDisable() => SelectionEvents.OnNothingSelected -= HandleNothingSelected;

    private void HandleNothingSelected()
    {
        Debug.Log("Empty Event");//! 목표 지정 시에도 호출됨
    }

    async void GatherAndInvoke()
    {
        await Awaitable.NextFrameAsync(); // SelectComponent enable이 구조적 변경 동기화될 시간 확보

        var data = query.ToComponentDataArray<UnitEnumComponent>(Allocator.TempJob);
        var entitiesArr = query.ToEntityArray(Allocator.TempJob);
        var unitMap = new NativeParallelMultiHashMap<UnitEnumComponent, Entity>(query.CalculateEntityCount(), Allocator.TempJob);

        new MakeSet
        {
            data = data.AsReadOnly(),
            unitMap = unitMap.AsParallelWriter(),
            Entities = entitiesArr.AsReadOnly()
        }.Schedule(data.Length, JobsUtility.MaxJobThreadCount).Complete();

        var activeTypesThisFrame = new HashSet<UnitEnum>();


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

            if (currentSet.Count > 0) activeTypesThisFrame.Add(v.unitType);

            bool hasPrevious = previousSets.TryGetValue(v.unitType, out var prevSet);
            bool changed = !hasPrevious || !currentSet.SetEquals(prevSet);

            if (changed && currentSet.Count > 0)
            {
                var targetEntities = new NativeArray<Entity>(currentSet.Count, Allocator.TempJob);
                int idx = 0;
                foreach (var e in currentSet) targetEntities[idx++] = e;

                try { OnInvoke?.Invoke(v.unitType, targetEntities.ToArray()); }
                finally { targetEntities.Dispose(); }
            }

            previousSets[v.unitType] = currentSet;
        }

        // 이번 클릭으로 뭔가는 선택됐는데 특정 타입은 빠졌다면 그 타입만 지우라는 신호
        if (activeTypesThisFrame.Count > 0)
        {
            foreach (var v in unitEnumDB.Types)
            {
                if (!activeTypesThisFrame.Contains(v.unitType))
                    OnInvoke?.Invoke(v.unitType, new Entity[0]);
            }
        }

        data.Dispose();
        entitiesArr.Dispose();
        unitMap.Dispose();
    }

    public static event Action<UnitEnum, Entity[]> OnInvoke;
    public static event System.Action OnNothingSelected;

    public struct MakeSet : IJobParallelFor
    {
        public NativeArray<UnitEnumComponent>.ReadOnly data;
        public NativeArray<Entity>.ReadOnly Entities;
        public NativeParallelMultiHashMap<UnitEnumComponent, Entity>.ParallelWriter unitMap;

        public void Execute(int index) => unitMap.Add(data[index], Entities[index]);
    }

    public static class SelectionEvents
    {
        public static event System.Action OnNothingSelected;

        public static void RaiseNothingSelected() => OnNothingSelected?.Invoke();
    }
}