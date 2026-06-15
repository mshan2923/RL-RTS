using System.ComponentModel;
using Unity.Entities;
using Unity.Mathematics;
public struct UnitComponent : IComponentData
{
    public int Id; // 서버와 통신할 때 쓸 고유 번호
}
public struct MoveTargetComponent : IComponentData
{
    public float PrevBaseDist;
    public float3 Target;
}
public struct DetectWallNormalize : IComponentData
{
    public float n0, n1, n2, n3, n4, n5;
    public bool isWall;
}