using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// ECS에서 접근용
public class WebSocketManagerComponent : IComponentData
{
    public NativeQueue<WebSocketManager.ActionData> ActionQueue;
    public NativeQueue<WebSocketManager.StateData> StateQueue;

    void Awake()
    {
        ActionQueue = new NativeQueue<WebSocketManager.ActionData>(Allocator.Persistent);
        StateQueue = new NativeQueue<WebSocketManager.StateData>(Allocator.Persistent);

        var world = World.DefaultGameObjectInjectionWorld;
        var _entity = world.EntityManager.CreateEntity();
        world.EntityManager.AddComponentObject(_entity, new WebSocketManagerComponent
        {
            ActionQueue = ActionQueue,
            StateQueue = StateQueue
        });

        Debug.Log("Create");
    }
    void OnDestroy()
    {
        ActionQueue.Dispose();
        StateQueue.Dispose();
    }
}