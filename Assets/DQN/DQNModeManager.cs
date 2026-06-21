using UnityEngine;
using System.Collections.Generic;

public class DQNModeManager : MonoBehaviour
{
    public enum RLMode { Train, Inference }
    public RLMode currentMode = RLMode.Train;

    public GameObject agentPrefab;
    public GameObject targetPrefab;
    public int agentCount = 5; // 💡 한 번에 학습시킬 에이전트 수

    private List<Transform> allTiles = new List<Transform>();

    // 에이전트와 타겟을 쌍으로 관리하기 위한 클래스
    private class AgentPair
    {
        public GameObject agent;
        public GameObject target;
        public DQNClient client;
    }
    private List<AgentPair> activePairs = new List<AgentPair>();

    // 맵 생성 완료 후 호출됨
    public void InitializeMapData(List<Transform> generatedTiles)
    {
        allTiles = generatedTiles;

        // 지정된 개수만큼 에이전트 쌍 생성
        for (int i = 0; i < agentCount; i++)
        {
            SpawnNewPair();
        }
    }

    private void SpawnNewPair()
    {
        AgentPair pair = new AgentPair();

        // 임시 위치에 스폰 후 RespawnPair에서 타일 위로 정밀 재배치
        pair.agent = Instantiate(agentPrefab, Vector3.zero, Quaternion.identity);
        pair.target = Instantiate(targetPrefab, Vector3.zero, Quaternion.identity);

        pair.client = pair.agent.GetComponent<DQNClient>();
        if (pair.client == null) pair.client = pair.agent.AddComponent<DQNClient>();

        // 💡 클라이언트에게 매니저 자신을 참조하게 함 (목적지 도달 신호 수신용)
        pair.client.Setup(this, pair.target.transform);

        activePairs.Add(pair);
        RespawnPair(pair);
    }

    // 🔄 특정 에이전트 쌍만 맵 안에서 랜덤 재배치하는 함수
    public void RespawnPair(DQNClient client)
    {
        foreach (var pair in activePairs)
        {
            if (pair.client == client)
            {
                RespawnPair(pair);
                return;
            }
        }
    }

    private void RespawnPair(AgentPair pair)
    {
        int agentIdx = Random.Range(0, allTiles.Count);
        int targetIdx = Random.Range(0, allTiles.Count);
        while (agentIdx == targetIdx) targetIdx = Random.Range(0, allTiles.Count);

        // 🎯 [수정] 타일의 정확한 중심 좌표(allTiles[...].position)를 그대로 가져와서 Y값만 보정
        Vector3 agentTilePos = allTiles[agentIdx].position;
        Vector3 targetTilePos = allTiles[targetIdx].position;

        // 프리팹 자체 중심점에 맞춰 Y축 높이 보정 (기본 타일 표면 위로 딱 맞춤)
        pair.agent.transform.position = new Vector3(agentTilePos.x, agentTilePos.y + 0.5f, agentTilePos.z);
        pair.target.transform.position = new Vector3(targetTilePos.x, targetTilePos.y + 0.5f, targetTilePos.z);

        // 에이전트 리셋 및 루프 시작
        pair.client.ResetAgent(allTiles, currentMode == RLMode.Train);
    }
}