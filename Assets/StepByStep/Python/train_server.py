"""
DQN 학습 서버. 보상/종료(done)는 Unity에서 계산되어 관측에 실려온다.
Python은 그 값을 그대로 replay buffer에 사용한다 (자체 보상 재계산 없음).
- obs: unitId, distanceToTarget, dotToTarget, crossToTarget, h0~h5, reward, done
- action: unitId, direction(0~5)

PPO 대비 단순함: Replay Buffer + ε-greedy + 고정 주기 타겟 네트워크 갱신.
"""

import socket
import struct
import random
from collections import deque

import torch
import torch.nn as nn
import torch.nn.functional as F

from protocol import OBS_SIZE, recv_exact, parse_batch, build_response, validate_ranges, setup_logger

logger = setup_logger()

OBS_DIM = 9  # 신경망 입력은 여전히 9개 (distance, dot, cross, h0~h5). reward/done은 입력 아님.
ACTION_DIM = 6

GAMMA = 0.99
LR = 1e-3
BATCH_SIZE = 64
REPLAY_CAPACITY = 20000
MIN_REPLAY_BEFORE_TRAIN = 500
TARGET_UPDATE_EVERY = 500     # 이 학습 스텝마다 타겟 네트워크 동기화
TRAIN_EVERY_STEPS = 4         # 이 환경 스텝마다 한 번씩 학습

EPS_START = 1.0
EPS_END = 0.3   # 0.05 -> 0.1: 왕복 같은 국소 최적점에 갇혔을 때 스스로 벗어날 탐험 여지 확보
EPS_DECAY_STEPS = 5000        # 20000 -> 5000: 지금 속도(약 300스텝/1.5분)면 20000은 체감상 너무 느림


class QNet(nn.Module):
    def __init__(self, obs_dim=OBS_DIM, hidden=64, action_dim=ACTION_DIM):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(obs_dim, hidden),
            nn.ReLU(),
            nn.Linear(hidden, hidden),
            nn.ReLU(),
            nn.Linear(hidden, action_dim),
        )

    def forward(self, x):
        return self.net(x)  # (batch, action_dim) Q값


q_net = QNet()
target_net = QNet()
target_net.load_state_dict(q_net.state_dict())
target_net.eval()

optimizer = torch.optim.Adam(q_net.parameters(), lr=LR)

replay_buffer = deque(maxlen=REPLAY_CAPACITY)

# unitId -> 직전 스텝의 (obs_tensor, action) — 다음 스텝 관측 도착 시 transition 완성
pending_transition = {}

env_step_count = 0
train_step_count = 0


def obs_dict_to_tensor(unit):
    """신경망 입력용 관측 벡터. reward/done은 여기 포함되지 않는다 (그건 학습 신호용).
    Phase1Converters.ObsToInput(Unity, C#)와 필드 순서가 반드시 일치해야 한다."""
    vec = [unit["distanceToTarget"], unit["dotToTarget"], unit["crossToTarget"]]
    vec += [unit[f"h{i}"] for i in range(6)]
    return torch.tensor(vec, dtype=torch.float32)


def epsilon_by_step(step):
    if step >= EPS_DECAY_STEPS:
        return EPS_END
    frac = step / EPS_DECAY_STEPS
    return EPS_START + frac * (EPS_END - EPS_START)


def select_action(obs_tensor, epsilon):
    if random.random() < epsilon:
        return random.randint(0, ACTION_DIM - 1)
    with torch.no_grad():
        q_values = q_net(obs_tensor.unsqueeze(0))
        return int(torch.argmax(q_values, dim=-1).item())


