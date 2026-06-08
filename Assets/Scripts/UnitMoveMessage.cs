using UnityEngine;

[System.Serializable]
public struct UnitMoveMessage
{
    public int UnitId;
    public int TargetX;
    public int TargetZ;
}