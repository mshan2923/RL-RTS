import socket
import json
import torch
import torch.nn as nn
import torch.optim as optim
from torch.distributions import Categorical
import threading
import os

# ==========================================
# 1. 하이퍼파라미터 및 하드웨어 설정
# ==========================================
INPUT_DIM = 2    # 관측치: [목표물 방향, 거리 변화량]
ACTION_DIM = 4   # 행동: 0(상), 1(하), 2(좌), 3(우) - 일반 캐릭터 이동

# ==========================================
# 2. PPO 뉴럴 네트워크 정의 (OOP 구조)
# ==========================================
class Actor(nn.Module):
    """행동을 결정하는 정책 네트워크 (유니티 인퍼런스 엔진 수출용)"""
    def __init__(self, input_dim, action_dim):
        super(Actor, self).__init__()
        self.net = nn.Sequential(
            nn.Linear(input_dim, 64),
            nn.ReLU(),
            nn.Linear(64, 64),
            nn.ReLU(),
            nn.Linear(64, action_dim)
        )
    def forward(self, x):
        return self.net(x)

class Critic(nn.Module):
    """상태의 가치를 평가하는 가치 네트워크 (학습 서버 전용)"""
    def __init__(self, input_dim):
        super(Critic, self).__init__()
        self.net = nn.Sequential(
            nn.Linear(input_dim, 64),
            nn.ReLU(),
            nn.Linear(64, 64),
            nn.ReLU(),
            nn.Linear(64, 1)
        )
    def forward(self, x):
        return self.net(x)

# ==========================================
# 3. PPO 학습 및 에이전트 관리 클래스
# ==========================================
class PPOManager:
    def __init__(self, lr_actor=0.0003, lr_critic=0.001, gamma=0.99, K_epochs=5, eps_clip=0.2):
        self.gamma = gamma
        self.eps_clip = eps_clip
        self.K_epochs = K_epochs
        
        self.actor = Actor(INPUT_DIM, ACTION_DIM)
        self.critic = Critic(INPUT_DIM)
        
        self.optimizer_actor = optim.Adam(self.actor.parameters(), lr=lr_actor)
        self.optimizer_critic = optim.Adam(self.critic.parameters(), lr=lr_critic)
        
        self.MseLoss = nn.MSELoss()
        
        # 멀티 에이전트 데이터 수집용 버퍼
        self.states = []
        self.actions = []
        self.logprobs = []
        self.rewards = []
        self.is_terminals = []
        self.num_agents = 1

    def select_action(self, state_list):
        """유니티 내 모든 에이전트의 관측치를 한 번에 받아 배치 처리로 행동 선택"""
        state_tensor = torch.FloatTensor(state_list)
        with torch.no_grad():
            logits = self.actor(state_tensor)
            dist = Categorical(logits=logits)
            action = dist.sample()
            action_logprob = dist.log_prob(action)
            
        return action.tolist(), action_logprob.tolist()

    def store_transition(self, states, actions, logprobs, rewards, dones):
        """멀티 에이전트의 스텝 데이터를 버퍼에 저장"""
        self.num_agents = len(states)
        self.states.extend(states)
        self.actions.extend(actions)
        self.logprobs.extend(logprobs)
        self.rewards.extend(rewards)
        self.is_terminals.extend(dones)

    def update(self):
        """수집된 데이터를 바탕으로 에포크만큼 가중치 업데이트"""
        if len(self.states) == 0:
            return
            
        total_samples = len(self.states)
        num_steps = total_samples // self.num_agents
        
        # 동기식 멀티 에이전트 환경에 맞게 리턴(Returns) 계산 분리
        rewards_targets = [0.0] * total_samples
        for a in range(self.num_agents):
            discounted_reward = 0
            for s in range(num_steps - 1, -1, -1):
                idx = s * self.num_agents + a
                if self.is_terminals[idx]:
                    discounted_reward = 0
                discounted_reward = self.rewards[idx] + (self.gamma * discounted_reward)
                rewards_targets[idx] = discounted_reward
                
        # 텐서 변환
        old_states = torch.FloatTensor(self.states)
        old_actions = torch.LongTensor(self.actions)
        old_logprobs = torch.FloatTensor(self.logprobs)
        rewards = torch.FloatTensor(rewards_targets)
        
        # 보상 정규화로 안정성 확보
        rewards = (rewards - rewards.mean()) / (rewards.std() + 1e-7)
        
        # K-epochs 최적화 루프
        for _ in range(self.K_epochs):
            logits = self.actor(old_states)
            dist = Categorical(logits=logits)
            logprobs = dist.log_prob(old_actions)
            dist_entropy = dist.entropy()
            state_values = self.critic(old_states).squeeze()
            
            # PPO Clip 손실 계산
            ratios = torch.exp(logprobs - old_logprobs)
            advantages = rewards - state_values.detach()
            
            surr1 = ratios * advantages
            surr2 = torch.clamp(ratios, 1 - self.eps_clip, 1 + self.eps_clip) * advantages
            
            loss = -torch.min(surr1, surr2) + 0.5 * self.MseLoss(state_values, rewards) - 0.01 * dist_entropy
            
            # 가중치 업데이트
            self.optimizer_actor.zero_grad()
            self.optimizer_critic.zero_grad()
            loss.mean().backward()
            self.optimizer_actor.step()
            self.optimizer_critic.step()
            
        # 데이터 비우기
        self.states.clear()
        self.actions.clear()
        self.logprobs.clear()
        self.rewards.clear()
        self.is_terminals.clear()
        print("[Server] >>> PPO 신경망 가중치 최적화 완료 <<<")

    def export_onnx(self):
            if not hasattr(self, 'actor') or self.actor is None:
                print("[Error] 내보낼 모델 객체(actor)가 존재하지 않아.")
                return

            import os
            import torch
            import onnx  # 이 부분이 추가되었어!

            dummy_input = torch.randn(1, 2)
            
            script_dir = os.path.dirname(os.path.abspath(__file__))
            onnx_path = os.path.join(script_dir, "PPO_Policy.onnx")

            try:
                # 1. 먼저 문제의 매개변수를 빼고 기본 구조로 익스포트를 진행해
                torch.onnx.export(
                    self.actor,                 
                    (dummy_input,),
                    onnx_path,
                    export_params=True,
                    opset_version=15,
                    do_constant_folding=True,
                    input_names=['input'],
                    output_names=['output'],
                    dynamic_axes={
                        'input': {0: 'batch_size'},
                        'output': {0: 'batch_size'}
                    }
                )
                
                # 2. 내보낸 ONNX 파일을 다시 불러와서 강제로 단일 파일로 덮어씌워 (*.onnx.data 제거)
                loaded_model = onnx.load(onnx_path)
                onnx.save(loaded_model, onnx_path, save_as_external_data=False)
                
                print(f"[Python] 외부 파일 없이 단일 ONNX 파일 익스포트 성공: {onnx_path}")
            except Exception as e:
                print(f"[Python] ONNX 내보내기 실패: {e}")