def process_batch(units):
    """
    유닛별로:
      1) Unity가 계산해 보낸 reward/done을 그대로 사용
      2) 직전 transition이 있으면 (prev_obs, prev_action, reward, curr_obs, done)으로 replay_buffer에 push
      3) done이면 pending_transition 리셋 (Unity가 이미 리스폰까지 처리함)
      4) 이번 관측으로 새 액션 선택, pending_transition에 저장
    """
    global env_step_count
    responses = []
    epsilon = epsilon_by_step(env_step_count)

    for unit in units:
        validate_ranges(unit, logger)
        uid = unit["unitId"]
        curr_obs = obs_dict_to_tensor(unit)
        reward = unit["reward"]
        reward = max(-5.0, min(5.0, reward))  # 클리핑: 큰 보상(도달/이탈)이 TD 타겟을 과도하게 흔드는 것 완화
        done = bool(unit["done"])

        prev = pending_transition.get(uid)
        if prev is not None:
            prev_obs, prev_action = prev
            replay_buffer.append((prev_obs, prev_action, reward, curr_obs, done))

        if done:
            pending_transition.pop(uid, None)
            # 이 스텝은 Unity가 이미 리스폰 처리했으므로 반환하는 action은 적용되지 않는다(무시됨).
            action = random.randint(0, ACTION_DIM - 1)
        else:
            action = select_action(curr_obs, epsilon)
            pending_transition[uid] = (curr_obs, action)

        responses.append((uid, action))

    env_step_count += len(units)
    return responses, epsilon


def train_step():
    if len(replay_buffer) < MIN_REPLAY_BEFORE_TRAIN:
        return None

    batch = random.sample(replay_buffer, BATCH_SIZE)
    obs_b, action_b, reward_b, next_obs_b, done_b = zip(*batch)

    obs_b = torch.stack(obs_b)
    action_b = torch.tensor(action_b, dtype=torch.long)
    reward_b = torch.tensor(reward_b, dtype=torch.float32)
    next_obs_b = torch.stack(next_obs_b)
    done_b = torch.tensor(done_b, dtype=torch.float32)

    q_values = q_net(obs_b).gather(1, action_b.unsqueeze(1)).squeeze(1)

    with torch.no_grad():
        # Double DQN: 다음 액션 "선택"은 online network(q_net), "평가"는 target_net으로 분리.
        # 기존 vanilla DQN(target_net.max)은 Q값을 과대추정하는 경향이 있는데,
        # 이게 "제자리 왔다갔다" 같은 인접 액션 간 미세한 우열 다툼을 증폭시킬 수 있다.
        next_actions = q_net(next_obs_b).argmax(dim=1, keepdim=True)
        next_q_values = target_net(next_obs_b).gather(1, next_actions).squeeze(1)
        target = reward_b + GAMMA * next_q_values * (1.0 - done_b)

    loss = F.smooth_l1_loss(q_values, target)

    optimizer.zero_grad()
    loss.backward()
    torch.nn.utils.clip_grad_norm_(q_net.parameters(), 10.0)
    optimizer.step()

    return loss.item()


def save_checkpoint(path="phase1_dqn_checkpoint.pt"):
    torch.save(q_net.state_dict(), path)
    logger.info(f"체크포인트 저장: {path}")


def start_server(host="127.0.0.1", port=5555):
    global train_step_count

    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((host, port))
    server.listen(1)
    logger.info(f"DQN 학습 서버 대기 중... {host}:{port}")

    conn, addr = server.accept()
    logger.info(f"연결됨: {addr}")

    step_count = 0
    try:
        while True:
            header = recv_exact(conn, 4)
            if header is None:
                logger.info("연결 종료됨")
                break

            count = struct.unpack("<i", header)[0]
            data_bytes = recv_exact(conn, OBS_SIZE * count)
            if data_bytes is None:
                logger.info("데이터 수신 중 연결 끊김")
                break

            units = parse_batch(data_bytes, count)
            step_count += 1

            responses, epsilon = process_batch(units)
            conn.sendall(build_response(responses))

            if step_count % TRAIN_EVERY_STEPS == 0:
                loss = train_step()
                if loss is not None:
                    train_step_count += 1
                    if train_step_count % TARGET_UPDATE_EVERY == 0:
                        target_net.load_state_dict(q_net.state_dict())
                        logger.info(f"[Target Net] 동기화 완료 (train_step={train_step_count})")

                    if train_step_count % 100 == 0:
                        logger.info(
                            f"[Step {step_count}] train_step={train_step_count} "
                            f"loss={loss:.4f} eps={epsilon:.3f} replay={len(replay_buffer)}"
                        )
                        save_checkpoint()

    except KeyboardInterrupt:
        logger.info("서버 종료 - 종료 전 체크포인트 저장")
        save_checkpoint()
    finally:
        conn.close()
        server.close()


if __name__ == "__main__":
    start_server()