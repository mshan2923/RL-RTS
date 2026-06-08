using Unity.Mathematics;
using UnityEngine;

public static class HexMetrics
{
    public const float outerRadius = .51f;
    public const float innerRadius = outerRadius * 1.73205f / 2f; // √3/2

    // Pointy-top 코너
    public static readonly Vector3[] corners = {
        new Vector3(0f,          0f, outerRadius),
        new Vector3(innerRadius, 0f, outerRadius * 0.5f),
        new Vector3(innerRadius, 0f, -outerRadius * 0.5f),
        new Vector3(0f,          0f, -outerRadius),
        new Vector3(-innerRadius, 0f, -outerRadius * 0.5f),
        new Vector3(-innerRadius, 0f, outerRadius * 0.5f)
    };


    private static int3 CubeRound(float x, float y, float z)
    {
        int rx = Mathf.RoundToInt(x);
        int ry = Mathf.RoundToInt(y);
        int rz = Mathf.RoundToInt(z);

        float xDiff = Mathf.Abs(rx - x);
        float yDiff = Mathf.Abs(ry - y);
        float zDiff = Mathf.Abs(rz - z);

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
        float q = (position.x * Mathf.Sqrt(3f) / 3f - position.z / 3f) / outerRadius;
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
        return Mathf.RoundToInt(normalized / 60f) % 6;
    }
    public static int2 WorldToOffsetWithYaw(float3 position, float yawDegrees)
    {
        float rad = yawDegrees * Mathf.Deg2Rad;
        float3 dir = new float3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

        return WorldToOffset(position + dir * innerRadius * 2f);
    }
    public static bool IsValidCell(int2 cell, int minCol, int maxCol, int minRow, int maxRow)
    {
        return cell.x >= minCol && cell.x <= maxCol &&
               cell.y >= minRow && cell.y <= maxRow;
    }
}