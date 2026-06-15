using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// https://github.com/chanfort/kdtree-jobified-unity
/// </summary>
public class KDTreeDebugger : MonoBehaviour
{
    [Header("Settings")]
    public int PointCount = 1000;
    public float SpawnRadius = 20f;
    public Transform QueryTarget; // 가장 가까운 점을 찾을 기준 오브젝트 (예: 플레이어)

    [Header("Debug")]
    public bool UseStructVersion = false; // 체크하면 Job용 Struct 버전 사용, 끄면 Class 버전 사용

    // Data
    private List<Vector3> points = new List<Vector3>();

    // Class Version
    private KDTree treeClass;

    // Struct Version
    private NativeArray<float3> pointsNative;
    private NativeArray<byte> teamNative;

    private KDTreeStruct treeStruct;
    private bool isStructCreated = false;

    void Start()
    {
        GeneratePoints();
        BuildTrees();
    }

    void OnDestroy()
    {
        // NativeArray는 반드시 직접 해제해야 메모리 누수가 없습니다.
        if (isStructCreated)
        {
            treeStruct.DisposeArrays();
            if (pointsNative.IsCreated) pointsNative.Dispose();
            if (teamNative.IsCreated) teamNative.Dispose();
        }
    }

    [ContextMenu("Regenerate Points")]
    void GeneratePoints()
    {
        points.Clear();
        for (int i = 0; i < PointCount; i++)
        {
            points.Add(UnityEngine.Random.insideUnitSphere * SpawnRadius);
        }

        // 실행 중이라면 트리도 다시 빌드
        if (Application.isPlaying)
        {
            BuildTrees();
        }
    }

    void BuildTrees()
    {
        // 1. Class Version 빌드
        treeClass = KDTree.MakeFromPoints(points.ToArray());

        // 2. Struct Version 빌드 (기존 것 정리 후)
        if (isStructCreated)
        {
            treeStruct.DisposeArrays();
            if (pointsNative.IsCreated) pointsNative.Dispose();
            if (teamNative.IsCreated) teamNative.Dispose();
        }

        pointsNative = new NativeArray<float3>(points.Count, Allocator.Persistent);
        for (int i = 0; i < points.Count; i++)
        {
            pointsNative[i] = points[i];
        }
        teamNative = new NativeArray<byte>(points.Count, Allocator.Persistent);
        for (int i = 0; i < points.Count; i++)
        {
            teamNative[i] = 0;
        }

        treeStruct = new KDTreeStruct();
        treeStruct.MakeFromPoints(pointsNative, teamNative);
        isStructCreated = true;
    }

    void OnDrawGizmos()
    {
        if (points == null || points.Count == 0) return;

        // 전체 점 그리기 (희미하게)
        Gizmos.color = new Color(0, 1, 1, 0.3f); // 반투명한 청록색으로 변경
        foreach (var p in points)
        {
            Gizmos.DrawSphere(p, 0.1f); // WireSphere -> Sphere (더 잘 보임)
        }

        if (QueryTarget == null) return;

        Vector3 targetPos = QueryTarget.position;
        Vector3 nearestPos = Vector3.zero;
        int nearestIndex = -1;

        // 가장 가까운 점 찾기
        if (UseStructVersion && isStructCreated)
        {
            // Struct 버전 (DOTS/Job용)
            nearestIndex = treeStruct.FindNearest(targetPos, 0);
            if (nearestIndex != -1)
                nearestPos = pointsNative[nearestIndex];
        }
        else if (treeClass != null)
        {
            // Class 버전 (일반용)
            nearestIndex = treeClass.FindNearest(targetPos);
            if (nearestIndex != -1)
                nearestPos = points[nearestIndex];
        }

        // 결과 그리기 (초록색 선과 구)
        if (nearestIndex != -1)
        {
            Gizmos.color = Color.red; // 결과는 빨간색으로 강조
            Gizmos.DrawLine(targetPos, nearestPos);
            Gizmos.DrawSphere(nearestPos, 0.5f); // 결과 지점 강조

            // 타겟 위치 표시
            Gizmos.color = Color.green; // 타겟은 초록색
            Gizmos.DrawSphere(targetPos, 0.5f);
        }
    }
}
