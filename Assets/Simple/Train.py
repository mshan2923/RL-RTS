"""
train_server_ppo.py  ─  PPO 학습 서버 (캐릭터 이동: vx, vz 정규화)
=================================================================
행동 (연속 2차원):
  action[0] = vx  ∈ [-1, 1]   (좌/우)
  action[1] = vz  ∈ [-1, 1]   (아래/위)
  → Unity 에서 Vector3(vx,0,vz).normalized * moveSpeed 로 적용
    = 항상 일정 속도, 방향만 결정 (캐릭터처럼)

관측 (2개):
  obs[0] = 에이전트 → 목표 월드 X 방향 성분 (정규화)
  obs[1] = 에이전트 → 목표 월드 Z 방향 성분 (정규화)

[export / 종료 수정]
  - conn.settimeout(0.3) → readline 이 블록되지 않아 export 즉시 반응
  - KeyboardInterrupt → ONNX export 후 정상 종료
  - export 에러 시 연결 유지, 에러 로그 출력

프로토콜: 개행 구분 JSON  포트 9000
  Unity→Python  {"type":"step","agents":[{"id":0,"obs":[dx,dz],"reward":r,"done":b},...]}
  Python→Unity  {"actions":[vx0,vz0,vx1,vz1,...]}
  Unity→Python  {"type":"export"}
  Python→Unity  {"status":"ok","path":"..."} | {"status":"error","msg":"..."}
  콘솔 'e'+Enter → ONNX export   Ctrl+C → export 후 종료
"""

import json, math, os, random, socket, sys, threading
import numpy as np
import torch
import torch.nn as nn
from torch.distributions import Normal


# ══════════════════════════════════════════════════════════════════
#  관측 정규화
# ══════════════════════════════════════════════════════════════════
class RunningMeanStd:
    def __init__(self, shape=(2,), eps=1e-4):
        self.mean  = np.zeros(shape, np.float64)
        self.var   = np.ones(shape,  np.float64)
        self.count = eps

    def update(self, x):
        x = np.atleast_2d(np.asarray(x, np.float64))
        n, delta = x.shape[0], x.mean(0) - self.mean
        tot = self.count + n
        self.mean += delta * n / tot
        self.var   = (self.var * self.count + x.var(0) * n
                      + delta**2 * self.count * n / tot) / tot
        self.count = tot

    def normalize(self, x):
        return ((np.asarray(x, np.float32) - self.mean)
                / (np.sqrt(self.var) + 1e-8)).astype(np.float32)


# ══════════════════════════════════════════════════════════════════
#  Actor-Critic
# ══════════════════════════════════════════════════════════════════
class ActorCritic(nn.Module):
    def __init__(self, obs_dim=2, act_dim=2, hidden=64):
        super().__init__()
        self.backbone = nn.Sequential(
            nn.Linear(obs_dim, hidden), nn.Tanh(),
            nn.Linear(hidden, hidden),  nn.Tanh(),
        )
        self.actor_mean = nn.Linear(hidden, act_dim)
        self.log_std    = nn.Parameter(torch.zeros(act_dim) - 0.5)
        self.critic     = nn.Linear(hidden, 1)

        for layer in self.backbone:
            if isinstance(layer, nn.Linear):
                nn.init.orthogonal_(layer.weight, gain=math.sqrt(2))
                nn.init.zeros_(layer.bias)
        nn.init.orthogonal_(self.actor_mean.weight, gain=0.01)
        nn.init.zeros_(self.actor_mean.bias)
        nn.init.orthogonal_(self.critic.weight, gain=1.0)
        nn.init.zeros_(self.critic.bias)

    def get_action_and_value(self, obs, action=None):
        feat = self.backbone(obs)
        mean = torch.tanh(self.actor_mean(feat))
        std  = self.log_std.exp().expand_as(mean)
        dist = Normal(mean, std)
        if action is None:
            action = dist.sample().clamp(-1.0, 1.0)
        log_prob = dist.log_prob(action).sum(-1)
        entropy  = dist.entropy().sum(-1)
        value    = self.critic(feat).squeeze(-1)
        return action, log_prob, entropy, value

    def get_value(self, obs):
        return self.critic(self.backbone(obs)).squeeze(-1)


