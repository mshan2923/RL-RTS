using System.Runtime.InteropServices;
using Unity.Entities;
using UnityEngine;


public static class RLConstants
{
    public const int OBS_DIM = 7; // dx, dy, delta, selfHp, targetHp, InAttackRange, distToEdge
    public const int NUM_ACTIONS = 4; // MoveToward, HoldPosition, Retreat, Action
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
    public float distToEdge; // 추가
    public float reward;
    public int done;
}

    [StructLayout(LayoutKind.Sequential)]
public struct CActionData : IComponentData
{
    public int action_index; // Discrete니까 int 하나?
}