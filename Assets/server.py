import socket
import struct
import torch
import torch.nn as nn
import torch.optim as optim
import numpy as np

# [OOP] 새로 태어난 에이전트의 신경망 구조
class FreshQNetwork(nn.Module):
    def __init__(self):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(2, 16),
            nn.ReLU(),
            nn.Linear(16, 4) # 상(0), 하(1), 좌(2), 우(3)
        )
    def forward(self, x):
        return self.net(x)

# [OOP] 오직 실시간 학습과 ONNX 수동 저장만 담당하는 서버
class RealTimeLearningServer:
    def __init__(self):
        # 파일 로드 없음! 무조건 빈 도화지 상태로 새로 시작이야
        self.model = FreshQNetwork()
        self.optimizer = optim.Adam(self.model.parameters(), lr=0.01)
        self.criterion = nn.MSELoss()
        
        # 실시간 학습용 버퍼
        self.prev_state = None
        self.prev_action = None

    def run(self, host='127.0.0.1', port=9999):
        server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server_socket.bind((host, port))
        server_socket.listen(1)
        print("[Python] 완전히 새로운 학습 서버 가동 중... 유니티 연결을 기다리는 중이야.")
        
        try:
            conn, addr = server_socket.accept()
            print(f"[Python] 유니티 클라이언트 연결 완료: {addr}")
            
            while True:
                # 유니티가 주는 데이터 받기: 방향(4B), 거리변화량(4B), 보상(4B), 종료여부(4B) = 총 16B
                raw_data = conn.recv(16)
                if not raw_data: 
                    break
                
                direction, delta_dist, reward, done = struct.unpack('fffi', raw_data)
                current_state = torch.tensor([[direction, delta_dist]], dtype=torch.float32)

                # 1. 실시간 학습 처리 (이전 행동의 결과와 유니티가 준 보상을 매칭)
                if self.prev_state is not None and self.prev_action is not None:
                    self.optimizer.zero_grad()
                    
                    with torch.no_grad():
                        next_q = self.model(current_state).max(1)[0] if not done else torch.tensor([0.0])
                    target_q = reward + 0.99 * next_q
                    current_q = self.model(self.prev_state)[0, self.prev_action]
                    
                    loss = self.criterion(current_q, target_q.detach()[0])
                    loss.backward()
                    self.optimizer.step()

                # 2. 실시간 추론 처리 (다음 행동 결정)
                if np.random.rand() < 0.1: # 10% 탐색
                    action = np.random.randint(0, 4)
                else:
                    with torch.no_grad():
                        action = int(torch.argmax(self.model(current_state)).item())

                # 데이터 갱신
                if done:
                    self.prev_state = None
                    self.prev_action = None
                else:
                    self.prev_state = current_state
                    self.prev_action = action

                # 3. 유니티에 즉시 다음 추론값(Action) 반환
                conn.sendall(struct.pack('i', action))

        # 원하는 시점에 터미널에서 Ctrl + C 누르면 수동 익스포트 작동
        except KeyboardInterrupt:
            print("\n[Python] 수동 종료 명령 확인! 현재까지 학습된 모델을 ONNX로 추출할게.")
            self.export_to_onnx()
        finally:
            if 'conn' in locals():
                conn.close()
            server_socket.close()

    def export_to_onnx(self, path="FreshTrainedAgent.onnx"):
        self.model.eval()
        dummy_input = torch.randn(1, 2, dtype=torch.float32)
        torch.onnx.export(
            self.model, dummy_input, path,
            export_params=True, opset_version=11,
            input_names=['input'], output_names=['output']
        )
        print(f"[Python] ONNX 파일 수동 추출 성공: {path}")

if __name__ == "__main__":
    server = RealTimeLearningServer()
    server.run()