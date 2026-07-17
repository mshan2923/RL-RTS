using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class Phase3Connecter : MonoBehaviour
{
    private static Phase3Connecter _instace;
    public static Phase3Connecter Instace {get => _instace;}

    public GameObject UnitPrefab;
    public int Amount;

    private List<Entity> Unit = new();
    public List<(GameObject, Entity)> Units = new();

    EntityManager em;

    NativeArray<Entity> spawned;
    public float Scale = 0.25f;

    private void Awake() {
        if (_instace == null) _instace = this;
    }

    void Start()
    {
        em = World.DefaultGameObjectInjectionWorld.EntityManager;
        var query = em.CreateEntityQuery(typeof(Phase3UnitManagerComponent));
        var unitManager = query.GetSingleton<Phase3UnitManagerComponent>();


        spawned = em.Instantiate(unitManager.Prefab, Amount, Allocator.Persistent);
        Unit.AddRange(spawned);
    

        for(int i = 0; i < Unit.Count; i++)
        {
            var trans = GameObject.Instantiate(UnitPrefab, transform);
            Units.Add((trans, Unit[i]));

            em.SetName(Unit[i], $"Unit {i}");
            em.SetComponentData(Unit[i], new UnitComponent
            {
               Id = i 
            });
            em.SetComponentData(Unit[i], new LocalTransform
            {
                Position = float3.zero,
                Scale = Scale
            });

            em.SetComponentData(Unit[i], new MoveTargetComponent
            {
                MoveTo = float3.zero
            });
            em.SetComponentData(Unit[i], new DetectWallNormalize
            {
                n0 = 1,
                n1 = 1,
                n2 = 1,
                n3 = 1,
                n4 = 1,
                n5 = 1
            });

        }

        // var unitQuery = em.CreateEntityQuery(typeof(UnitComponent), typeof(LocalTransform));
        // var trans = unitQuery.ToEntityArray(Allocator.TempJob);
        // Unit.AddRange(trans);
        // Debug.Log(Unit.Count);
    }
    void OnDestroy()
    {
        spawned.Dispose();
    }

    public void SetTransform(int i , Vector3 pos)
    {
        em.SetComponentData(Unit[i], new LocalTransform
        {
            Position = pos,
            Scale = Scale
        });

        em.SetComponentData(Unit[i], new MoveTargetComponent
        {
            MoveTo = pos
        });
    }
    public void SyncTransform(int i , Vector3 pos)
    {
        em.SetComponentData(Unit[i], new MoveTargetComponent
        {
            MoveTo = pos
        });
    }


}
