"""
train_server.py  ─  Target Seek RL 학습 서버
=========================================================
Unity 와 TCP 소켓으로 통신하며 DQN 학습 진행.
ONNX 는 학습 도중 언제든 수동으로 추출 가능.

[프로토콜]  개행(\n) 구분 JSON, 기본 포트 9000

  Unity → Python
    {"type":"step",
     "agents":[{"id":0,"obs":[angleNorm,distDelta],"reward":0.1,"done":false}, ...]}

  Python → Unity
    {"actions":[1,0,2,...]}

  Unity → Python  (UI 버튼 등)
    {"type":"export"}
  Python → Unity
    {"status":"ok","path":"checkpoints/q_net.onnx"}

[콘솔]  실행 중 'e' + Enter → ONNX 즉시 export
"""

import json, os, random, socket, sys, threading
from collections import deque

import numpy as np
import torch
import torch.nn as nn


# ══════════════════════════════════════════════════════════════════
#  모델
# ══════════════════════════════════════════════════════════════════
class QNetwork(nn.Module):
    """입력 2개(angleNorm, distDelta) → Q값 3개(좌회전/직진/우회전)"""

    def __init__(self, state_dim: int = 2, action_dim: int = 3, hidden: int = 64):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(state_dim, hidden), nn.ReLU(),
            nn.Linear(hidden, hidden),   nn.ReLU(),
            nn.Linear(hidden, action_dim),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.net(x)


# ══════════════════════════════════════════════════════════════════
#  리플레이 버퍼
# ══════════════════════════════════════════════════════════════════
class ReplayBuffer:
    def __init__(self, capacity: int = 100_000):
        self.buf = deque(maxlen=capacity)

    def push(self, s, a, r, s2, done):
        self.buf.append((s, a, r, s2, float(done)))

    def sample(self, n: int):
        batch = random.sample(self.buf, n)
        s, a, r, s2, d = zip(*batch)
        return (np.array(s,  dtype=np.float32),
                np.array(a,  dtype=np.int64),
                np.array(r,  dtype=np.float32),
                np.array(s2, dtype=np.float32),
                np.array(d,  dtype=np.float32))

    def __len__(self):
        return len(self.buf)


# ══════════════════════════════════════════════════════════════════
#  DQN 에이전트
# ══════════════════════════════════════════════════════════════════
class DQNAgent:
    def __init__(self, lr: float = 1e-3, gamma: float = 0.99):
        self.gamma = gamma
        self.q   = QNetwork()
        self.tgt = QNetwork()
        self.tgt.load_state_dict(self.q.state_dict())
        self.tgt.eval()
        self.opt = torch.optim.Adam(self.q.parameters(), lr=lr)

    # ε-greedy 행동 선택
    def act(self, obs: list, eps: float) -> int:
        if random.random() < eps:
            return random.randrange(3)
        t = torch.tensor(obs, dtype=torch.float32).unsqueeze(0)
        with torch.no_grad():
            return int(self.q(t).argmax(1).item())

    # 학습 1스텝
    def train_step(self, batch) -> float:
        s, a, r, s2, d = (torch.tensor(x) for x in batch)
        q_val  = self.q(s).gather(1, a.unsqueeze(1)).squeeze(1)
        with torch.no_grad():
            target = r + self.gamma * self.tgt(s2).max(1)[0] * (1 - d)
        loss = nn.functional.mse_loss(q_val, target)
        self.opt.zero_grad()
        loss.backward()
        self.opt.step()
        return float(loss.item())

    def sync_target(self):
        self.tgt.load_state_dict(self.q.state_dict())

    # 체크포인트 저장/로드
    def save(self, path: str):
        os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
        torch.save(self.q.state_dict(), path)

    def load(self, path: str):
        sd = torch.load(path, map_location="cpu")
        self.q.load_state_dict(sd)
        self.tgt.load_state_dict(sd)

    # ONNX export  (Unity Inference Engine 입력/출력 이름 고정)
    def export_onnx(self, path: str):
        os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
        self.q.eval()
        dummy = torch.zeros(1, 2)
        torch.onnx.export(
            self.q, dummy, path,
            input_names=["obs"],
            output_names=["q_values"],
            dynamic_axes={"obs": {0: "batch"}, "q_values": {0: "batch"}},
            opset_version=13,
            dynamo=False,   # 레거시 TorchScript 기반, opset 고정
        )
        self.q.train()
        print(f"[Export] ONNX → {path}")
        print(f"         obs(batch,2) → q_values(batch,3)  argmax = 행동 선택")


