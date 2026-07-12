using System.Runtime.InteropServices;
using Unity.Entities;


namespace RL_StepByStep
{
    /// <summary>facing 없음. 목표까지 상대 위치만 관측. Python struct: "&lt;i3fi"</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Phase3Observation : IComponentData
    {
        public int unitId;
        public float dx; // 목표까지 상대 x (정규화)
        public float dy; // 목표까지 상대 y (정규화)
        public float reward;
        public int done;
    }

    /// <summary>0~5: 6방향 중 하나로 즉시 이동 (절대 axial 방향, facing 없음)</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Phase3Action : IComponentData
    {
        // public int direction;
        public float dx;
        public float dy;
    }
}