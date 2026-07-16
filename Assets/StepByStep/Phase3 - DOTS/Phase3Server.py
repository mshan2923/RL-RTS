import socket
import struct
import threading
import queue
import numpy as np
from gymnasium import spaces
from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import BaseCallback
from stable_baselines3.common.vec_env import VecEnv
from stable_baselines3.common.callbacks import BaseCallback
import threading
import torch

# =========================================================================
# [1. 데이터 프로토콜 설정 구역] 
# 나중에 데이터 구조를 바꿀 때 여기만 수정하면 전체 코드가 알아서 맞춰져!
# =========================================================================
class RLConfig:
    # 유니티 C# 구조체와 매핑될 바이트 포맷 (<: 리틀엔디안, i: int, f: float)
    # 현재 구조: unit_id(int), dx(float), dy(float), d0 ~ d5(float), reward(float), done(int)
    OBS_FORMAT = "<i9fi"
    OBS_SIZE = struct.calcsize(OBS_FORMAT)
    
    # 유니티로 보낼 액션 바이트 포맷
    # 현재 구조: unit_id(int), ax(float), ay(float)
    ACTION_FORMAT = "<iff"
    ACTION_SIZE = struct.calcsize(ACTION_FORMAT)
    
    # Sentis 가변 배치와 연동할 '에이전트 1개 기준' 차원 정의
    OBS_SHAPE = (8,)      # [dx, dy] 이므로 2차원
    ACTION_SHAPE = (2,)   # [ax, ay] 이므로 2차원
    
    # 데이터 스페이스 범위 설정
    OBS_LOW, OBS_HIGH = -2.0, 2.0
    ACT_LOW, ACT_HIGH = -1.0, 1.0

    @staticmethod
    def unpack_observation(data_bytes, offset):
        """유니티에서 받은 raw 바이트를 해석해서 데이터 딕셔너리로 쪼개줘"""
        raw_data = struct.unpack(RLConfig.OBS_FORMAT, data_bytes[offset : offset + RLConfig.OBS_SIZE])
        
        # OBS_SHAPE 크기에 맞춰서 동적으로 데이터를 슬라이싱해 에러를 원천 차단해
        num_obs = RLConfig.OBS_SHAPE[0]  # 현재 설정인 7을 가져옴
        
        unit_id = raw_data[0]
        obs_vector = list(raw_data[1 : 1 + num_obs])  # 관측치 개수만큼 유연하게 가져오기
        reward = raw_data[1 + num_obs]                # 관측치 바로 다음 인덱스는 reward
        done = raw_data[2 + num_obs]                  # 맨 마지막 인덱스는 done
        
        return {
            "unit_id": unit_id,
            "obs_vector": obs_vector,  # 신경망 입력으로 들어갈 핵심 벡터
            "reward": reward,
            "done": bool(done)
        }

    @staticmethod
    def pack_action(unit_id, action_vector):
        """모델이 결정한 액션을 유니티가 읽을 수 있는 바이트로 패킹해줘"""
        ax, ay = action_vector
        return struct.pack(RLConfig.ACTION_FORMAT, unit_id, float(ax), float(ay))


