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
    """유니티의 재연결(씬 재로드 등)을 지원하는 강화된 소켓 브릿지"""

    def __init__(self, host="127.0.0.1", port=5555):
        self.host = host
        self.port = port
        self.obs_queue = queue.Queue(maxsize=1)
        self.action_queue = queue.Queue(maxsize=1)
        self.unit_id = None
        self._server_socket = None
        self._conn = None
        self._running = True
        self._thread = threading.Thread(target=self._run_server, daemon=True)
        self._thread.start()

    def close(self):
        self._running = False
        try:
            if self._conn:
                self._conn.close()
        except Exception:
            pass
        try:
            if self._server_socket:
                self._server_socket.close()
        except Exception:
            pass

    def _run_server(self):
        server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server.bind((self.host, self.port))
        server.listen(1)
        self._server_socket = server
        print(f"서버 오픈 완료. 대기 중... {self.host}:{self.port}")

        while self._running:
            try:
                # 유니티가 새로 연결할 때마다 accept
                conn, addr = server.accept()
                self._conn = conn
                print(f"유니티 연결됨: {addr}")

                # 새 연결이 들어오면 큐에 남아있던 이전 에피소드 데이터 청소
                while not self.obs_queue.empty():
                    try: self.obs_queue.get_nowait()
                    except queue.Empty: break
                while not self.action_queue.empty():
                    try: self.action_queue.get_nowait()
                    except queue.Empty: break

            except Exception:
                if not self._running:
                    break
                continue

            try:
                while self._running:
                    header = recv_exact(conn, 4)
                    if header is None:
                        print("유니티 연결 끊김 (다음 재연결 대기)")
                        break
                    
                    count = struct.unpack("<i", header)[0]
                    data_bytes = recv_exact(conn, OBS_SIZE * count)
                    if data_bytes is None:
                        print("데이터 수신 실패 (연결 끊김)")
                        break

                    unit_id, dx, dy, reward, done = struct.unpack(OBS_FORMAT, data_bytes[:OBS_SIZE])
                    self.unit_id = unit_id

                    # 관측값 전달
                    self.obs_queue.put({"dx": dx, "dy": dy, "reward": reward, "done": bool(done)})

                    # 유니티가 종료 신호를 보냈다면 액션 대기 없이 즉시 더미 응답 후 씬 리셋 대기
                    if done:
                        try:
                            header_out = struct.pack("<i", 1)
                            body_out = struct.pack("<iff", unit_id, 0.0, 0.0)
                            conn.sendall(header_out + body_out)
                        except Exception:
                            pass
                        continue

                    # 일반 스텝인 경우 파이썬의 액션 결정을 대기
                    try:
                        action_x, action_y = self.action_queue.get(timeout=5.0)
                    except queue.Empty:
                        print("파이썬 학습 루프에서 액션이 오지 않아 소켓 스레드 대기 마감")
                        break

                    header_out = struct.pack("<i", 1)
                    body_out = struct.pack("<iff", unit_id, action_x, action_y)
                    conn.sendall(header_out + body_out)
            except Exception as e:
                print(f"통신 중 에러 발생: {e}")
            finally:
                conn.close()

        server.close()

    def wait_obs(self, timeout=10.0):
        try:
            return self.obs_queue.get(timeout=timeout)
        except queue.Empty:
            raise TimeoutError("Unity로부터 지정된 시간 내에 관측 데이터가 도착하지 않았어.")

    def send_action(self, action_x, action_y):
        try:
            self.action_queue.put((action_x, action_y), timeout=2.0)
        except queue.Full:
            pass


class UnityHexEnv(gym.Env):
    def __init__(self, bridge: UnitySocketBridge, max_steps=100):
        super().__init__()
        self.bridge = bridge
        self.observation_space = spaces.Box(low=-2.0, high=2.0, shape=(2,), dtype=np.float32)
        self.action_space = spaces.Box(low=-1.0, high=1.0, shape=(2,), dtype=np.float32)
        self._last_obs = None
        self.max_steps = max_steps
        self._step_count = 0
        self._needs_truncated_unblock = False

    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        self._step_count = 0
        
        if self._needs_truncated_unblock:
            self.bridge.send_action(0.0, 0.0)
            self._needs_truncated_unblock = False
            
        data = self.bridge.wait_obs()
        self._last_obs = np.array([data["dx"], data["dy"]], dtype=np.float32)
        return self._last_obs, {}

    def step(self, action):
        self.bridge.send_action(float(action[0]), float(action[1]))

        data = self.bridge.wait_obs()
        obs = np.array([data["dx"], data["dy"]], dtype=np.float32)
        reward = data["reward"]
        terminated = data["done"]

        self._step_count += 1
        truncated = self._step_count >= self.max_steps
        
        if truncated and not terminated:
            reward -= 1.0
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
            print(f"[체크포인트] {self.num_timesteps} 스텝 저장 완료")
        return True


if __name__ == "__main__":
    bridge = UnitySocketBridge(host="127.0.0.1", port=5555)
    env = UnityHexEnv(bridge)

    model = PPO(
        "MlpPolicy", env, verbose=1,
        n_steps=128,       # 디버깅을 위해 롤아웃 단위를 더 줄여서 동기화 확인
        batch_size=32,
        learning_rate=3e-4,
    )

    callback = PeriodicSaveCallback(save_every=1000, save_path="phase2_ppo_model")

    try:
        model.learn(total_timesteps=50000, callback=callback)
    except KeyboardInterrupt:
        print("\n사용자 요청으로 학습 중단")
    finally:
        bridge.close()
        model.save("phase2_ppo_model")
        print("최종 모델 저장 완료")