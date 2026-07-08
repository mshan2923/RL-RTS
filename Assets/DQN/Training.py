"""
Hex Tile RL Training Server
- HTTP POST 동기식 통신 (Unity <-> Python)
- DQN 6방향 이산 / Pointy-top Hexagon
- 콘솔에서  E  키 → ONNX 저장,  Q  키 → 종료
"""

import json, os, random, sys, threading
from collections import deque
from http.server import BaseHTTPRequestHandler, HTTPServer

import numpy as np
import torch, torch.nn as nn, torch.optim as optim

# ── 하이퍼파라미터 ──────────────────────────────────────────────────
STATE_DIM     = 3
ACTION_DIM    = 6
HIDDEN        = 128
LR            = 1e-3
GAMMA         = 0.99
BATCH_SIZE    = 64
MEMORY_SIZE   = 50_000
EPSILON_START = 1.0
EPSILON_END   = 0.05
EPSILON_DECAY = 0.9995
TARGET_UPDATE = 200
PORT          = 9000
ONNX_PATH     = os.path.join(os.path.dirname(os.path.abspath(__file__)), "hex_dqn.onnx")

# ── Q-Network ───────────────────────────────────────────────────────
class QNet(nn.Module):
    def __init__(self):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(STATE_DIM, HIDDEN), nn.ReLU(),
            nn.Linear(HIDDEN,    HIDDEN), nn.ReLU(),
            nn.Linear(HIDDEN,    ACTION_DIM),
        )
    def forward(self, x): return self.net(x)

# ── Replay Buffer ───────────────────────────────────────────────────
class ReplayBuffer:
    def __init__(self, cap): self.buf = deque(maxlen=cap)
    def push(self, *t):      self.buf.append(t)
    def __len__(self):       return len(self.buf)
    def sample(self, n):
        s,a,r,s2,d = zip(*random.sample(self.buf, n))
        return (torch.tensor(s,  dtype=torch.float32),
                torch.tensor(a,  dtype=torch.long),
                torch.tensor(r,  dtype=torch.float32),
                torch.tensor(s2, dtype=torch.float32),
                torch.tensor(d,  dtype=torch.float32))

# ── DQN Agent ───────────────────────────────────────────────────────
class DQNAgent:
    def __init__(self):
        self.device  = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        self.online  = QNet().to(self.device)
        self.target  = QNet().to(self.device)
        self.target.load_state_dict(self.online.state_dict())
        self.target.eval()
        self.opt     = optim.Adam(self.online.parameters(), lr=LR)
        self.memory  = ReplayBuffer(MEMORY_SIZE)
        self.epsilon = EPSILON_START
        self.steps   = 0
        self.lock    = threading.Lock()

    def select_action(self, state):
        if random.random() < self.epsilon:
            return random.randint(0, ACTION_DIM - 1)
        s = torch.tensor([state], dtype=torch.float32, device=self.device)
        with torch.no_grad():
            return int(self.online(s).argmax(1).item())

    def learn(self):
        if len(self.memory) < BATCH_SIZE: return None
        s,a,r,s2,d = (t.to(self.device) for t in self.memory.sample(BATCH_SIZE))
        q  = self.online(s).gather(1, a.unsqueeze(1)).squeeze(1)
        with torch.no_grad():
            qt = r + GAMMA * self.target(s2).max(1).values * (1 - d)
        loss = nn.MSELoss()(q, qt)
        self.opt.zero_grad(); loss.backward()
        nn.utils.clip_grad_norm_(self.online.parameters(), 10.0)
        self.opt.step()
        self.steps  += 1
        self.epsilon = max(EPSILON_END, self.epsilon * EPSILON_DECAY)
        if self.steps % TARGET_UPDATE == 0:
            self.target.load_state_dict(self.online.state_dict())
        return float(loss)

    def export_onnx(self, path=ONNX_PATH):
        self.online.eval()
        torch.onnx.export(
            self.online,
            (torch.zeros(1, STATE_DIM, device=self.device),),
            path,
            input_names=["state"], output_names=["q_values"],
            dynamic_axes={"state":{0:"batch"}, "q_values":{0:"batch"}},
            opset_version=17,
            export_params=True,
            do_constant_folding=True,
        )
        self.online.train()
        kb = os.path.getsize(path) / 1024
        print(f"\n[Server] ONNX saved → {path}  ({kb:.1f} KB)  steps={self.steps}  ε={self.epsilon:.3f}\n> ", end="", flush=True)

AGENT = DQNAgent()

# ── HTTP 핸들러 ─────────────────────────────────────────────────────
class RLHandler(BaseHTTPRequestHandler):
    def log_message(self, *_): pass

    def _json(self):
        return json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))

    def _send(self, code, data):
        body = json.dumps(data).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        if self.path == "/step":
            data = self._json()
            actions = {}
            losses  = []
            is_episode_end = data.get("is_episode_end", False)

            with AGENT.lock:
                # 전이 저장 + 매 스텝 학습
                for ag in data.get("agents", []):
                    ps, pa = ag.get("prev_state"), ag.get("prev_action")
                    if ps is not None and pa is not None:
                        AGENT.memory.push(ps, pa, ag.get("reward", 0),
                                          ag["state"], float(ag.get("done", False)))
                        lv = AGENT.learn()
                        if lv: losses.append(lv)
                        
                    actions[str(ag["id"])] = AGENT.select_action(ag["state"])

                # 에피소드 끝이면 추가 배치 업데이트
                if is_episode_end and len(AGENT.memory) >= BATCH_SIZE:
                    for _ in range(32):
                        lv = AGENT.learn()
                        if lv: losses.append(lv)

            self._send(200, {
                "actions": actions,
                "epsilon": round(AGENT.epsilon, 4),
                "steps":   AGENT.steps,
                "loss":    round(float(np.mean(losses)), 6) if losses else None,
            })
            return

        if self.path == "/status":
            self._send(200, {"steps": AGENT.steps, "epsilon": round(AGENT.epsilon, 4),
                             "memory": len(AGENT.memory), "device": str(AGENT.device)})
            return

        self._send(404, {"error": "not found"})

# ── 콘솔 키 입력 스레드 ─────────────────────────────────────────────
def console_loop(server):
    print("[Server] 명령어:  E = ONNX 저장    Q = 종료")
    print("> ", end="", flush=True)
    for line in sys.stdin:
        cmd = line.strip().upper()
        if cmd == "E":
            with AGENT.lock:
                AGENT.export_onnx()
        elif cmd == "Q":
            print("[Server] 종료 중...")
            server.shutdown()
            break
        else:
            print(f"[Server] 알 수 없는 명령: {cmd!r}  (E=export, Q=quit)")
        print("> ", end="", flush=True)

# ── 엔트리포인트 ────────────────────────────────────────────────────
if __name__ == "__main__":
    server = HTTPServer(("0.0.0.0", PORT), RLHandler)
    print(f"[Server] :{PORT}  |  device={'CUDA' if torch.cuda.is_available() else 'CPU'}")
    t = threading.Thread(target=console_loop, args=(server,), daemon=True)
    t.start()
    server.serve_forever()
    print("[Server] 종료.")