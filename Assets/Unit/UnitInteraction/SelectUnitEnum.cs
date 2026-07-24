using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

public class SelectUnitEnum : MonoBehaviour
{
    EntityManager em;
    EntityQueryDesc desc;

    public int enumAmount; 
    public UnitEnumDB unitEnumDB;

    async void Start()
    {
        em = World.DefaultGameObjectInjectionWorld.EntityManager;
        desc = new EntityQueryDesc
        {
            All = new ComponentType[] {typeof(UnitEnumComponent), typeof(UnitComponent), typeof(SelectComponent)}
        };
        enumAmount = Enum.GetNames(typeof(UnitEnum)).Length;


        //! unitEnumDB 의 이벤트 연결


        await Loop(0.1f);
    }


    async Awaitable Loop(float time)
    {
        while (true && isActiveAndEnabled )
        {
            await Awaitable.WaitForSecondsAsync(time);

            

            if (Input.GetMouseButton(0))
            {

                var query = em.CreateEntityQuery(desc);
                var data = query.ToComponentDataArray<UnitEnumComponent>(Allocator.TempJob);
                var Entities = query.ToEntityArray(Allocator.TempJob);

                var unitMap = new NativeParallelMultiHashMap<UnitEnumComponent, Entity>(query.CalculateEntityCount() ,Allocator.TempJob);
                new MakeSet
                {
                    data = data.AsReadOnly(),
                    unitMap = unitMap.AsParallelWriter(),
                    Entities = Entities.AsReadOnly()

                }.Schedule(data.Length, JobsUtility.MaxJobThreadCount).Complete();


                foreach (var v in unitEnumDB.Types)
                {
                    var searchKey = new UnitEnumComponent(v.type);
                    int count = unitMap.CountValuesForKey(searchKey);

                    if (count > 0)
                    {
                        var targetEntities = new NativeArray<Entity>(count, Allocator.Temp);
                        try
                        {
                            int index = 0;
                            if (unitMap.TryGetFirstValue(searchKey, out Entity entity, out var iterator))
                            {
                                do
                                {
                                    targetEntities[index++] = entity;
                                }
                                while (unitMap.TryGetNextValue(out entity, ref iterator));
                            }

                            foreach (var k in v.Pure)
                            {
                                if (k is UnitEnumInterface unitEnumInterface)
                                {
                                    unitEnumInterface.Invoke(v.type, targetEntities);//!====== 살아있는 오브젝트가 아님
                                    Debug.Log("Invoke");
                                }
                            }

                                
                            

                            // {
                            //     var types = TypeCache.GetTypesDerivedFrom<UnitEnumInterface>()
                            //         .Where(t => !t.IsAbstract && !t.IsInterface);

                            //     foreach (var type in types)
                            //     {
                            //         if (typeof(MonoBehaviour).IsAssignableFrom(type))
                            //             continue; // MonoBehaviour는 씬에서 찾아야 함, new()로 못 만듦

                            //         var instance = (UnitEnumInterface)Activator.CreateInstance(type);
                            //         instance.Invoke(v.type, targetEntities);
                            //     }   

                            //     var sceneLogics = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                            //         .OfType<UnitEnumInterface>();
                            //     foreach (var logic in sceneLogics)
                            //         logic.Invoke(v.type, targetEntities);
                            // }
                        }
                        finally
                        {
                            targetEntities.Dispose();
                        }
                    }
                }

                data.Dispose();
                Entities.Dispose();
                unitMap.Dispose();
            }else
            {
                foreach (var v in unitEnumDB.Types)
                {
                    //Debug.Log($"DB Type 순회 중: {v.type}, 인터페이스 개수: {v.Interface?.Count ?? 0}");
                    foreach (var k in v.Pure)
                        if (k is UnitEnumInterface unitEnumInterface)
                        {
                            unitEnumInterface.EndInvoke(v.type);
                        }

                    
                }


            }

        }
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
