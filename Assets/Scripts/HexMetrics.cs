using Unity.Mathematics;

public static class HexMetrics
{
    public const float outerRadius = .51f;
    public const float innerRadius = outerRadius * 1.73205f / 2f; // √3/2

    private static readonly int2 Even0 = new(1, 1);
    private static readonly int2 Even1 = new(1, 0);
    private static readonly int2 Even2 = new(1, -1);
    private static readonly int2 Even3 = new(0, -1);
    private static readonly int2 Even4 = new(-1, 0);
    private static readonly int2 Even5 = new(0, 1);

    private static readonly int2 Odd0 = new(0, 1);
    private static readonly int2 Odd1 = new(1, 0);
    private static readonly int2 Odd2 = new(0, -1);
    private static readonly int2 Odd3 = new(-1, -1);
    private static readonly int2 Odd4 = new(-1, 0);
    private static readonly int2 Odd5 = new(-1, 1);


    private static int3 CubeRound(float x, float y, float z)
    {
        int rx = (int)math.round(x);
        int ry = (int)math.round(y);
        int rz = (int)math.round(z);

        float xDiff = math.abs(rx - x);
        float yDiff = math.abs(ry - y);
        float zDiff = math.abs(rz - z);

        if (xDiff > yDiff && xDiff > zDiff)
            rx = -ry - rz;
        else if (yDiff > zDiff)
            ry = -rx - rz;
        else
            rz = -rx - ry;

        return new int3(rx, ry, rz);
    }

    public static int2 WorldToOffset(float3 position)
    {
        // Pointy-top 정확한 world → axial
        float q = (position.x * math.sqrt(3f) / 3f - position.z / 3f) / outerRadius;
        float r = position.z * 2f / 3f / outerRadius;

        int3 cube = CubeRound(q, -q - r, r);

        int row = cube.z;
        int col = cube.x + (cube.z - (cube.z & 1)) / 2;
        return new int2(col, row);
    }

    public static float3 OffsetToWorld(int2 offset)
    {
        float x = offset.x * (innerRadius * 2f);
        if ((offset.y & 1) == 1)
            x += innerRadius;
        float z = offset.y * (outerRadius * 1.5f);
        return new float3(x, 0f, z);
    }

    public static int WorldYawToIndex(float yawDegrees)
    {
        // 0~360 정규화 후 60도 단위로 반올림
        float normalized = ((yawDegrees % 360f) + 360f) % 360f;
        return (int)math.round(normalized / 60f) % 6;
    }
    public static int2 WorldToOffsetWithYaw(float3 position, float yawDegrees)
    {
        float rad = math.radians(yawDegrees);
        float3 dir = new float3(math.sin(rad), 0f, math.cos(rad));

        return WorldToOffset(position + dir * innerRadius * 2f);
    }
    public static bool IsValidCell(int2 cell, int minCol, int maxCol, int minRow, int maxRow)
    {
        return cell.x >= minCol && cell.x <= maxCol &&
               cell.y >= minRow && cell.y <= maxRow;
    }


    public static int2 GetNeighborOffset(int2 current, int rot)
    {
        int index = rot % 6;
        bool isEven = (current.y & 1) == 0;

        if (isEven)
        {
            return index switch
            {
                0 => Even0, 1 => Even1, 2 => Even2, 3 => Even3, 4 => Even4, 5 => Even5,
                _ => Even0
            };
        }
        else
        {
            return index switch
            {
                0 => Odd0, 1 => Odd1, 2 => Odd2, 3 => Odd3, 4 => Odd4, 5 => Odd5,
                _ => Odd0
            };
        }
    }

    public static int2 GetNeighborOffset(int2 current, float degrees)
    {
        int rot = HexMetrics.WorldYawToIndex(degrees);
        return GetNeighborOffset(current, rot);
    }
}


