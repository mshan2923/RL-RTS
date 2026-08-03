using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;

public abstract class ActionAbstract : UnitEnumInterface
{
    [LayerField] public int layer;

    protected abstract FixedString64Bytes DebugLog();
    protected abstract string Name();

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
        data.CollisionLayer = layer;
        data.NeedRaycast = true;
        em.SetComponentData(entities[0], data);

        // Debug.Log($"CTX Entities : {ctx.unitEnum} => {ctx.Entities.Length}");

        foreach(var v in ctx.Entities)
        {
            em.SetComponentData(v, new CUnitState
            {
                Debug = DebugLog(),
                unitState = UnitState.Action
            });

            em.SetComponentEnabled<CCommandPending>(v, true);
        }
    }

    public string GetLabel(UnitActionContext ctx)
    {
        
        return $"{ctx.unitEnum} {Name()}";
    }
}
