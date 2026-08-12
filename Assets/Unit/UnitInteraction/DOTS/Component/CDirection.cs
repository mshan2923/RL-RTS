
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 순수 -> DOTS 으로 위치 지시
/// </summary>
public struct CDirectionRequest : IComponentData , IEnableableComponent
{
    public Entity entity;
    public int CollisionLayer;
    public bool NeedRaycast;
}
public struct CDirectionResponse : IComponentData
{
    public Entity entity;
        public float3 TargetPos;
}

/// <summary>
/// CUnitState에 "이번 프레임에 새 목표를 받아야 하는지" 표시용 플래그를 별도 IEnableableComponent로 분리
/// </summary>
public struct CCommandPending : IComponentData, IEnableableComponent { }
//순수 상태 표시용
public struct CDirectionRequestPending : IComponentData { public bool Value; }
public struct CSelectColor : IComponentData
{
    public float4 color;
}