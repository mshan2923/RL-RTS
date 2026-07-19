using Unity.Entities;

public struct MapConfig : IComponentData
{
    public Entity HexPrefab;
    public int Width;
    public int Height;
    public float Radius;
    public static readonly float FixedStepSize = 0.02f;
}