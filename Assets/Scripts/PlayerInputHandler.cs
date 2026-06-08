using UnityEngine;

public class PlayerInputHandler : MonoBehaviour
{
    public WebSocketBridge bridge; // 위에서 만든 브릿지 연결

    void Update()
    {
        // 간단하게 화살표 키로 테스트
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            // 예시: 0번 유닛을 위쪽으로 이동 요청
            bridge.SendMoveRequest(0, 0, 1);
        }
        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            // 예시: 0번 유닛을 위쪽으로 이동 요청
            bridge.SendMoveRequest(0, 0, -1);
        }
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            // 예시: 0번 유닛을 위쪽으로 이동 요청
            bridge.SendMoveRequest(0, 1, 0);
        }
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            // 예시: 0번 유닛을 위쪽으로 이동 요청
            bridge.SendMoveRequest(0, -1, 0);
        }
    }
}