# ══════════════════════════════════════════════════════════════════
#  학습 서버
# ══════════════════════════════════════════════════════════════════
class RLServer:
    CKPT = "checkpoints/q_net.pt"
    ONNX = "checkpoints/q_net.onnx"

    def __init__(
        self,
        host: str  = "0.0.0.0",
        port: int  = 9000,
        batch: int = 64,
        warmup: int          = 500,
        target_update: int   = 300,
        eps_start: float     = 1.0,
        eps_end: float       = 0.05,
        eps_decay: int       = 30_000,
        save_every: int      = 2_000,
        log_every: int       = 200,
    ):
        self.host, self.port = host, port
        self.batch, self.warmup = batch, warmup
        self.target_update = target_update
        self.eps_start, self.eps_end, self.eps_decay = eps_start, eps_end, eps_decay
        self.save_every, self.log_every = save_every, log_every

        self.agent  = DQNAgent()
        self.buf    = ReplayBuffer()
        self.prev   = {}         # {agent_id: (obs, action)}  학습용 이전 상태 캐시
        self.steps  = 0
        self._export_req = False # 콘솔 'e' 플래그

    # ── 엡실론 스케줄 ─────────────────────────────────────────────
    def _eps(self) -> float:
        frac = min(1.0, self.steps / self.eps_decay)
        return self.eps_start + frac * (self.eps_end - self.eps_start)

    # ── step 메시지 처리 ─────────────────────────────────────────
    def _handle_step(self, msg: dict) -> dict:
        actions = []
        for a in msg["agents"]:
            aid, obs, rew, done = a["id"], a["obs"], a["reward"], a["done"]

            # 이전 (s,a) 존재하면 전환 저장
            if aid in self.prev:
                prev_obs, prev_act = self.prev.pop(aid) if done else (None, None)
                if prev_obs is None:               # done=False → pop 하지 말고 peek
                    prev_obs, prev_act = self.prev[aid]
                self.buf.push(prev_obs, prev_act, rew, obs, done)

                # done=True 면 이 에이전트 캐시 제거 (에피소드 종료)
                if done and aid in self.prev:
                    del self.prev[aid]

            # 행동 선택
            action = self.agent.act(obs, self._eps())
            if not done:
                self.prev[aid] = (obs, action)
            actions.append(action)

        # 학습
        if len(self.buf) >= max(self.batch, self.warmup):
            loss = self.agent.train_step(self.buf.sample(self.batch))
            self.steps += 1

            if self.steps % self.target_update == 0:
                self.agent.sync_target()

            if self.steps % self.save_every == 0:
                self.agent.save(self.CKPT)
                print(f"[Step {self.steps}]  ε={self._eps():.3f}  loss={loss:.4f}  buf={len(self.buf)}")

            elif self.steps % self.log_every == 0:
                print(f"[Step {self.steps}]  ε={self._eps():.3f}  loss={loss:.4f}")

        return {"actions": actions}

    # ── ONNX 추출 ────────────────────────────────────────────────
    def _do_export(self):
        self.agent.save(self.CKPT)
        self.agent.export_onnx(self.ONNX)

    # ── 콘솔 감시 스레드 ('e' → export) ─────────────────────────
    def _stdin_watcher(self):
        for line in sys.stdin:
            if line.strip().lower() == "e":
                self._export_req = True

    # ── 서버 메인 루프 ────────────────────────────────────────────
    def run(self):
        threading.Thread(target=self._stdin_watcher, daemon=True).start()

        srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        srv.bind((self.host, self.port))
        srv.listen(1)
        print(f"[Server] {self.host}:{self.port} 대기 중...")
        print(f"         콘솔에서 'e' + Enter → 즉시 ONNX export")
        print(f"         Unity UI 버튼 → {{\"type\":\"export\"}} 전송해도 됩니다\n")

        while True:
            conn, addr = srv.accept()
            print(f"[Server] Unity 연결: {addr}")
            self.prev.clear()   # 새 연결 시 에이전트 캐시 초기화

            try:
                rf = conn.makefile("r", encoding="utf-8")
                wf = conn.makefile("w", encoding="utf-8")

                while True:
                    # export 요청 확인 (콘솔)
                    if self._export_req:
                        self._do_export()
                        self._export_req = False

                    line = rf.readline()
                    if not line:
                        break

                    msg = json.loads(line)
                    t = msg.get("type")

                    if t == "step":
                        resp = self._handle_step(msg)
                    elif t == "export":
                        self._do_export()
                        resp = {"status": "ok", "path": self.ONNX}
                    else:
                        resp = {"error": f"unknown type: {t}"}

                    wf.write(json.dumps(resp) + "\n")
                    wf.flush()

            except Exception as e:
                print(f"[Server] 연결 종료: {e}")
            finally:
                conn.close()
                print("[Server] 재연결 대기...\n")


# ══════════════════════════════════════════════════════════════════
if __name__ == "__main__":
    RLServer().run()