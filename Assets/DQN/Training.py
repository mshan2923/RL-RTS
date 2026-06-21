"""
Hex Tile RL Training Server
- HTTP POST 동기식 통신 (Unity <-> Python)
- DQN (Discrete 6-direction for Pointy-top Hexagon)
- State: [target_dir_x, target_dir_z, delta_distance] (3-dim)
- Action: 0~5 (6방향 이산)
- ONNX Export 엔드포인트 포함
"""

import json
import random
import threading
from collections import deque
from http.server import BaseHTTPRequestHandler, HTTPServer

import numpy as np
import torch
import torch.nn as nn
import torch.optim as optim

# ──────────────────────────────────────────
# 하이퍼파라미터
# ──────────────────────────────────────────
STATE_DIM   = 3          # [dir_x, dir_z, delta_dist]
ACTION_DIM  = 6          # Pointy-top 6방향
HIDDEN      = 128
LR          = 1e-3
GAMMA       = 0.99
BATCH_SIZE  = 64
MEMORY_SIZE = 50_000
EPSILON_START = 1.0
EPSILON_END   = 0.05
EPSILON_DECAY = 0.9995   # 스텝마다 곱셈
TARGET_UPDATE = 200      # 스텝마다 target net 동기
PORT        = 9000

# ──────────────────────────────────────────
# Q-Network
# ──────────────────────────────────────────
class QNet(nn.Module):
    def __init__(self):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(STATE_DIM, HIDDEN),
            nn.ReLU(),
            nn.Linear(HIDDEN, HIDDEN),
            nn.ReLU(),
            nn.Linear(HIDDEN, ACTION_DIM),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.net(x)


# ──────────────────────────────────────────
# Replay Buffer
# ──────────────────────────────────────────
class ReplayBuffer:
    def __init__(self, capacity: int):
        self.buf = deque(maxlen=capacity)

    def push(self, s, a, r, s_next, done):
        self.buf.append((s, a, r, s_next, done))

    def sample(self, n: int):
        batch = random.sample(self.buf, n)
        s, a, r, s2, d = zip(*batch)
        return (
            torch.tensor(s,  dtype=torch.float32),
            torch.tensor(a,  dtype=torch.long),
            torch.tensor(r,  dtype=torch.float32),
            torch.tensor(s2, dtype=torch.float32),
            torch.tensor(d,  dtype=torch.float32),
        )

    def __len__(self):
        return len(self.buf)


# ──────────────────────────────────────────
# DQN Agent (싱글톤)
# ──────────────────────────────────────────
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

    # ── 행동 선택 (ε-greedy) ──────────────
    def select_action(self, state: list[float]) -> int:
        if random.random() < self.epsilon:
            return random.randint(0, ACTION_DIM - 1)
        s = torch.tensor([state], dtype=torch.float32, device=self.device)
        with torch.no_grad():
            return int(self.online(s).argmax(dim=1).item())

    # ── 배치 학습 ─────────────────────────
    def learn(self):
        if len(self.memory) < BATCH_SIZE:
            return None

        s, a, r, s2, d = self.memory.sample(BATCH_SIZE)
        s, a, r, s2, d = (t.to(self.device) for t in (s, a, r, s2, d))

        q_pred = self.online(s).gather(1, a.unsqueeze(1)).squeeze(1)
        with torch.no_grad():
            q_next = self.target(s2).max(1).values
            q_tgt  = r + GAMMA * q_next * (1.0 - d)

        loss = nn.MSELoss()(q_pred, q_tgt)
        self.opt.zero_grad()
        loss.backward()
        nn.utils.clip_grad_norm_(self.online.parameters(), 10.0)
        self.opt.step()

        self.steps   += 1
        self.epsilon  = max(EPSILON_END, self.epsilon * EPSILON_DECAY)

        if self.steps % TARGET_UPDATE == 0:
            self.target.load_state_dict(self.online.state_dict())

        return float(loss.item())

    # ── ONNX 내보내기 ─────────────────────
    def export_onnx(self, path: str = "hex_dqn.onnx"):
        dummy = torch.zeros(1, STATE_DIM, device=self.device)
        torch.onnx.export(
            self.online,
            (dummy,),
            path,
            input_names=["state"],
            output_names=["q_values"],
            dynamic_axes={"state": {0: "batch"}, "q_values": {0: "batch"}},
            opset_version=17,
        )
        print(f"[Server] ONNX exported → {path}")
        return path


AGENT = DQNAgent()


# ──────────────────────────────────────────
# HTTP 핸들러
# ──────────────────────────────────────────
class RLHandler(BaseHTTPRequestHandler):

    def log_message(self, fmt, *args):
        # 콘솔 로그 억제 (필요 시 활성화)
        pass

    def _read_json(self):
        length = int(self.headers.get("Content-Length", 0))
        return json.loads(self.rfile.read(length))

    def _send_json(self, code: int, payload: dict):
        body = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):

        # ── /step : Unity → 상태 전송, 행동 반환 ──────────────────
        # Body: { "agents": [ { "id": int, "state": [dx,dz,dd],
        #                        "reward": float, "done": bool,
        #                        "prev_state": [dx,dz,dd], "prev_action": int } ] }
        # Response: { "actions": { "<id>": int, ... },
        #             "epsilon": float, "steps": int }
        if self.path == "/step":
            data = self._read_json()
            actions = {}
            loss_vals = []

            with AGENT.lock:
                for ag in data["agents"]:
                    aid = str(ag["id"])
                    state      = ag["state"]        # 현재 상태
                    prev_state = ag.get("prev_state")
                    prev_act   = ag.get("prev_action")
                    reward     = ag.get("reward", 0.0)
                    done       = ag.get("done", False)

                    # 이전 전이가 있으면 버퍼에 저장 후 학습
                    if prev_state is not None and prev_act is not None:
                        AGENT.memory.push(prev_state, prev_act, reward, state, float(done))
                        lv = AGENT.learn()
                        if lv is not None:
                            loss_vals.append(lv)

                    # 다음 행동 선택
                    actions[aid] = AGENT.select_action(state)

            resp = {
                "actions": actions,
                "epsilon": round(AGENT.epsilon, 4),
                "steps":   AGENT.steps,
                "loss":    round(float(np.mean(loss_vals)), 6) if loss_vals else None,
            }
            self._send_json(200, resp)
            return

        # ── /export : ONNX 파일 생성 ──────────────────────────────
        # Body: { "path": "hex_dqn.onnx" }  (선택)
        if self.path == "/export":
            body = self._read_json() if int(self.headers.get("Content-Length", 0)) > 0 else {}
            out_path = body.get("path", "hex_dqn.onnx")
            with AGENT.lock:
                saved = AGENT.export_onnx(out_path)
            self._send_json(200, {"status": "ok", "file": saved})
            return

        # ── /status : 현재 학습 상태 조회 ────────────────────────
        if self.path == "/status":
            self._send_json(200, {
                "steps":       AGENT.steps,
                "epsilon":     round(AGENT.epsilon, 4),
                "memory_size": len(AGENT.memory),
                "device":      str(AGENT.device),
            })
            return

        self._send_json(404, {"error": "not found"})


# ──────────────────────────────────────────
# 엔트리포인트
# ──────────────────────────────────────────
if __name__ == "__main__":
    server = HTTPServer(("0.0.0.0", PORT), RLHandler)
    print(f"[Server] DQN RL Server listening on :{PORT}")
    print(f"[Server] Device: {'CUDA' if torch.cuda.is_available() else 'CPU'}")
    print(f"[Server] Endpoints: POST /step  |  POST /export  |  POST /status")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[Server] Shutting down.")
        server.server_close()