# =========================================================================
# [2. 네트워크 통신 브릿지]
# 유니티와의 소켓 연결 및 스레드 기반 큐(Queue) 관리를 담당해
# =========================================================================
def recv_exact(conn, size):
    """지정한 바이트 크기만큼 데이터가 다 올 때까지 안전하게 수신해줘"""
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
        
        # 백그라운드에서 유니티 통신을 무한 루프로 처리할 백그라운드 스레드 가동
        self._thread = threading.Thread(target=self._run_server, daemon=True)
        self._thread.start()

    def _run_server(self):
        server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server.bind((self.host, self.port))
        server.listen(1)
        self._server_socket = server
        print(f"[Bridge] 서버 오픈 완료. 유니티 연결 대기 중... {self.host}:{self.port}")

        while self._running:
            try:
                conn, addr = server.accept()
                self._conn = conn
                print(f"[Bridge] 유니티 클라이언트 연결됨: {addr}")
                
                # 새 연결이 들어오면 잔여 큐 데이터 청소
                self._clear_queues()
            except Exception:
                if not self._running: break
                continue

            try:
                while self._running:
                    # 1. 헤더 수신 (현재 프레임에 전송된 에이전트 총 개수 int)
                    header = recv_exact(conn, 4)
                    if header is None: break
                    count = struct.unpack("<i", header)[0]
                    
                    # 2. 바디 수신 (에이전트 개수 * 구조체 바이트 크기)
                    data_bytes = recv_exact(conn, RLConfig.OBS_SIZE * count)
                    if data_bytes is None: break

                    # 3. 바이트 데이터를 루프 돌며 데이터 딕셔너리 리스트로 변환
                    batch_obs = []
                    for i in range(count):
                        offset = i * RLConfig.OBS_SIZE
                        parsed_node = RLConfig.unpack_observation(data_bytes, offset)
                        batch_obs.append(parsed_node)

                    # 환경 스레드로 데이터 토스
                    self.obs_queue.put(batch_obs)

                    # 환경 리셋 신호(done)가 하나라도 잡히면 더미 액션을 즉시 반환해서 락을 방지해
                    if any(o["done"] for o in batch_obs):
                        self._send_dummy_action(conn, batch_obs, count)
                        continue

                    # 4. 환경 스레드가 계산한 액션 데이터 가져오기 (5초 타임아웃)
                    try:
                        actions = self.action_queue.get(timeout=60.0)
                    except queue.Empty:
                        break

                    # 5. 유니티로 액션 바이트 전송
                    header_out = struct.pack("<i", count)
                    body_out = b""
                    for i in range(count):
                        u_id = batch_obs[i]["unit_id"]
                        body_out += RLConfig.pack_action(u_id, actions[i])
                        
                    conn.sendall(header_out + body_out)
            except Exception as e:
                print(f"[Bridge] 통신 에러 발생: {e}")
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

    def _send_dummy_action(self, conn, batch_obs, count):
        try:
            header_out = struct.pack("<i", count)
            body_out = b""
            for o in batch_obs:
                body_out += RLConfig.pack_action(o["unit_id"], np.zeros(RLConfig.ACTION_SHAPE))
            conn.sendall(header_out + body_out)
        except Exception: pass

    def wait_obs(self, timeout=10.0):
        try: return self.obs_queue.get(timeout=timeout)
        except queue.Empty: raise TimeoutError("유니티로부터 관측 데이터 수신 대기 시간 초과")

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


