using Unity.Collections;
using Unity.Entities;


public enum UnitState : int
{
    MoveToward, HoldPosition, Retreat, Action
}
public struct CUnitState : IComponentData
{
    public FixedString64Bytes Debug;
    public UnitState unitState;
}
