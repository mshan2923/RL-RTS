import asyncio
import websockets
import json
import torch
import torch.nn as nn
import torch.optim as optim
from torch.distributions import Categorical
from enum import Enum

MODEL_PATH = "ppo_hex.pt"

class Mode(Enum):
    TRAIN    = "train"
    INFERENCE = "inference"

class PPONetwork(nn.Module):
    def __init__(self, state_dim, action_dim):
        super().__init__()
        self.shared = nn.Sequential(
            nn.Linear(state_dim, 128), nn.ReLU(),
            nn.Linear(128, 128),       nn.ReLU()
        )
        self.policy = nn.Linear(128, action_dim)
        self.value  = nn.Linear(128, 1)

    def forward(self, x):
        x = self.shared(x)
        return self.policy(x), self.value(x)

class PPO:
    def __init__(self, state_dim=4, action_dim=6, mode=Mode.TRAIN):
        self.net       = PPONetwork(state_dim, action_dim)
        self.optimizer = optim.Adam(self.net.parameters(), lr=3e-4)
        self.mode      = mode
        self.clip      = 0.2
        self.gamma     = 0.99
        self.lam       = 0.95

        self.states    = []
        self.actions   = []
        self.rewards   = []
        self.dones     = []
        self.log_probs = []
        self.values    = []

        # 저장된 모델 있으면 로드
        self._load()

    def set_mode(self, mode: Mode):
        self.mode = mode
        if mode == Mode.INFERENCE:
            self.net.eval()
            print("[PPO] 추론 모드")
        else:
            self.net.train()
            print("[PPO] 학습 모드")

    def _load(self):
        try:
            self.net.load_state_dict(torch.load(MODEL_PATH))
            print(f"[PPO] 모델 로드 완료: {MODEL_PATH}")
        except:
            print("[PPO] 저장된 모델 없음, 새로 학습")

    def _save(self):
        torch.save(self.net.state_dict(), MODEL_PATH)
        print(f"[PPO] 모델 저장: {MODEL_PATH}")

    def select_action(self, state):
        t = torch.FloatTensor(state).unsqueeze(0)

        if self.mode == Mode.INFERENCE:
            with torch.no_grad():
                logits, _ = self.net(t)
                action    = logits.argmax(dim=-1)  # 추론은 greedy
            return action.item(), 0.0, 0.0
        else:
            logits, value = self.net(t)
            dist          = Categorical(logits=logits)
            action        = dist.sample()
            return action.item(), dist.log_prob(action).item(), value.item()

    def store(self, state, action, reward, done, log_prob, value):
        if self.mode == Mode.INFERENCE: return  # 추론 모드엔 저장 안 함
        self.states.append(state)
        self.actions.append(action)
        self.rewards.append(reward)
        self.dones.append(done)
        self.log_probs.append(log_prob)
        self.values.append(value)

    def compute_gae(self, next_value):
        advantages, gae = [], 0
        for i in reversed(range(len(self.rewards))):
            next_val = next_value if i == len(self.rewards)-1 else self.values[i+1]
            delta    = self.rewards[i] + self.gamma * next_val * (1-self.dones[i]) - self.values[i]
            gae      = delta + self.gamma * self.lam * (1-self.dones[i]) * gae
            advantages.insert(0, gae)
        return advantages

    def update(self):
        if self.mode == Mode.INFERENCE: return
        if len(self.states) < 64: return

        _, next_value = self.net(torch.FloatTensor(self.states[-1]).unsqueeze(0))
        advantages    = self.compute_gae(next_value.item())

        states   = torch.FloatTensor(self.states)
        actions  = torch.LongTensor(self.actions)
        old_lp   = torch.FloatTensor(self.log_probs)
        advs     = torch.FloatTensor(advantages)
        returns  = advs + torch.FloatTensor(self.values)
        advs     = (advs - advs.mean()) / (advs.std() + 1e-8)

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

        print(f"[PPO] loss={loss.item():.4f} | buf={len(self.states)}")
        self._save()
        self.states.clear(); self.actions.clear(); self.rewards.clear()
        self.dones.clear();  self.log_probs.clear(); self.values.clear()

# ── 서버 ─────────────────────────────────────────────
import sys
mode = Mode.INFERENCE if "--infer" in sys.argv else Mode.TRAIN
ppo  = PPO(state_dim=4, action_dim=6, mode=mode)

async def handler(websocket):
    print("[WS] Unity connected")
    async for message in websocket:
        data     = json.loads(message)
        response = {"units": []}

        for unit in data["units"]:
            state = [
                unit["col"],
                unit["row"],
                unit["yaw"],
                data["captureRatio"]
            ]

            action, log_prob, value = ppo.select_action(state)
            ppo.store(state, action, unit["reward"], unit["done"], log_prob, value)

            if unit["done"]:
                ppo.update()
                print(f"[Episode] captureRatio={data['captureRatio']:.2f}")

            response["units"].append({"id": unit["id"], "action": action})

        await websocket.send(json.dumps(response))

async def main():
    async with websockets.serve(handler, "localhost", 8765):
        print(f"[WS] Server started | mode={ppo.mode.value}")
        await asyncio.Future()

if __name__ == "__main__":
    asyncio.run(main())