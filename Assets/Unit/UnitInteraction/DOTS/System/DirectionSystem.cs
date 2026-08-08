using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

[UpdateAfter(typeof(SelectSystem))]
partial struct DirectionSystem : ISystem
{
    PhysicsWorldSingleton physics;
    public bool RequestToggle; 

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        physics = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
    }

    // [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        physics = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        
        var lookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        lookup.Update(ref state);

        foreach(var (request, entity) in SystemAPI.Query<RefRO<CDirectionRequest>>().WithEntityAccess().WithChangeFilter<CDirectionRequest>())
        {
            RequestToggle = true;
            SystemAPI.SetSingleton(new CDirectionRequestPending { Value = RequestToggle });
        }

        if (Input.GetMouseButtonDown(0) && RequestToggle)//Up으로 하면 ui 땔때 인식
        {
            UnityEngine.Ray mouseRay = Camera.main.ScreenPointToRay(Input.mousePosition);

            foreach(var (request, response, entity) in SystemAPI.Query<RefRO<CDirectionRequest>, RefRW<CDirectionResponse>>().WithEntityAccess())
            {
                RequestToggle = false;
                SystemAPI.SetSingleton(new CDirectionRequestPending { Value = RequestToggle });

                if (request.ValueRO.entity == Entity.Null)
                {
                    var ray = new RaycastInput
                    {
                        Start = mouseRay.origin,
                        End = mouseRay.origin + mouseRay.direction * 100f,
                        Filter = new CollisionFilter
                        {
                            BelongsTo = ~0u,
                            CollidesWith = 1u << request.ValueRO.CollisionLayer,
                            GroupIndex = 0
                        }
                    };

                    var result = physics.CastRay(ray, out var hit);
                    if (result)
                    {
                        response.ValueRW.entity = hit.Entity;
                        response.ValueRW.TargetPos = new Unity.Mathematics.float3(hit.Position.x, 0, hit.Position.z);
                    }
                }
                else
                {
                    //? 이것도 raycast할까? 
                    if (lookup.HasComponent(request.ValueRO.entity))
                    {
                        var pos = lookup[request.ValueRO.entity].Position;
                        response.ValueRW.entity = request.ValueRO.entity;
                        response.ValueRW.TargetPos = new Unity.Mathematics.float3(pos.x, 0, pos.z);
                    }
                }

                state.EntityManager.SetComponentEnabled<CDirectionRequest>(entity, false);
            }   

        }

        foreach(var (response, entity) in SystemAPI.Query<RefRO<CDirectionResponse>>().WithEntityAccess().WithChangeFilter<CDirectionResponse>())
        {
            Debug.Log($"Edit : {response.ValueRO.TargetPos}");
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }
}