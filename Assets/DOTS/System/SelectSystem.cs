using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;

partial struct SelectSystem : ISystem
{
    private bool isInitialized;
    private bool isDragging;
    private Vector3 startScreenPos;

    EntityQuery directionQuery;
    EntityQuery selectQuery;

    public void OnCreate(ref SystemState state)
    {
        isDragging = false;
        isInitialized = false;


        using var build = new EntityQueryBuilder(Allocator.Temp);
        build.WithAll<CDirectionRequest>();
        directionQuery = build.Build(ref state);

        using var SelectBuild = new EntityQueryBuilder(Allocator.Temp);
        SelectBuild.WithDisabled<SelectComponent>();
        selectQuery = SelectBuild.Build(ref state);
    }

    public void OnDestroy(ref SystemState state) { }

    public void OnUpdate(ref SystemState state)
    {
        if (!isInitialized)
        {
            {
                int count = selectQuery.CalculateEntityCount();
                if (count == 0)
                {
                    return;
                }
            }

            isInitialized = true;

            foreach (var (_, entity) in SystemAPI.Query<SelectComponent>().WithEntityAccess())
                SystemAPI.SetComponentEnabled<SelectComponent>(entity, false);
        }

        bool isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
        {
            isDragging = true;
            startScreenPos = Input.mousePosition;


            return;
        }

        Debug.Log("isDragging");

        if (!isDragging) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Rect rect = GetScreenRect(startScreenPos, Input.mousePosition);

        // 드래그 중: 실제 선택 상태는 그대로 두고, 색상만 미리보기로 갱신
        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<UnitComponent>().WithEntityAccess())
        {
            Debug.Log("--");
            Vector3 screenPos = cam.WorldToScreenPoint(transform.ValueRO.Position);
            bool isInside = screenPos.z > 0 && rect.Contains(new Vector2(screenPos.x, screenPos.y));
            bool alreadySelected = state.EntityManager.IsComponentEnabled<SelectComponent>(entity);

            bool previewSelected = isInside || (isShiftPressed && alreadySelected);

            state.EntityManager.SetComponentData(entity, new URPMaterialPropertyBaseColor
            {
                Value = previewSelected ? new float4(1, 0, 0, 1) : new float4(1, 1, 1, 1)
            });
            
        }

        if (!Input.GetMouseButtonUp(0)) return;

        // 뗀 순간: 여기서만 실제 선택 상태를 확정
        isDragging = false;

        var hitEntities = new System.Collections.Generic.List<Entity>();
        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<UnitComponent>().WithEntityAccess())
        {
            Vector3 screenPos = cam.WorldToScreenPoint(transform.ValueRO.Position);
            if (screenPos.z > 0 && rect.Contains(new Vector2(screenPos.x, screenPos.y)))
                hitEntities.Add(entity);
        }

        // 아무것도 안 걸렸으면 기존 선택은 절대 건드리지 않음
        if (hitEntities.Count > 0)
        {
            if (!isShiftPressed)
            {
                foreach (var (sel, entity) in SystemAPI.Query<RefRW<SelectComponent>>().WithEntityAccess())
                    state.EntityManager.SetComponentEnabled<SelectComponent>(entity, false);
            }
            foreach (var entity in hitEntities)
                state.EntityManager.SetComponentEnabled<SelectComponent>(entity, true);
        }else
        {
            // if (SystemAPI.GetSingleton<CDirectionRequestPending>().Value)
            //     Debug.Log("Directing");

            SelectUnitEnum.SelectionEvents.RaiseNothingSelected();
        }

        // 색상을 실제 선택 상태로 최종 동기화 (미리보기 잔상 제거)
        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<UnitComponent>().WithEntityAccess())
        {
            bool selected = state.EntityManager.IsComponentEnabled<SelectComponent>(entity);
            state.EntityManager.SetComponentData(entity, new URPMaterialPropertyBaseColor
            {
                Value = selected ? new float4(1, 0, 0, 1) : new float4(1, 1, 1, 1)
            });
        }
    }

    private Rect GetScreenRect(Vector3 p1, Vector3 p2)
    {
        float minX = Mathf.Min(p1.x, p2.x);
        float maxX = Mathf.Max(p1.x, p2.x);
        float minY = Mathf.Min(p1.y, p2.y);
        float maxY = Mathf.Max(p1.y, p2.y);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}