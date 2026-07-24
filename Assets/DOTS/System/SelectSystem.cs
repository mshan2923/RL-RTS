using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.LowLevelPhysics2D;

[UpdateAfter(typeof(BakingSystem))]
partial struct SelectSystem : ISystem
{
    private bool isInitialized;
    private bool isDragging;
    private Vector3 startScreenPos;

    public void OnCreate(ref SystemState state)
    {
        isDragging = false;
        isInitialized = false;
    }

    public void OnDestroy(ref SystemState state) { }

    public void OnUpdate(ref SystemState state)
    {
        if (!isInitialized)
        {
            isInitialized = true;
            
            // 첫 프레임에 시작하자마자 비활성화 처리
            foreach (var (_, entity) in SystemAPI.Query<SelectComponent>().WithEntityAccess())
            {
                SystemAPI.SetComponentEnabled<SelectComponent>(entity, false);
            }
        }

        // 이후 평소 로직 수행
        if (!isInitialized) return;

        // var physics = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        bool isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            startScreenPos = Input.mousePosition;
        }

        if (Input.GetMouseButton(0))//(isDragging && Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            Vector3 endScreenPos = Input.mousePosition;

            if (!isShiftPressed)
            {
                foreach (var (selectComp, entity) in SystemAPI.Query<RefRW<SelectComponent>>().WithEntityAccess())
                {
                    state.EntityManager.SetComponentEnabled<SelectComponent>(entity, false);
                    state.EntityManager.SetComponentData(entity, new URPMaterialPropertyBaseColor
                    {
                        Value = new Unity.Mathematics.float4 (1,1,1,1)
                    });
                }
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                // Y축 뒤집는 연산 제거하고 Input.mousePosition 기준(Bottom-Up)으로 통일
                Rect rect = GetScreenRect(startScreenPos, endScreenPos);

                foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<UnitComponent>().WithEntityAccess())
                {
                    Vector3 screenPos = cam.WorldToScreenPoint(transform.ValueRO.Position);
                    
                    // 1. screenPos.z > 0 : 카메라 뒤에 있는 유닛 제외
                    // 2. rect.Contains : 화면 좌표계 기준으로 박스 내부에 있는지 확인
                    if (screenPos.z > 0 && rect.Contains(new Vector2(screenPos.x, screenPos.y)))
                    {
                        state.EntityManager.SetComponentEnabled<SelectComponent>(entity, true);

                        state.EntityManager.SetComponentData(entity, new URPMaterialPropertyBaseColor
                        {
                            Value = new Unity.Mathematics.float4 (1,0,0,1)
                        });
                    }
                }
            }
        }
    }

    private Rect GetScreenRect(Vector3 p1, Vector3 p2)
    {
        float minX = Mathf.Min(p1.x, p2.x);
        float maxX = Mathf.Max(p1.x, p2.x);
        float minY = Mathf.Min(p1.y, p2.y);
        float maxY = Mathf.Max(p1.y, p2.y);
        
        // Bottom-Up 기준으로 Rect 생성 (Unity 화면 좌표계와 일치)
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}
