using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;

public class StopCommand : UnitEnumInterface
{
    public bool CanExecute(UnitActionContext ctx)
    {
        return true;
    }

    public void Execute(UnitActionContext ctx)
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        using var build = new EntityQueryBuilder(Allocator.Temp);
        build.WithDisabled<CDirectionRequest>();
        var Query = em.CreateEntityQuery(build);

        if (Query.IsEmpty) return;
        //var entity = Query.GetSingletonEntity();
        using var entities = Query.ToEntityArray(Allocator.Temp);
        using var datas = Query.ToComponentDataArray<CDirectionRequest>(Allocator.Temp);
        // var trans = new ComponentLookup<LocalTransform>();
        // Debug.Log($"CTX Entities : {ctx.unitEnum} => {ctx.Entities.Length}");

        foreach(var v in ctx.Entities)
        {
            em.SetComponentData(v, new CUnitState
            {
                Debug = "Pure - StopCommand.cs",
                unitState = UnitState.Stop
            });

            em.SetComponentEnabled<CCommandPending>(v, true);
            // moveData.

            em.SetComponentData(entities[0], new CDirectionResponse
            {
                TargetPos = float3.zero
            });//변경을 알리기만 함!
        }
    }

    public string GetLabel(UnitActionContext ctx)
    {
        
        return $"{ctx.unitEnum} Stop";
    }
}
