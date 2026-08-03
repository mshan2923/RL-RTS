using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;

public class MoveTo : UnitEnumInterface
{
    // [LayerField]public  int temp;
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
        em.SetComponentEnabled<CDirectionRequest>(entities[0], true);
        //var direction = Query.GetSingleton<CDirection>();

        var data = datas[0];
        data.NeedRaycast = true;
        em.SetComponentData(entities[0], data);

        // Debug.Log($"CTX Entities : {ctx.unitEnum} => {ctx.Entities.Length}");

        foreach(var v in ctx.Entities)
        {
            em.SetComponentData(v, new CUnitState
            {
                Debug = "Pure - MoveTo.cs",
                unitState = UnitState.Move
            });

            em.SetComponentEnabled<CCommandPending>(v, true);
        }
    }

    public string GetLabel(UnitActionContext ctx)
    {
        
        return $"{ctx.unitEnum} Move";
    }
}
