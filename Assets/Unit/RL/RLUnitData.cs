using System.Runtime.InteropServices;
using Unity.Entities;
using UnityEngine;

    [StructLayout(LayoutKind.Sequential)]
    public struct CObservation : IComponentData
{
    public int unit_id;
    public float dx, dy, selfHp, targetHp;
    public int alive;
    public float reward;
    public int done;
}

    [StructLayout(LayoutKind.Sequential)]
public struct CActionData : IComponentData
{
    public int action_index; // Discrete니까 int 하나?
}