using Unity.Collections;
using Unity.Entities;

public enum UnitState : int
{
    None, Move, Stop , Attack , Action
}
public struct CUnitState : IComponentData
{
    public FixedString64Bytes Debug;
    public UnitState unitState;
}
