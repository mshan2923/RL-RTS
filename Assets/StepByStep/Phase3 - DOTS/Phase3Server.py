import socket
import struct
import threading
import queue

import numpy as np
import gymnasium as gym
from gymnasium import spaces
from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import BaseCallback

OBS_FORMAT = "<i3fi"
OBS_SIZE = struct.calcsize(OBS_FORMAT)


def recv_exact(conn, size):
    buf = b""
    while len(buf) < size:
        chunk = conn.recv(size - len(buf))
        if not chunk:
            return None
        buf += chunk
    return buf


class UnitySocketBridge:
    def __init__(self, host="127.0.0.1", port=5555):
        self.host = host
        self.port = port
        self.obs_queue = queue.Queue(maxsize=1)
        self.action_queue = queue.Queue(maxsize=1)
        
        self._server_socket = None
        self._conn = None
        self._running = True
        self._thread = threading.Thread(target=self._run_server, daemon=True)
        self._thread.start()

    def close(self):
        self._running = False
        try:
            if self._conn: self._conn.close()
        except Exception: pass
        try:
            if self._server_socket: self._server_socket.close()
        except Exception: pass

    def _run_server(self):
        server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server.bind((self.host, self.port))
        server.listen(1)
        self._server_socket = server
        print(f"서버 오픈 완료. 유니티 연결 대기 중... {self.host}:{self.port}")

        while self._running:
            try:
                conn, addr = server.accept()
                self._conn = conn
                print(f"유니티 연결됨: {addr}")

                while not self.obs_queue.empty():
                    try: self.obs_queue.get_nowait()
                    except queue.Empty: break
                while not self.action_queue.empty():
                    try: self.action_queue.get_nowait()
                    except queue.Empty: break

            except Exception:
                if not self._running: break
                continue

            try:
                while self._running:
                    header = recv_exact(conn, 4)
                    if header is None: break
                    
                    count = struct.unpack("<i", header)[0]
                    data_bytes = recv_exact(conn, OBS_SIZE * count)
                    if data_bytes is None: break

                    batch_obs = []
                    for i in range(count):
                        offset = i * OBS_SIZE
                        unit_id, dx, dy, reward, done = struct.unpack(OBS_FORMAT, data_bytes[offset:offset+OBS_SIZE])
                        batch_obs.append({"unit_id": unit_id, "dx": dx, "dy": dy, "reward": reward, "done": bool(done)})

                    self.obs_queue.put(batch_obs)

                    any_done = any(o["done"] for o in batch_obs)
                    if any_done:
                        try:
                            header_out = struct.pack("<i", count)
                            body_out = b""
                            for o in batch_obs:
                                body_out += struct.pack("<iff", o["unit_id"], 0.0, 0.0)
                            conn.sendall(header_out + body_out)
                        except Exception: pass
                        continue

                    try:
                        actions = self.action_queue.get(timeout=5.0)
                    except queue.Empty: break

                    header_out = struct.pack("<i", count)
                    body_out = b""
                    for i in range(count):
                        u_id = batch_obs[i]["unit_id"]
                        ax, ay = actions[i]
                        body_out += struct.pack("<iff", u_id, float(ax), float(ay))
                        
                    conn.sendall(header_out + body_out)
            except Exception as e:
                print(f"통신 에러: {e}")
            finally:
                conn.close()

        server.close()

    def wait_obs(self, timeout=10.0):
        try: return self.obs_queue.get(timeout=timeout)
        except queue.Empty: raise TimeoutError("관측 데이터 대기 시간 초과")

    def send_action(self, actions):
        try: self.action_queue.put(actions, timeout=2.0)
        except queue.Full: pass


class UnityHexEnv(gym.Env):
    def __init__(self, bridge: UnitySocketBridge, max_steps=100):
        super().__init__()
        self.bridge = bridge
        self.max_steps = max_steps
        self._step_count = 0
        self._needs_truncated_unblock = False
        
        # 1. 최초 1회 유니티 패킷을 기다려서 에이전트 수 동적 분석
        print("에이전트 수 자치 분석을 위해 최초 데이터 대기 중...")
        self._first_batch = self.bridge.wait_obs(timeout=60.0)
        self.num_agents = len(self._first_batch)
        print(f"분석 완료 - 감지된 에이전트 수: {self.num_agents}개")
        
        # 2. 분석된 크기로 스페이스 정의
        self.observation_space = spaces.Box(low=-2.0, high=2.0, shape=(self.num_agents, 2), dtype=np.float32)
        self.action_space = spaces.Box(low=-1.0, high=1.0, shape=(self.num_agents, 2), dtype=np.float32)
        self._last_obs = None

    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        self._step_count = 0
        
        if self._needs_truncated_unblock:
            self.bridge.send_action(np.zeros((self.num_agents, 2), dtype=np.float32))
            self._needs_truncated_unblock = False
            
        # 최초 리셋 시에는 생성자에서 감지용으로 미리 받아둔 패킷을 소모함
        if self._first_batch is not None:
            batch_data = self._first_batch
            self._first_batch = None
        else:
            batch_data = self.bridge.wait_obs()
            
        obs_list = [[d["dx"], d["dy"]] for d in batch_data]
        self._last_obs = np.array(obs_list, dtype=np.float32)
        return self._last_obs, {}

    def step(self, action):
        self.bridge.send_action(action)

        batch_data = self.bridge.wait_obs()
        obs_list = [[d["dx"], d["dy"]] for d in batch_data]
        obs = np.array(obs_list, dtype=np.float32)
        
        reward = sum(d["reward"] for d in batch_data)
        terminated = any(d["done"] for d in batch_data)

        self._step_count += 1
        truncated = self._step_count >= self.max_steps
        
        if truncated and not terminated:
            reward -= 1.0 * self.num_agents
            self._needs_truncated_unblock = True

        self._last_obs = obs
        return obs, reward, terminated, truncated, {}


class PeriodicSaveCallback(BaseCallback):
    def __init__(self, save_every=2000, save_path="phase2_ppo_model"):
        super().__init__()
        self.save_every = save_every
        self.save_path = save_path

    def _on_step(self) -> bool:
        if self.num_timesteps % self.save_every == 0:
            self.model.save(self.save_path)
            print(f"[체크포인트] {self.num_timesteps} 스텝 저장")
        return True


if __name__ == "__main__":
    bridge = UnitySocketBridge(host="127.0.0.1", port=5555)
    
    # 생성자 내부에서 최초 연결 및 개수 인식이 자동으로 이루어짐
    env = UnityHexEnv(bridge)  

    model = PPO(
        "MlpPolicy", env, verbose=1,
        n_steps=128,       
        batch_size=32,
        learning_rate=3e-4,
    )

    callback = PeriodicSaveCallback(save_every=1000, save_path="phase3_ppo_model")

    try:
        model.learn(total_timesteps=50000, callback=callback)
    except KeyboardInterrupt:
        print("\n학습 중단")
    finally:
        bridge.close()
        model.save("phase2_ppo_model")
        print("최종 모델 저장 완료")