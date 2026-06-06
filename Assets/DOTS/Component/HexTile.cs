using Unity.Entities;

public struct HexTile : IComponentData
{
    public int X;
    public int Z;
    // 나중에 점령 상태나 유닛 존재 여부 등을 여기서 관리
    public int Type; 
}