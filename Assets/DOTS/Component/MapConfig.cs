using Unity.Entities;

public struct MapConfig : IComponentData
{
    public Entity HexPrefab;
    public int Width;
    public int Height;
    // public float Radius;//HexMetrics.outterRadius 으로 대체
    public static readonly float FixedStepSize = 0.02f;
}