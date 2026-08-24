import socket
import struct
import threading
import queue
import numpy as np
from gymnasium import spaces
from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import BaseCallback
from stable_baselines3.common.vec_env import VecEnv
import torch


class RLConfig:
    OBS_FORMAT = "<i5fiiffi"  # unit_id, dx, dy, delta selfHp, targetHp, alive, InAttackRange, distToEdge, reward, done
    OBS_SIZE = struct.calcsize(OBS_FORMAT)

    ACTION_FORMAT = "<ii"  # unit_id, action_index
    ACTION_SIZE = struct.calcsize(ACTION_FORMAT)

    OBS_SHAPE = (7,)   # dx, dy, dalta, selfHp, targetHp, in_attack_range, distToEdge
    NUM_ACTIONS = 4    # Move, Stop, Attack, Action

    OBS_LOW, OBS_HIGH = -1.0, 1.0

    @staticmethod
    def unpack_observation(data_bytes, offset):
        raw = struct.unpack(RLConfig.OBS_FORMAT, data_bytes[offset:offset + RLConfig.OBS_SIZE])
        unit_id, dx, dy, delta, self_hp, target_hp, alive, in_attack_range, distToEdge, reward, done = raw
        return {
            "unit_id": unit_id,
            "obs_vector": [dx, dy, delta, self_hp, target_hp, in_attack_range, distToEdge],  # 관측 벡터에도 추가해야 함
            "alive": bool(alive),
            "reward": reward,
            "done": bool(done)
        }

    @staticmethod
    def pack_action(unit_id, action_index):
        return struct.pack(RLConfig.ACTION_FORMAT, unit_id, int(action_index))


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

    def _run_server(self):
        server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server.bind((self.host, self.port))
        server.listen(1)
        self._server_socket = server
        print(f"[Bridge] 서버 오픈. 대기 중... {self.host}:{self.port}")

        while self._running:
            try:
                conn, addr = server.accept()
                self._conn = conn
                print(f"[Bridge] 연결됨: {addr}")
                self._clear_queues()
            except Exception:
                if not self._running: break
                continue

            try:
                while self._running:
                    header = recv_exact(conn, 4)
                    if header is None: break
                    count = struct.unpack("<i", header)[0]

                    data_bytes = recv_exact(conn, RLConfig.OBS_SIZE * count)
                    if data_bytes is None: break

                    batch_obs = []
                    for i in range(count):
                        offset = i * RLConfig.OBS_SIZE
                        batch_obs.append(RLConfig.unpack_observation(data_bytes, offset))

                    self.obs_queue.put(batch_obs)

                    try:
                        actions = self.action_queue.get(timeout=60.0)
                    except queue.Empty:
                        break

                    header_out = struct.pack("<i", count)
                    body_out = b""
                    for i in range(count):
                        u_id = batch_obs[i]["unit_id"]
                        body_out += RLConfig.pack_action(u_id, actions[i])

                    conn.sendall(header_out + body_out)
            except Exception as e:
                print(f"[Bridge] 통신 에러: {e}")
            finally:
                conn.close()
        server.close()

    def _clear_queues(self):
        while not self.obs_queue.empty():
            try: self.obs_queue.get_nowait()
            except queue.Empty: break
        while not self.action_queue.empty():
            try: self.action_queue.get_nowait()
            except queue.Empty: break

    def wait_obs(self, timeout=10.0):
        try: return self.obs_queue.get(timeout=timeout)
        except queue.Empty: raise TimeoutError("관측 데이터 수신 타임아웃")

    def send_action(self, actions):
        try: self.action_queue.put(actions, timeout=2.0)
        except queue.Full: pass

    def close(self):
        self._running = False
        try:
            if self._conn: self._conn.close()
        except Exception: pass
        try:
            if self._server_socket: self._server_socket.close()
        except Exception: pass