# =========================================================================
# [3. 다중 에이전트 병렬 학습 환경 (SB3 VecEnv)]
# 여러 유니티 에이전트 데이터를 파이썬 병렬 환경(Vectorized Environment) 차원으로 다뤄
# =========================================================================
class UnityHexVecEnv(VecEnv):
    def __init__(self, bridge: UnitySocketBridge):
        self.bridge = bridge
        
        print("[Env] 에이전트 개수 자동 분석을 위해 최초 패킷 대기 중...")
        self._first_batch = self.bridge.wait_obs(timeout=60.0)
        num_envs = len(self._first_batch)
        print(f"[Env] 분석 완료 - 감지된 에이전트(병렬 환경 수): {num_envs}개")
        
        # RLConfig에 설정된 차원과 범위 정보를 가져와 스페이스 구축
        observation_space = spaces.Box(low=RLConfig.OBS_LOW, high=RLConfig.OBS_HIGH, shape=RLConfig.OBS_SHAPE, dtype=np.float32)
        action_space = spaces.Box(low=RLConfig.ACT_LOW, high=RLConfig.ACT_HIGH, shape=RLConfig.ACTION_SHAPE, dtype=np.float32)
        
        super().__init__(num_envs, observation_space, action_space)
        self.actions = None

    def reset(self):
        if self._first_batch is not None:
            batch_data = self._first_batch
            self._first_batch = None
        else:
            batch_data = self.bridge.wait_obs()
            
        obs_list = [d["obs_vector"] for d in batch_data]
        return np.array(obs_list, dtype=np.float32)

    def step_async(self, actions):
        self.actions = actions

    def step_wait(self):
            self.bridge.send_action(self.actions)

            # 액션이 적용된 결과 데이터 패킷 수신
            batch_data = self.bridge.wait_obs()
            
            # [안전장치 추가] 유니티가 보낸 데이터 개수와 기대하는 에이전트 개수(12)가 맞는지 검증해
            if len(batch_data) != self.num_envs:
                raise ValueError(
                    f"\n[에러] 유니티에서 보낸 에이전트 개수 불일치!\n"
                    f"기대치(num_envs): {self.num_envs}개, 실제 받은 개수: {len(batch_data)}개.\n"
                    f"유니티 씬 리셋이나 스폰 타이밍에 에이전트가 순간적으로 사라졌는지 확인해봐!"
                )
            
            obs = np.array([d["obs_vector"] for d in batch_data], dtype=np.float32)
            rewards = np.array([d["reward"] for d in batch_data], dtype=np.float32)
            dones = np.array([d["done"] for d in batch_data], dtype=bool)
            
            # 에이전트 동기화 리셋 판정
            if np.any(dones):
                dones[:] = True
                
                # 리셋 직후의 첫 데이터 수신
                next_batch_data = self.bridge.wait_obs()
                
                # [리셋 시점에도 안전장치 검증]
                if len(next_batch_data) != self.num_envs:
                    raise ValueError(
                        f"\n[리셋 에러] 리셋 후 받은 에이전트 개수 불일치!\n"
                        f"기대치: {self.num_envs}개, 실제 받은 개수: {len(next_batch_data)}개."
                    )
                    
                next_obs = np.array([d["obs_vector"] for d in next_batch_data], dtype=np.float32)
                
                infos = [{"terminal_observation": obs[i]} for i in range(self.num_envs)]
                return next_obs, rewards, dones, infos
            
            infos = [{} for _ in range(self.num_envs)]
            return obs, rewards, dones, infos

    def close(self):
        self.bridge.close()

    def get_attr(self, attr_name, indices=None): return [None] * self.num_envs
    def set_attr(self, attr_name, value, indices=None): pass
    def env_method(self, method_name, *method_args, **method_kwargs): return [None] * self.num_envs
    def env_is_wrapped(self, wrapper_class, indices=None): return [False] * self.num_envs


# =========================================================================
# [4. 메인 실행 루프]
# =========================================================================
class AsyncSaveCallback(BaseCallback):
    def __init__(self, save_freq, save_path, verbose=1):
        super().__init__(verbose)
        self.save_freq = save_freq
        self.save_path = save_path

    def _on_step(self) -> bool:
        # 설정한 스텝 주기마다 저장
        if self.n_calls % self.save_freq == 0:
            save_path = f"{self.save_path}_{self.num_timesteps}_steps.pth"
            self.save_model_threaded(self.model, save_path)
        return True

    def save_model_threaded(self, model, path):
        # state_dict()를 얕게 참조만 하고, 실제 .cpu() 카피(=blocking 지점)는
        # 전부 스레드 안에서 처리해서 메인 스레드(=유니티 통신 루프)를 절대 안 막음
        state_dict_ref = model.policy.state_dict()

        def _save_task():
            try:
                policy_state = {k: v.detach().cpu().clone() for k, v in state_dict_ref.items()}
                torch.save(policy_state, path)
                print(f"\n[비동기 저장 성공] {path}")
            except Exception as e:
                print(f"\n[저장 실패] {e}")

        save_thread = threading.Thread(target=_save_task)
        save_thread.daemon = True
        save_thread.start()

if __name__ == "__main__":
    bridge = UnitySocketBridge(host="127.0.0.1", port=5555)
    env = UnityHexVecEnv(bridge)  

    # PPO 하이퍼파라미터 정의
    model = PPO(
        "MlpPolicy", 
        env, 
        device="cuda",
        verbose=1,
        n_steps=128,       
        batch_size=32,
        learning_rate=3e-4,
    )

    callback = AsyncSaveCallback(save_freq=10000, save_path="checkpoints/phase3_model")

    try:
        print("[Train] PPO 강화학습 루프를 시작해.")
        model.learn(total_timesteps=5000000, callback=callback)
    except KeyboardInterrupt:
        print("\n[Train] 사용자에 의해 학습이 중단되었어.")
    finally:
        # 종료 직전에 최종 모델 저장 (마지막은 동기식으로 확실하게)
        model.save("phase3_final_model.zip")
        bridge.close()
        print("[Train] 최종 모델 저장 완료.")