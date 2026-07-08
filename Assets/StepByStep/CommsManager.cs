using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>
    /// Unity <-> Python 바이너리 TCP 통신을 전담하는 싱글톤.
    /// RL 구조(관측/액션 형태), 게임 페이즈가 바뀌어도 이 클래스는 절대 수정하지 않는다.
    ///
    /// 사용 패턴: 유닛/게임 매니저가 직접 소켓을 몰라도 되도록,
    /// Enqueue로 데이터만 넣고, 액션 도착은 이벤트(OnActionReceived)로 통지한다.
    ///
    /// 프로토콜:
    ///   송신: [count:int32][TObs * count]
    ///   수신: [count:int32][(unitId:int32, TAction) * count]
    /// </summary>
    public class CommsManager<TObs, TAction> : MonoBehaviour, ICommsManager
        where TObs : struct
        where TAction : struct
    {
        protected static CommsManager<TObs, TAction> _instance;
        public static CommsManager<TObs, TAction> Instance => _instance;

        /// <summary>액션이 도착했을 때 발생. (unitId, action)</summary>
        public event Action<int, TAction> OnActionReceived;

        /// <summary>연결 상태가 바뀔 때 발생.</summary>
        public event Action<bool> OnConnectionChanged;

        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private int port = 5555;

        private readonly Queue<TObs> pending = new Queue<TObs>();
        private readonly object lockObj = new object();
        private bool isSending;

        private TcpClient client;
        private NetworkStream stream;
        private readonly int obsSize = Marshal.SizeOf<TObs>();
        private readonly int actionSize = Marshal.SizeOf<TAction>();

        public bool IsConnected => client != null && client.Connected;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public async void Connect(string host = null, int port = -1)
        {
            if (host != null) this.host = host;
            if (port > 0) this.port = port;

            try
            {
                client = new TcpClient();
                await client.ConnectAsync(this.host, this.port);
                stream = client.GetStream();
                Debug.Log($"[CommsManager] 연결 성공: {this.host}:{this.port}");
                OnConnectionChanged?.Invoke(true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CommsManager] 연결 실패: {e.Message}");
                OnConnectionChanged?.Invoke(false);
            }
        }

        /// <summary>관측 데이터를 큐에 넣는다. 실제 전송은 Flush()가 호출될 때 일어난다.</summary>
        public void Enqueue(TObs data)
        {
            lock (lockObj)
            {
                pending.Enqueue(data);
            }
        }

        /// <summary>큐에 쌓인 모든 관측을 한 번에 전송하고 응답을 받아 이벤트로 통지한다.</summary>
        public async void Flush()
        {
            if (isSending) return;
            if (stream == null || !IsConnected) return;

            TObs[] batch;
            lock (lockObj)
            {
                if (pending.Count == 0) return;
                batch = pending.ToArray();
                pending.Clear();
            }

            isSending = true;
            try
            {
                await SendAndReceive(batch);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CommsManager] 통신 실패: {e.Message}");
                OnConnectionChanged?.Invoke(false);
            }
            finally
            {
                isSending = false;
            }
        }

        private async System.Threading.Tasks.Task SendAndReceive(TObs[] batch)
        {
            // ---- 송신 ----
            byte[] header = BitConverter.GetBytes(batch.Length);
            await stream.WriteAsync(header, 0, header.Length);

            byte[] buffer = new byte[obsSize * batch.Length];
            IntPtr ptr = Marshal.AllocHGlobal(obsSize);
            try
            {
                for (int i = 0; i < batch.Length; i++)
                {
                    Marshal.StructureToPtr(batch[i], ptr, false);
                    Marshal.Copy(ptr, buffer, i * obsSize, obsSize);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            await stream.WriteAsync(buffer, 0, buffer.Length);

            // ---- 수신 ----
            byte[] respHeader = await ReadExact(4);
            int respCount = BitConverter.ToInt32(respHeader, 0);
            byte[] respBody = await ReadExact(respCount * (4 + actionSize));

            int offset = 0;
            for (int i = 0; i < respCount; i++)
            {
                int unitId = BitConverter.ToInt32(respBody, offset);
                offset += 4;

                IntPtr actionPtr = Marshal.AllocHGlobal(actionSize);
                TAction action;
                try
                {
                    Marshal.Copy(respBody, offset, actionPtr, actionSize);
                    action = Marshal.PtrToStructure<TAction>(actionPtr);
                }
                finally
                {
                    Marshal.FreeHGlobal(actionPtr);
                }
                offset += actionSize;

                OnActionReceived?.Invoke(unitId, action);
            }
        }

        private async System.Threading.Tasks.Task<byte[]> ReadExact(int size)
        {
            byte[] buf = new byte[size];
            int offset = 0;
            while (offset < size)
            {
                int read = await stream.ReadAsync(buf, offset, size - offset);
                if (read == 0) throw new Exception("연결 끊김");
                offset += read;
            }
            return buf;
        }

        void OnDestroy()
        {
            stream?.Close();
            client?.Close();
        }
    }
}