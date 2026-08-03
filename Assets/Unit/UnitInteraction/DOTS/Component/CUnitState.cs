using Unity.Collections;
using Unity.Entities;

public enum UnitState
{
    None, Move, Stop , Attack , Action
}
public struct CUnitState : IComponentData
{
    public FixedString64Bytes Debug;
    public UnitState unitState;
}
