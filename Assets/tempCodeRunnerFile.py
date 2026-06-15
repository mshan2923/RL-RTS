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

MODEL_PATH = "ppo_hex.pt"

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
    def __init__(self, state_dim=6, action_dim=6, mode=Mode.TRAIN):
        self.net       = PPONetwork(state_dim, action_dim)
        self.optimizer = optim.Adam(self.net.parameters(), lr=0.05)
        self.mode      = mode
        self.clip      = 0.2
        self.gamma     = 0.99
        self.lam       = 0.95
        self.entropy_coef = 0.01  # 일단 이 정도로 시작해!
        self.buffers   = {}
        self._load()

    def _load(self):
        try:
            self.net.load_state_dict(torch.load(MODEL_PATH))
            print(f"[PPO] 모델 로드: {MODEL_PATH}")
        except:
            print("[PPO] 새로 학습 시작")

    def _save(self):
        torch.save(self.net.state_dict(), MODEL_PATH)
        print(f"[PPO] 저장: {MODEL_PATH}")

    def save_onnx(self, path="ppo_hex.onnx"):
        dummy = (torch.FloatTensor([[0] * 6]),)
        torch.onnx.export(
            self.net, dummy, path,
            input_names=["state"],
            output_names=["policy", "value"],
            opset_version=17
        )
        print(f"[PPO] ONNX 저장: {path}")

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

        for _ in range(4):
            logits, values = self.net(states)
            dist     = Categorical(logits=logits)
            new_lp   = dist.log_prob(actions)
            entropy  = dist.entropy().mean()

            ratio       = (new_lp - old_lp).exp()
            surr1       = ratio * advs
            surr2       = torch.clamp(ratio, 1-self.clip, 1+self.clip) * advs
            policy_loss = -torch.min(surr1, surr2).mean()
            value_loss  = nn.MSELoss()(values.squeeze(), returns)
            loss        = policy_loss + 0.5 * value_loss - 0.01 * entropy

            self.optimizer.zero_grad()
            loss.backward()
            self.optimizer.step()

        print(f"[PPO] unit={unit_id} loss={loss.item():.4f}")
        self._save()
        for k in buf: buf[k].clear()

# ── 서버 ──────────────────────────────────────────────
mode = Mode.INFERENCE if "--infer" in sys.argv else Mode.TRAIN
ppo  = PPO(state_dim=9, action_dim=6, mode=mode)

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
                unit["n3"], unit["n4"], unit["n5"]
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