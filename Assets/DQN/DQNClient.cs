using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;

public class DQNClient : MonoBehaviour
{
    [System.Serializable]
    public class PacketData
    {
        public List<float> state;
        public bool is_training;
        public float reward;
        public bool done;
    }

    [System.Serializable]
    public class ResponseData { public int action; }

    [Header("--- 에피소드 제한 설정 ---")]
    public int maxStepsPerEpisode = 50; // 💡 50걸음 안에 못 찾으면 타임아웃
    private int currentStepCount = 0;

    private DQNModeManager manager;
    private Transform targetTransform;
    private List<Transform> mapTiles;

    private bool isMoving = false;
    private Vector3 targetPosition;
    private float lastDistance;
    private bool isTrainingMode;

    private float currentReward = 0f;
    private bool isDone = false;

    public void Setup(DQNModeManager mgr, Transform target)
    {
        manager = mgr;
        targetTransform = target;
    }

    public void ResetAgent(List<Transform> tiles, bool isTraining)
    {
        mapTiles = tiles;
        isTrainingMode = isTraining;
        targetPosition = transform.position;
        isMoving = false;
        isDone = false;
        currentReward = 0f;
        currentStepCount = 0; // 💡 스텝 카운트 초기화

        if (targetTransform != null)
        {
            lastDistance = Vector3.Distance(transform.position, targetTransform.position);
        }

        StopAllCoroutines();
        StartCoroutine(AgentLoop());
    }

    private IEnumerator AgentLoop()
    {
        while (!isDone)
        {
            if (targetTransform == null || isMoving) { yield return new WaitForSeconds(0.05f); continue; }

            // 💡 1. 스텝 카운트 증가 및 타임아웃 조건 체크
            currentStepCount++;
            if (currentStepCount >= maxStepsPerEpisode)
            {
                isDone = true;
                currentReward -= 2.0f; // 못 찾고 시간 초과되면 패널티 보상 던지기
            }

            // 2. 상태 수집
            List<float> stateVector = new List<float>();
            Vector3 relativePos = transform.InverseTransformPoint(targetTransform.position);
            stateVector.Add(relativePos.x);
            stateVector.Add(relativePos.z);
            stateVector.Add(Mathf.Clamp01(lastDistance / 30f));
            for (int i = 0; i < 6; i++) stateVector.Add(1.0f);

            // 3. 패킷 전송
            PacketData packet = new PacketData { state = stateVector, is_training = isTrainingMode, reward = currentReward, done = isDone };
            currentReward = -0.02f; // 기본 스텝 패널티 (지연 방지)

            string json = JsonUtility.ToJson(packet);
            byte[] rawBody = System.Text.Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest www = new UnityWebRequest("http://127.0.0.1:8000/step", "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(rawBody);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    ResponseData res = JsonUtility.FromJson<ResponseData>(www.downloadHandler.text);
                    ExecuteHexAction(res.action);
                }
            }

            // 💡 타임아웃으로 에피소드가 끝났다면 파이썬에 마지막 패킷을 전송한 직후 재배치
            if (isDone)
            {
                manager.RespawnPair(this);
                yield break;
            }

            yield return new WaitForSeconds(0.2f);
        }
    }

    private void ExecuteHexAction(int actionIndex)
    {
        Vector3[] hexDirections = new Vector3[] {
            new Vector3(0f, 0f, 1f), new Vector3(0.86f, 0f, 0.5f), new Vector3(0.86f, 0f, -0.5f),
            new Vector3(0f, 0f, -1f), new Vector3(-0.86f, 0f, -0.5f), new Vector3(-0.86f, 0f, 0.5f)
        };

        Vector3 approximateTarget = transform.position + (hexDirections[actionIndex] * 1.73f);

        Transform bestTile = null;
        float minDistance = 0.5f;

        foreach (var tile in mapTiles)
        {
            float dist = Vector3.Distance(tile.position + Vector3.up * 0.5f, approximateTarget);
            if (dist < minDistance)
            {
                minDistance = dist;
                bestTile = tile;
            }
        }

        if (bestTile != null)
        {
            targetPosition = bestTile.position + Vector3.up * 0.5f;
            StartCoroutine(SmoothMove());
        }
        else
        {
            currentReward -= 0.5f; // 외곽 벽 들이받으면 감점 확대
        }
    }

    private IEnumerator SmoothMove()
    {
        isMoving = true;
        while (Vector3.Distance(transform.position, targetPosition) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, 10f * Time.deltaTime);
            yield return null;
        }
        transform.position = targetPosition;
        isMoving = false;

        // 이동 후 결과 체크 (둘 다 타일 중심에 배치되므로 이제 정밀하게 맞음)
        float newDist = Vector3.Distance(transform.position, targetTransform.position);

        currentReward += (lastDistance - newDist) * 3.0f; // 방향성 보상 가중치 강화
        lastDistance = newDist;

        // 목적지 정상 도달 판정
        if (newDist < 0.1f)
        {
            currentReward += 10.0f; // 골인 보상 대폭 상향
            isDone = true;
            StopAllCoroutines();
            manager.RespawnPair(this);
        }
    }
}