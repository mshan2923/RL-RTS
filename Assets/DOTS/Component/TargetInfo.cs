using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 유닛의 현재 점령 목표 지점 정보를 담는 컴포넌트입니다.
/// </summary>
public struct TargetInfo : IComponentData
{
    // 실제 좌표 (예: x, y, z)
    public float3 Position;

    // 타겟이 활성화되어 있는지 여부 (0 또는 1로 처리 시 학습 모델에서 활용 용이)
    public bool IsActive;

}