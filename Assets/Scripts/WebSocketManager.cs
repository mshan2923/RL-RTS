using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class WebSocketManager : MonoBehaviour
{
    [Serializable] class ActionMessage { public List<UnitAction> units; }
    [Serializable] class UnitAction { public int id; public int action; }
    [Serializable] class StateMessage { public List<UnitState> units; public float captureRatio; }

    [Serializable]
    class UnitState
    {
        public int id, col, row, yaw;
        public float reward, captureRatio, baseDist, baseDir;
        public bool done;
        public float n0, n1, n2, n3, n4, n5; // 추가
    }

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private Entity _entity;

    public NativeQueue<ActionData> ActionQueue;
    public NativeQueue<StateData> StateQueue;

    public struct ActionData
    {
        public int UnitId;
        public int Action;
    }

    public struct StateData
    {
        public int UnitId;
        public int Col, Row, Yaw;
        public float Reward;
        public bool Done;
        public float CaptureRatio;
        public float BaseDist, BaseDir;
        public float N0, N1, N2, N3, N4, N5; // 추가
    }


    void Awake()
    {
        ActionQueue = new NativeQueue<ActionData>(Allocator.Persistent);
        StateQueue = new NativeQueue<StateData>(Allocator.Persistent);

        // ECS 엔티티에 등록
        var world = World.DefaultGameObjectInjectionWorld;
        _entity = world.EntityManager.CreateEntity();
        world.EntityManager.AddComponentData(_entity, new WebSocketManagerComponent
        {
            ActionQueue = ActionQueue,
            StateQueue = StateQueue
        });
    }

    async void Start()
    {
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri("ws://localhost:8765"), _cts.Token);
        Debug.Log("[WS] Connected");

        _ = ReceiveLoop(_cts.Token);
        _ = SendLoop(_cts.Token);
    }

    async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var msg = JsonUtility.FromJson<ActionMessage>(json);
                foreach (var a in msg.units)
                    ActionQueue.Enqueue(new ActionData { UnitId = a.id, Action = a.action });
            }
            catch (Exception e) { Debug.LogError($"[WS] {e.Message}"); break; }
        }
    }

    async Task SendLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var states = new List<UnitState>();
            float captureRatio = 0f;

            while (StateQueue.TryDequeue(out var s))
            {
                states.Add(new UnitState
                {
                    id = s.UnitId,
                    col = s.Col,
                    row = s.Row,
                    yaw = s.Yaw,
                    reward = s.Reward,
                    done = s.Done,
                    captureRatio = s.CaptureRatio,
                    baseDist = s.BaseDist,
                    baseDir = s.BaseDir,
                    n0 = s.N0,
                    n1 = s.N1,
                    n2 = s.N2,  // 추가
                    n3 = s.N3,
                    n4 = s.N4,
                    n5 = s.N5   // 추가
                });
                captureRatio = s.CaptureRatio;
            }

            if (states.Count > 0)
            {
                var json = JsonUtility.ToJson(new StateMessage { units = states, captureRatio = captureRatio });
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }

            await Task.Delay(16, ct);
        }
    }

    void OnDestroy()
    {
        _cts?.Cancel();
        _ws?.Dispose();
        ActionQueue.Dispose();
        StateQueue.Dispose();
        World.DefaultGameObjectInjectionWorld?.EntityManager.DestroyEntity(_entity);
    }
}