# ══════════════════════════════════════════════════════════════════
#  PPO 에이전트
# ══════════════════════════════════════════════════════════════════
class PPOAgent:
    CKPT = "checkpoints/ppo.pt"
    ONNX = "checkpoints/ppo_actor.onnx"

    def __init__(self, lr=3e-4, gamma=0.99, lam=0.95,
                 clip=0.2, n_epochs=8, batch_size=64,
                 vf_coef=0.5, ent_coef=0.01, max_grad_norm=0.5):
        self.gamma, self.lam = gamma, lam
        self.clip, self.n_epochs, self.batch_size = clip, n_epochs, batch_size
        self.vf_coef, self.ent_coef = vf_coef, ent_coef
        self.max_grad_norm = max_grad_norm

        self.ac  = ActorCritic()
        self.opt = torch.optim.Adam(self.ac.parameters(), lr=lr, eps=1e-5)

    @torch.no_grad()
    def select(self, obs_np):
        obs = torch.tensor(obs_np, dtype=torch.float32).unsqueeze(0)
        act, lp, _, val = self.ac.get_action_and_value(obs)
        return act.squeeze(0).tolist(), float(lp), float(val)

    @staticmethod
    def _gae(rewards, values, dones, last_val, gamma, lam):
        T, adv, g = len(rewards), [0.0]*len(rewards), 0.0
        for t in reversed(range(T)):
            nxt  = values[t+1] if t < T-1 else last_val
            mask = 1.0 - float(dones[t])
            delta = rewards[t] + gamma * nxt * mask - values[t]
            g     = delta + gamma * lam * mask * g
            adv[t] = g
        return adv, [a+v for a,v in zip(adv, values)]

    def update(self, trajectories):
        all_obs, all_acts, all_lps, all_rets, all_advs = [], [], [], [], []
        for traj in trajectories:
            lv = 0.0 if traj["dones"][-1] else float(
                self.ac.get_value(torch.tensor([traj["obs"][-1]], dtype=torch.float32)))
            adv, ret = self._gae(traj["rewards"], traj["values"],
                                  traj["dones"], lv, self.gamma, self.lam)
            all_obs.extend(traj["obs"]); all_acts.extend(traj["actions"])
            all_lps.extend(traj["log_probs"])
            all_rets.extend(ret); all_advs.extend(adv)

        obs  = torch.tensor(all_obs,  dtype=torch.float32)
        acts = torch.tensor(all_acts, dtype=torch.float32)
        lps  = torch.tensor(all_lps,  dtype=torch.float32)
        rets = torch.tensor(all_rets, dtype=torch.float32)
        advs = torch.tensor(all_advs, dtype=torch.float32)
        advs = (advs - advs.mean()) / (advs.std() + 1e-8)

        total, cnt = 0.0, 0
        for _ in range(self.n_epochs):
            for b in torch.randperm(len(obs)).split(self.batch_size):
                _, new_lp, ent, vals = self.ac.get_action_and_value(obs[b], acts[b])
                ratio    = (new_lp - lps[b]).exp()
                a        = advs[b]
                loss     = (-torch.min(ratio*a, ratio.clamp(1-self.clip, 1+self.clip)*a).mean()
                            + self.vf_coef * 0.5 * (vals - rets[b]).pow(2).mean()
                            - self.ent_coef * ent.mean())
                self.opt.zero_grad(); loss.backward()
                nn.utils.clip_grad_norm_(self.ac.parameters(), self.max_grad_norm)
                self.opt.step()
                total += loss.item(); cnt += 1

        return total / max(cnt, 1)

    def save(self, path=CKPT):
        os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
        torch.save(self.ac.state_dict(), path)
        print(f"[Save] {path}")

    def export_onnx(self, path=ONNX):
        """결정론적 Actor 만 추출 (Unity Inference Engine 용)"""
        os.makedirs(os.path.dirname(path) or ".", exist_ok=True)

        class _Actor(nn.Module):
            def __init__(self, ac):
                super().__init__()
                self.backbone, self.actor_mean = ac.backbone, ac.actor_mean
            def forward(self, obs):
                return torch.tanh(self.actor_mean(self.backbone(obs)))

        actor = _Actor(self.ac)
        actor.eval()
        torch.onnx.export(
            actor, torch.zeros(1, 2), path,
            input_names=["obs"], output_names=["action"],
            dynamic_axes={"obs": {0: "batch"}, "action": {0: "batch"}},
            opset_version=13, dynamo=False,
        )
        actor.train(); self.ac.train()
        print(f"[Export] ONNX → {path}")
        print(f"         obs(batch,2=[dx_norm,dz_norm]) → action(batch,2=[vx,vz]) ∈ [-1,1]")
        print(f"         Unity: moveDir = Vector3(vx,0,vz).normalized * moveSpeed")


