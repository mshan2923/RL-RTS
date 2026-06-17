import asyncio
import websockets
import json
import torch
import torch.nn as nn
import torch.optim as optim
from torch.distributions import Categorical
from enum import Enum
import sys
import threading
import os

MODEL_PATH = "ppo_hex.pt"
STATE_DIM = 12  # 기존 9 + [TargetDist, TargetDir, TargetActive] 추가분
ACTION_DIM = 6  # 이동 및 기타 액션

class Mode(Enum):
    TRAIN     = "train"
    INFERENCE = "inference"

class PPONetwork(nn.Module):
    def __init__(self, state_dim, action_dim):
        super().__init__()
        self.shared = nn.Sequential(
            nn.Linear(state_dim, 128), nn.ReLU(),
            nn.Linear(128, 128), nn.ReLU()
        )
        self.policy = nn.Linear(128, action_dim) # 이동 행동
        self.value  = nn.Linear(128, 1)          # 상태 가치
        # 나중에 점령 판단이나 자원 관리 헤드를 여기다 추가할 거야

    def forward(self, x):
        shared = self.shared(x)
        return self.policy(shared), self.value(shared)

class PPO:
    def __init__(self, state_dim = STATE_DIM, action_dim = ACTION_DIM, mode=Mode.TRAIN):
        self.net       = PPONetwork(state_dim, action_dim)
        self.optimizer = optim.Adam(self.net.parameters(), lr=0.00005)
        self.mode      = mode
        self.clip      = 0.2
        self.gamma     = 0.95
        self.lam       = 0.95
        self.entropy_coef = 0.25  # 탐색 강도를 약간 더 높여서 고착화 방지
        self.buffers   = {}
        self._load()
        
        test_state = [0.5] * STATE_DIM
        counts = {}
        for _ in range(100):
            a, _, _ = self.select_action(test_state)
            counts[a] = counts.get(a, 0) + 1
        print(f"[PPO] 초기 action 분포: {counts}")

    def _load(self):
        try:
            self.net.load_state_dict(torch.load(MODEL_PATH))
            print(f"[PPO] 모델 로드: {MODEL_PATH}")
        except FileNotFoundError:
            print(f"[PPO] 모델 파일 '{MODEL_PATH}'을 찾을 수 없습니다. 새로 학습을 시작합니다.")
        except Exception as e:
            print(f"[PPO] 모델 로드 중 오류 발생: {e}. 새로 학습을 시작합니다.")

    def _save(self):
        torch.save(self.net.state_dict(), MODEL_PATH)
        print(f"[PPO] 저장: {MODEL_PATH}")

    def save_onnx(self, path="ppo_hex.onnx"):
            # 기존에 생성된 .onnx.data 파일이 있다면 충돌 방지를 위해 먼저 삭제
            data_path = path + ".data"
            if os.path.exists(data_path):
                os.remove(data_path)

            self.net.eval()
            dummy = torch.randn(1, STATE_DIM)
            
            torch.onnx.export(
                self.net, 
                (dummy,), 
                path,
                export_params=True,       
                external_data=False, # <- [추가] 가중치를 외부 파일로 분리하지 않고 내부에 강제 포함!
                opset_version=15,          
                do_constant_folding=True, 
                input_names=["state"],
                output_names=["policy", "value"],
                dynamic_axes={
                    'state': {0: 'batch_size'},
                    'policy': {0: 'batch_size'},
                    'value': {0: 'batch_size'}
                }
            )
            print(f"[PPO] ONNX 저장 완료 (단일 파일 강제): {path}")

    def select_action(self, state):
        t = torch.FloatTensor(state).unsqueeze(0)
        if self.mode == Mode.INFERENCE:
            with torch.no_grad():
                logits, _ = self.net(t)
                
                action = logits.argmax(dim=-1)
            return action.item(), 0.0, 0.0
        else:
            logits, value = self.net(t)
        
            
            dist   = Categorical(logits=logits)
            
            if self.mode == Mode.TRAIN and torch.rand(1).item() < 0.15:
                action = torch.randint(0, 6, (1,)) # 0~5 중 완전 랜덤
            else:
                action = dist.sample()
                
            return action.item(), dist.log_prob(action).item(), value.item()

    def store(self, unit_id, state, action, reward, done, log_prob, value):
        if unit_id not in self.buffers:
            self.buffers[unit_id] = {
                "states":[], "actions":[], "rewards":[],
                "dones":[], "log_probs":[], "values":[]
            }
        buf = self.buffers[unit_id]
        buf["states"].append(state)
        buf["actions"].append(action)
        buf["rewards"].append(reward)
        buf["dones"].append(done)
        buf["log_probs"].append(log_prob)
        buf["values"].append(value)

    def compute_gae(self, buf):
        advantages, gae = [], 0
        for i in reversed(range(len(buf["rewards"]))):
            next_val = 0 if i == len(buf["rewards"])-1 else buf["values"][i+1]
            delta    = buf["rewards"][i] + self.gamma * next_val * (1-buf["dones"][i]) - buf["values"][i]
            gae      = delta + self.gamma * self.lam * (1-buf["dones"][i]) * gae
            advantages.insert(0, gae)
        return advantages

    def update(self, unit_id):
        if self.mode == Mode.INFERENCE: return
        buf = self.buffers.get(unit_id)
        if buf is None or len(buf["states"]) < 64: return

        print(f"[PPO] unit={unit_id} 학습 시작 buf={len(buf['states'])}")
        advantages = self.compute_gae(buf)

        states  = torch.FloatTensor(buf["states"])
        actions = torch.LongTensor(buf["actions"])
        old_lp  = torch.FloatTensor(buf["log_probs"])
        advs    = torch.FloatTensor(advantages)
        returns = advs + torch.FloatTensor(buf["values"])
        advs    = (advs - advs.mean()) / (advs.std() + 1e-8)

        for _ in range(5): # 업데이트 횟수 약간 증가
            logits, values = self.net(states)
            dist     = Categorical(logits=logits)
            new_lp   = dist.log_prob(actions)
            entropy  = dist.entropy().mean()

            ratio       = (new_lp - old_lp).exp()
            surr1       = ratio * advs
            surr2       = torch.clamp(ratio, 1-self.clip, 1+self.clip) * advs
            policy_loss = -torch.min(surr1, surr2).mean()
            value_loss  = nn.MSELoss()(values.squeeze(), returns)
            loss = policy_loss + 0.5 * value_loss - self.entropy_coef * entropy 

            self.optimizer.zero_grad()
            loss.backward()
            self.optimizer.step()

        print(f"[PPO] unit={unit_id} loss={loss.item():.4f}")
        self._save()
        for k in buf: buf[k].clear()

