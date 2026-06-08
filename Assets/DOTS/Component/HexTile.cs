using Unity.Entities;

public struct HexTile : IComponentData
{
    public int X;
    public int Z;
    // 나중에 점령 상태나 유닛 존재 여부 등을 여기서 관리
    public GroupType OwnerID; // 0:중립, 1:플레이어, 2:적
    public bool IsOccupied;
    public bool JustCaptured;
}

public enum GroupType : byte
{
    None, Ally, Enmy
}