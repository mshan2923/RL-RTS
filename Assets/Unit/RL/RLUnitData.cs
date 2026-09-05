using System.Runtime.InteropServices;
using Unity.Entities;
using UnityEngine;


public static class RLConstants
{
    public const int OBS_DIM = 8; // dx, dy, delta, selfHp, targetHp, InAttackRange, distToEdge , AttackTendency(추가)
    public const int NUM_ACTIONS = 3; // MoveToward, HoldPosition, Retreat
}

    [StructLayout(LayoutKind.Sequential)]
public struct CObservation : IComponentData
{
    public int unit_id;
    public float dx, dy;
    public float delta;
    public float selfHp, targetHp;
    public int alive;
    public int InAttackRange;
    public float distToEdge;
    public float AttackTendency; 
    public float reward;
    public int done;
}

    [StructLayout(LayoutKind.Sequential)]
public struct CActionData : IComponentData
{
    public int action_index; // Discrete니까 int 하나?
}