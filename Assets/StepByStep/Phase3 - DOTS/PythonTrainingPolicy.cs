namespace RL_StepByStep
{
    using System;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using UnityEngine;

    public class PythonTrainingPolicy<TObs, TAction> : IDisposable 
        where TObs : unmanaged 
        where TAction : unmanaged
    {
        private Socket socket;
        private byte[] sendBuffer;
        private byte[] receiveBuffer;
        
        private readonly int obsSize;
        private readonly int actionSize;
        private readonly int packetPerAgentSize;

        public PythonTrainingPolicy(string ip, int port)
        {
            obsSize = UnsafeUtility.SizeOf<TObs>();
            actionSize = UnsafeUtility.SizeOf<TAction>();
            packetPerAgentSize = 4 + actionSize; 

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(ip, port);
            socket.NoDelay = true; 
        }



        public async Task UpdateTrainingAsync(NativeArray<TObs> obsArray, NativeArray<TAction> actionArray)
        {
            int count = obsArray.Length;
            if (count == 0) return;

            // 1. 송신 데이터 준비 (메모리 카피는 빠르게 메인에서 처리)
            int payloadSize = count * obsSize;
            int totalSendSize = 4 + payloadSize; 

            if (sendBuffer == null || sendBuffer.Length < totalSendSize)
                sendBuffer = new byte[totalSendSize];

            byte[] countBytes = BitConverter.GetBytes(count);
            Buffer.BlockCopy(countBytes, 0, sendBuffer, 0, 4);

            unsafe
            {
                void* srcPtr = obsArray.GetUnsafeReadOnlyPtr();
                fixed (byte* dstPtr = sendBuffer)
                {
                    UnsafeUtility.MemCpy(dstPtr + 4, srcPtr, payloadSize);
                }
            }

            // 비동기 데이터 송신 (스레드 블로킹 없음)
            await SendAllAsync(sendBuffer, totalSendSize);

            // 2. 비동기 헤더 수신 (4바이트 개수 파싱)
            byte[] headerBuffer = new byte[4];
            await ReceiveAllAsync(headerBuffer, 4);
            int countOut = BitConverter.ToInt32(headerBuffer, 0);

            // 3. 비동기 바디 데이터 수신
            int targetReceiveSize = countOut * packetPerAgentSize;
            if (receiveBuffer == null || receiveBuffer.Length < targetReceiveSize)
                receiveBuffer = new byte[targetReceiveSize];

            await ReceiveAllAsync(receiveBuffer, targetReceiveSize);

            // 4. 수신 완료 후 NativeArray에 메모리 다이렉트 주입
            // (await 이후 유니티 메인 스레드 컨텍스트로 복귀하므로 NativeArray 접근 안전함)
            unsafe
            {
                byte* actionBasePtr = (byte*)actionArray.GetUnsafePtr();
                fixed (byte* resBufferPtr = receiveBuffer)
                {
                    for (int i = 0; i < countOut; i++)
                    {
                        int offset = i * packetPerAgentSize;
                        int responseUnitId = *(int*)(resBufferPtr + offset);
                        
                        if (responseUnitId >= 0 && responseUnitId < actionArray.Length)
                        {
                            byte* targetActionPtr = actionBasePtr + (responseUnitId * actionSize);
                            byte* sourceActionPtr = resBufferPtr + offset + 4;
                            
                            UnsafeUtility.MemCpy(targetActionPtr, sourceActionPtr, actionSize);
                        }
                    }
                }
            }
        }

        private async Task SendAllAsync(byte[] buffer, int size)
        {
            int sent = 0;
            while (sent < size)
            {
                int r = await socket.SendAsync(new ArraySegment<byte>(buffer, sent, size - sent), SocketFlags.None);
                if (r <= 0) throw new SocketException((int)SocketError.ConnectionReset);
                sent += r;
            }
        }

        private async Task ReceiveAllAsync(byte[] buffer, int size)
        {
            int received = 0;
            while (received < size)
            {
                int r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, received, size - received), SocketFlags.None);
                if (r <= 0) throw new SocketException((int)SocketError.ConnectionReset);
                received += r;
            }
        }

        public void Dispose()
        {
            if (socket != null)
            {
                if (socket.Connected) socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }
        }
    }
}