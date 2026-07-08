"""
Phase0: 회전만으로 목표 정면 맞추기. 관측이 dot/cross 2개뿐인 최소 문제.
이게 안정적으로 학습되면 "방향 계산+DQN 파이프라인" 자체는 검증된 것으로 보고
Phase1(이동 포함)으로 넘어가면 된다.

protocol: obs = unitId(int) + dot, cross, reward(float 3개) + done(int) = "<i3fi"
          action = unitId(int) + direction(int) = "<ii"
"""

import socket
import struct
import random
from collections import deque

import torch
import torch.nn as nn
import torch.nn.functional as F

OBS_FORMAT = "<i3fi"
OBS_SIZE = struct.calcsize(OBS_FORMAT)
ACTION_FORMAT = "<ii"

OBS_DIM = 2      # dot, cross
ACTION_DIM = 6

GAMMA = 0.9      # 에피소드가 짧으므로(회전만) 감가율도 낮게
LR = 1e-3
BATCH_SIZE = 32
REPLAY_CAPACITY = 5000
MIN_REPLAY_BEFORE_TRAIN = 200
TARGET_UPDATE_EVERY = 200
TRAIN_EVERY_STEPS = 2

EPS_START = 1.0
EPS_END = 0.1
EPS_DECAY_STEPS = 3000


class TinyQNet(nn.Module):
    def __init__(self, obs_dim=OBS_DIM, hidden=32, action_dim=ACTION_DIM):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(obs_dim, hidden),
            nn.LeakyReLU(0.01),
            nn.Linear(hidden, hidden),
            nn.LeakyReLU(0.01),
            nn.Linear(hidden, action_dim),
        )

    def forward(self, x):
        return self.net(x)


q_net = TinyQNet()
target_net = TinyQNet()
target_net.load_state_dict(q_net.state_dict())
target_net.eval()

optimizer = torch.optim.Adam(q_net.parameters(), lr=LR)
replay_buffer = deque(maxlen=REPLAY_CAPACITY)
pending_transition = {}

env_step_count = 0
train_step_count = 0


def recv_exact(conn, size):
    buf = b""
    while len(buf) < size:
        chunk = conn.recv(size - len(buf))
        if not chunk:
            return None
        buf += chunk
    return buf


def parse_batch(data_bytes, count):
    units = []
    for i in range(count):
        chunk = data_bytes[i * OBS_SIZE:(i + 1) * OBS_SIZE]
        unit_id, dx, dy, reward, done = struct.unpack(OBS_FORMAT, chunk)
        units.append({"unitId": unit_id, "dx": dx, "dy": dy, "reward": reward, "done": done})
    return units


def build_response(actions):
    header = struct.pack("<i", len(actions))
    body = b"".join(struct.pack(ACTION_FORMAT, uid, direction) for uid, direction in actions)
    return header + body


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
    global env_step_count
    responses = []
    epsilon = epsilon_by_step(env_step_count)

    for unit in units:
        uid = unit["unitId"]
        curr_obs = torch.tensor([unit["dx"], unit["dy"]], dtype=torch.float32)
        reward = max(-5.0, min(5.0, unit["reward"]))
        done = bool(unit["done"])

        prev = pending_transition.get(uid)
        if prev is not None:
            prev_obs, prev_action = prev
            replay_buffer.append((prev_obs, prev_action, reward, curr_obs, done))

        if done:
            pending_transition.pop(uid, None)
            action = random.randint(0, ACTION_DIM - 1)
        else:
            action = select_action(curr_obs, epsilon)
            pending_transition[uid] = (curr_obs, action)

        responses.append((uid, action))

        if uid == units[0]["unitId"]:
            print(f"[DEBUG] dx={unit['dx']:.3f} dy={unit['dy']:.3f} action={action} done={done}")

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
        next_actions = q_net(next_obs_b).argmax(dim=1, keepdim=True)
        next_q_values = target_net(next_obs_b).gather(1, next_actions).squeeze(1)
        target = reward_b + GAMMA * next_q_values * (1.0 - done_b)

    loss = F.smooth_l1_loss(q_values, target)
    optimizer.zero_grad()
    loss.backward()
    torch.nn.utils.clip_grad_norm_(q_net.parameters(), 5.0)
    optimizer.step()

    return loss.item()


def start_server(host="127.0.0.1", port=5555):
    global train_step_count

    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((host, port))
    server.listen(1)
    print(f"PureMove 학습 서버 대기 중... {host}:{port}")

    conn, addr = server.accept()
    print(f"연결됨: {addr}")

    step_count = 0
    try:
        while True:
            header = recv_exact(conn, 4)
            if header is None:
                print("연결 종료됨")
                break

            count = struct.unpack("<i", header)[0]
            data_bytes = recv_exact(conn, OBS_SIZE * count)
            if data_bytes is None:
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
                    if train_step_count % 50 == 0:
                        print(f"[Step {step_count}] train_step={train_step_count} "
                              f"loss={loss:.4f} eps={epsilon:.3f} replay={len(replay_buffer)}")
                        torch.save(q_net.state_dict(), 'checkpoint_model.pth')
                        print(f"[{train_step_count}회 학습] 모델이 성공적으로 저장되었습니다.")

    except KeyboardInterrupt:
        print("서버 종료")
    finally:
        conn.close()
        server.close()


if __name__ == "__main__":
    start_server()