# ══════════════════════════════════════════════════════════════════
#  PPO 서버
# ══════════════════════════════════════════════════════════════════
class PPOServer:
    RECV_TIMEOUT = 0.3   # readline 타임아웃 (초) — export 플래그 체크 주기

    def __init__(self, host="0.0.0.0", port=9000, save_every=50):
        self.host, self.port = host, port
        self.save_every = save_every

        self.agent   = PPOAgent()
        self.obs_rms = RunningMeanStd(shape=(2,))
        self.traj    = {}   # {agent_id: {"obs":[], ...}}
        self.prev    = {}   # {agent_id: (obs, action, log_prob, value)}

        self.episode     = 0
        self._export_req = False
        self._shutdown   = threading.Event()

    def _empty_traj(self):
        return {"obs": [], "actions": [], "log_probs": [],
                "values": [], "rewards": [], "dones": []}

    # ── 스텝 처리 ─────────────────────────────────────────────
    def _handle_step(self, msg):
        flat_actions = []
        all_done = True

        for a in msg["agents"]:
            aid, raw, rew, done = a["id"], a["obs"], a["reward"], a["done"]

            self.obs_rms.update(np.array(raw))
            obs = self.obs_rms.normalize(np.array(raw)).tolist()

            if aid in self.prev:
                po, pa, plp, pv = self.prev[aid]
                if aid not in self.traj:
                    self.traj[aid] = self._empty_traj()
                t = self.traj[aid]
                t["obs"].append(po); t["actions"].append(pa)
                t["log_probs"].append(plp); t["values"].append(pv)
                t["rewards"].append(rew); t["dones"].append(float(done))
                if done:
                    del self.prev[aid]

            action, lp, val = self.agent.select(obs)
            flat_actions.extend(action)

            if not done:
                self.prev[aid] = (obs, action, lp, val)
                all_done = False

        if all_done and self.traj:
            self._run_ppo_update()

        return {"actions": flat_actions}

    def _run_ppo_update(self):
        self.episode += 1
        trajs  = list(self.traj.values())
        n_steps = sum(len(t["rewards"]) for t in trajs)
        loss   = self.agent.update(trajs)
        print(f"[Ep {self.episode:4d}]  steps={n_steps}  loss={loss:.4f}")
        if self.episode % self.save_every == 0:
            self.agent.save()
        self.traj.clear(); self.prev.clear()

    # ── export ────────────────────────────────────────────────
    def _do_export(self):
        try:
            self.agent.save()
            self.agent.export_onnx()
            return {"status": "ok", "path": PPOAgent.ONNX}
        except Exception as e:
            print(f"[Export ERROR] {e}")
            return {"status": "error", "msg": str(e)}

    def _stdin_watcher(self):
        for line in sys.stdin:
            cmd = line.strip().lower()
            if cmd == "e":
                self._export_req = True
                print("[stdin] export 요청 접수")
            elif cmd in ("q", "quit", "exit"):
                self._shutdown.set()

    # ── 서버 루프 ─────────────────────────────────────────────
    def run(self):
        threading.Thread(target=self._stdin_watcher, daemon=True).start()

        srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        srv.settimeout(1.0)   # accept 도 타임아웃 → Ctrl+C 즉시 반응
        srv.bind((self.host, self.port))
        srv.listen(1)
        print(f"[PPO Server] {self.host}:{self.port} 대기 중...")
        print(f"  'e' + Enter → ONNX export")
        print(f"  'q' + Enter 또는 Ctrl+C → export 후 종료\n")

        try:
            while not self._shutdown.is_set():
                try:
                    conn, addr = srv.accept()
                except socket.timeout:
                    continue

                print(f"[Server] Unity 연결: {addr}")
                self.traj.clear(); self.prev.clear()

                # ── 핵심: readline 타임아웃으로 export 즉시 반응 ──
                conn.settimeout(self.RECV_TIMEOUT)

                try:
                    rf = conn.makefile("r", encoding="utf-8")
                    wf = conn.makefile("w", encoding="utf-8")

                    while not self._shutdown.is_set():
                        # export 플래그 확인 (RECV_TIMEOUT 마다 여기 도달)
                        if self._export_req:
                            resp = self._do_export()
                            try: wf.write(json.dumps(resp) + "\n"); wf.flush()
                            except Exception: pass
                            self._export_req = False

                        try:
                            line = rf.readline()
                        except socket.timeout:
                            continue     # 타임아웃 → 상단으로 (export 체크)
                        except OSError:
                            break

                        if not line:
                            break

                        msg = json.loads(line)
                        t   = msg.get("type")

                        if t == "step":
                            resp = self._handle_step(msg)
                        elif t == "export":
                            resp = self._do_export()
                        else:
                            resp = {"error": f"unknown: {t}"}

                        wf.write(json.dumps(resp) + "\n")
                        wf.flush()

                except Exception as e:
                    print(f"[Server] 연결 오류: {e}")
                finally:
                    conn.close()
                    print("[Server] 재연결 대기...\n")

        except KeyboardInterrupt:
            print("\n[Server] Ctrl+C → export 후 종료")

        finally:
            print("[Server] ONNX export 중...")
            self._do_export()
            srv.close()
            print("[Server] 종료 완료")


# ══════════════════════════════════════════════════════════════════
if __name__ == "__main__":
    PPOServer().run()