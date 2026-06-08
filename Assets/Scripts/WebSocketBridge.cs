using UnityEngine;
using NativeWebSocket; // 깃허브에서 받은 그 녀석
using System.Text;
using Unity.Entities;

public class WebSocketBridge : MonoBehaviour
{
    private WebSocket ws;
    private World world;

    async void Start()
    {
        ws = new WebSocket("ws://localhost:8765");


        ws.OnOpen += () => Debug.Log("Connection open!");
        ws.OnError += (e) => Debug.Log("Error: " + e);
        ws.OnClose += (e) => Debug.Log("Connection closed!");

        ws.OnMessage += (bytes) =>
        {
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            HandleResponse(json);
        };

        await ws.Connect();
        world = World.DefaultGameObjectInjectionWorld;
    }

    // 서버 응답을 처리해서 ECS로 던짐
    void HandleResponse(string json)
    {
        var data = JsonUtility.FromJson<UnitMoveMessage>(json);


        var entity = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntity();

        World.DefaultGameObjectInjectionWorld.EntityManager.AddComponentData(entity, new NetworkResponseComponent
        {
            UnitId = data.UnitId,
            NewX = data.TargetX,
            NewZ = data.TargetZ
        });
    }


    public async void SendMoveRequest(int id, int x, int z)
    {
        var msg = new UnitMoveMessage { UnitId = id, TargetX = x, TargetZ = z };
        string json = JsonUtility.ToJson(msg);
        await ws.SendText(json);
    }

    void Update() => ws.DispatchMessageQueue(); // 이걸 안 하면 응답을 못 받아
}