# ── 서버 ──────────────────────────────────────────────
mode = Mode.INFERENCE if "--infer" in sys.argv else Mode.TRAIN
ppo  = PPO(state_dim= STATE_DIM, action_dim= ACTION_DIM, mode=mode)

async def handler(websocket):
    print("[WS] Unity 연결됨")
    async for message in websocket:
        data     = json.loads(message)
        response = {"units": []}

        for unit in data["units"]:
            state = [
                unit["baseDist"],
                unit["baseDir"],
                unit["captureRatio"],
                unit["n0"], unit["n1"], unit["n2"],
                unit["n3"], unit["n4"], unit["n5"],
                float(unit["TargetDist"]),
                float(unit["TargetDir"]),
                float(unit["TargetActive"]) # 유니티에서 보낸 0 or 1
            ]

            action, log_prob, value = ppo.select_action(state)
            ppo.store(unit["id"], state, action, unit["reward"], unit["done"], log_prob, value)

            buf = ppo.buffers.get(unit["id"], {})
            if len(buf.get("states", [])) >= 64:
                ppo.update(unit["id"])

            if unit["done"]:
                ppo.update(unit["id"])
                print(f"[Episode] captureRatio={unit['captureRatio']:.2f}")

            response["units"].append({"id": unit["id"], "action": action})

        await websocket.send(json.dumps(response))

def input_listener():
    while True:
        cmd = input()
        if cmd == "s":
            ppo._save()
            ppo.save_onnx()
            print("[PPO] 수동 저장 완료")

async def main():
    threading.Thread(target=input_listener, daemon=True).start()
    async with websockets.serve(handler, "localhost", 8765):
        print(f"[WS] 서버 시작 | mode={ppo.mode.value}")
        print("'s' 입력시 수동 저장")
        await asyncio.Future()

if __name__ == "__main__":
    asyncio.run(main())