# ==========================================
# 4. 실시간 콘솔 명령어 처리 스레드
# ==========================================
def console_command_loop(ppo_manager):
    print("사용 가능한 콘솔 명령어 [ save: ONNX 추출 | exit: 서버 종료 ]")
    while True:
        cmd = input().strip().lower()
        if cmd == "save":
            ppo_manager.export_onnx()
        elif cmd == "exit":
            print("서버를 안전하게 종료할게.")
            os._exit(0)

# ==========================================
# 5. 메인 네트워크 소켓 서버 구동
# ==========================================
def main():
    ppo = PPOManager()
    
    # 키보드 입력 처리를 위한 백그라운드 스레드 가동 (수동 ONNX 추출용)
    cmd_thread = threading.Thread(target=console_command_loop, args=(ppo,), daemon=True)
    cmd_thread.start()
    
    server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_socket.bind(('127.0.0.1', 5000))
    server_socket.listen(1)
    print("[Server] 파이썬 PPO 학습 서버가 127.0.0.1:5000 에서 대기 중이야.")
    
    # 이전 프레임의 연산 추적용 변수
    last_states = None
    last_actions = None
    last_logprobs = None
    
    while True:
        conn, addr = server_socket.accept()
        print(f"[Server] 유니티 그래픽 환경 연결됨: {addr}")
        
        buffer = ""
        while True:
            data = conn.recv(4096).decode('utf-8')
            if not data:
                break
                
            buffer += data
            while "\n" in buffer:
                line, buffer = buffer.split("\n", 1)
                if not line.strip():
                    continue
                    
                try:
                    request = json.loads(line)
                    cmd = request.get("command")
                    states = request.get("states")
                    
                    if cmd == "step":
                        rewards = request.get("rewards")
                        dones = request.get("dones")
                        
                        # 이전 행동의 결과가 존재하면 학습 버퍼에 누적
                        if last_states is not None:
                            ppo.store_transition(last_states, last_actions, last_logprobs, rewards, dones)
                        
                        # 현재 상태를 바탕으로 새로운 배치 행동 결정
                        actions, logprobs = ppo.select_action(states)
                        
                        last_states = states
                        last_actions = actions
                        last_logprobs = logprobs
                        
                        # 유니티로 즉시 전달
                        response = json.dumps({"actions": actions}) + "\n"
                        conn.sendall(response.encode('utf-8'))
                        
                        # 배치 샘플 수량이 기준치를 넘기면 최적화 수행 (예: 1024개 샘플)
                        if len(ppo.states) >= 1024:
                            ppo.update()
                            last_states = None  # 연속성 데이터 초기화
                            
                    elif cmd == "reset":
                        # 에피소드가 끝나서 전체 유닛이 일괄 재배치 되었을 때 진입
                        last_states = None
                        last_actions = None
                        last_logprobs = None
                        
                        actions, logprobs = ppo.select_action(states)
                        last_states = states
                        last_actions = actions
                        last_logprobs = logprobs
                        
                        response = json.dumps({"actions": actions}) + "\n"
                        conn.sendall(response.encode('utf-8'))
                        print("[Server] >>> 에피소드 종료: 일괄 제거 및 초기 재배치 완료 <<<")
                        
                except Exception as e:
                    print(f"[Error] 데이터 파싱 에러: {e}")
                    
        conn.close()
        print("[Server] 유니티 클라이언트 연결 종료.")

if __name__ == "__main__":
    main()