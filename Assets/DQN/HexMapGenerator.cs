using UnityEngine;
using System.Collections.Generic;

public class HexMapGenerator : MonoBehaviour
{
    [Header("--- 타일 프리팹 ---")]
    public GameObject tilePrefab; // 💡 여기에 육각 타일 프리팹을 넣어줘!

    [Header("--- 맵 크기 설정 ---")]
    public int width = 10;
    public int height = 10;
    public float outerRadius = 1.0f; // 육각 타일 중심에서 꼭짓점까지의 거리

    void Start()
    {
        GenerateMap();
    }

    private void GenerateMap()
    {
        List<Transform> generatedTiles = new List<Transform>();

        // 뾰족한 육각 타일(Pointy-top)의 가로/세로 간격 공식
        float xSpacing = Mathf.Sqrt(3f) * outerRadius;
        float zSpacing = 1.5f * outerRadius;

        // 타일들을 깔끔하게 묶어줄 부모 오브젝트 생성
        GameObject mapParent = new GameObject("HexMapGrid");
        mapParent.transform.SetParent(this.transform);

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                // 지그재그 오프셋 계산 (홀수 행일 때 오른쪽으로 살짝 밀기)
                float xPos = x * xSpacing;
                if (z % 2 == 1)
                {
                    xPos += xSpacing * 0.5f;
                }
                float zPos = z * zSpacing;

                Vector3 spawnPos = new Vector3(xPos, 0f, zPos);

                // 타일 생성 및 부모 설정
                GameObject go = Instantiate(tilePrefab, spawnPos, Quaternion.identity);
                go.name = $"Hex_{x}_{z}";
                go.transform.SetParent(mapParent.transform);

                // 리스트에 추가
                generatedTiles.Add(go.transform);
            }
        }

        Debug.Log($"✅ OOP MapGenerator: {generatedTiles.Count}개의 육각 타일 생성 완료!");

        // 🔗 매니저 스크립트 찾아서 생성된 타일 리스트 다이렉트로 주입!
        DQNModeManager modeManager = Object.FindFirstObjectByType<DQNModeManager>();
        if (modeManager != null)
        {
            modeManager.InitializeMapData(generatedTiles);
        }
        else
        {
            Debug.LogError("🚨 씬에서 DQNModeManager를 찾을 수 없어! 확인해봐.");
        }
    }
}