class UnityDiscreteVecEnv(VecEnv):
    """
    슬롯 수(num_envs)는 항상 고정(MaxUnitAmount)이고,
    alive=False인 슬롯은 reward 0, action 0(None)으로 마스킹 처리한다.
    """
    def __init__(self, bridge: UnitySocketBridge):
        self.bridge = bridge

        print("[Env] 최초 패킷 대기 중...")
        self._first_batch = self.bridge.wait_obs(timeout=60.0)
        num_envs = len(self._first_batch)
        print(f"[Env] 슬롯 수(고정): {num_envs}")

        observation_space = spaces.Box(
            low=RLConfig.OBS_LOW, high=RLConfig.OBS_HIGH,
            shape=RLConfig.OBS_SHAPE, dtype=np.float32
        )
        action_space = spaces.Discrete(RLConfig.NUM_ACTIONS)

        super().__init__(num_envs, observation_space, action_space)
        self.actions = None

    def reset(self):
        if self._first_batch is not None:
            batch_data = self._first_batch
            self._first_batch = None
        else:
            batch_data = self.bridge.wait_obs()
        return np.array([d["obs_vector"] for d in batch_data], dtype=np.float32)

    def step_async(self, actions):
        self.actions = actions

    def step_wait(self):
        self.bridge.send_action(self.actions)
        batch_data = self.bridge.wait_obs()

        if len(batch_data) != self.num_envs:
            raise ValueError(
                f"[에러] 슬롯 수 불일치. 기대: {self.num_envs}, 실제: {len(batch_data)}. "
                f"Unity가 항상 MaxAmount만큼 슬롯을 패딩해서 보내야 함."
            )

        alive_mask = np.array([d["alive"] for d in batch_data], dtype=bool)
        obs = np.array([d["obs_vector"] for d in batch_data], dtype=np.float32)
        rewards = np.array([d["reward"] for d in batch_data], dtype=np.float32)
        dones = np.array([d["done"] for d in batch_data], dtype=bool)

        # 죽은 슬롯은 보상 0으로 마스킹 (학습에 영향 안 주게)
        rewards[~alive_mask] = 0.0

        infos = [{} for _ in range(self.num_envs)]

            # 죽은 슬롯만 terminal_observation 기록 (SB3 관례)
        for i in range(self.num_envs):
            if dones[i]:
                infos[i]["terminal_observation"] = obs[i].copy()

        return obs, rewards, dones, infos

    def close(self):
        self.bridge.close()

    def get_attr(self, attr_name, indices=None): return [None] * self.num_envs
    def set_attr(self, attr_name, value, indices=None): pass
    def env_method(self, method_name, *method_args, **method_kwargs): return [None] * self.num_envs
    def env_is_wrapped(self, wrapper_class, indices=None): return [False] * self.num_envs


class AsyncSaveCallback(BaseCallback):
    def __init__(self, save_freq, save_path, verbose=1):
        super().__init__(verbose)
        self.save_freq = save_freq
        self.save_path = save_path

    def _on_step(self) -> bool:
        if self.n_calls % self.save_freq == 0:
            path = f"{self.save_path}_{self.num_timesteps}_steps.pth"
            self.save_model_threaded(self.model, path)
        return True

    def save_model_threaded(self, model, path):
        state_dict_ref = model.policy.state_dict()

        def _save_task():
            try:
                policy_state = {k: v.detach().cpu().clone() for k, v in state_dict_ref.items()}
                torch.save(policy_state, path)
                print(f"\n[비동기 저장 성공] {path}")
            except Exception as e:
                print(f"\n[저장 실패] {e}")

        threading.Thread(target=_save_task, daemon=True).start()


if __name__ == "__main__":
    bridge = UnitySocketBridge(host="127.0.0.1", port=5555)
    env = UnityDiscreteVecEnv(bridge)

    model = PPO(
        "MlpPolicy",
        env,
        device="cpu",
        verbose=1,
        n_steps=128,
        batch_size=32,
        learning_rate=3e-4,
        ent_coef=0.01,  # 확률 분포가 너무 빨리 한쪽으로 쏠리지 않게
    )

    callback = AsyncSaveCallback(save_freq=10000, save_path="checkpoints/action_select_model")

    try:
        print("[Train] PPO 학습 시작")
        model.learn(total_timesteps=5000000, callback=callback)
    except KeyboardInterrupt:
        print("\n[Train] 사용자 중단")
    finally:
        model.save("action_select_final_model.zip")
        bridge.close()
        print("[Train] 최종 저장 완료")