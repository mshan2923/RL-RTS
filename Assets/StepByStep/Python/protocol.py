"""
공통 통신 프로토콜 + 로깅. train_server.py, inference용 export 스크립트가 공유.
"""

import struct
import logging
import sys

# unitId(int) + distanceToTarget, dotToTarget, crossToTarget, h0~h5, reward (float 10개) + done(int)
# reward/done은 Unity에서 계산되어 실려온다 (Python은 정규화된 값만 보므로 경계이탈 등을
# 정확히 판정할 수 없어, 실제 상태를 아는 Unity 쪽에서 계산하도록 구조 변경됨)
OBS_FORMAT = "<i10fi"
OBS_SIZE = struct.calcsize(OBS_FORMAT)
OBS_FIELDS = ["unitId", "distanceToTarget", "dotToTarget", "crossToTarget",
              "h0", "h1", "h2", "h3", "h4", "h5", "reward", "done"]

ACTION_FORMAT = "<ii"  # unitId + direction (done은 Unity가 이미 알고 있어 왕복 불필요)


def setup_logger(name="rl_step_by_step", level=logging.INFO):
    logger = logging.getLogger(name)
    logger.setLevel(level)
    if not logger.handlers:
        handler = logging.StreamHandler(sys.stdout)
        handler.setFormatter(logging.Formatter("%(asctime)s [%(levelname)s] %(message)s", "%H:%M:%S"))
        logger.addHandler(handler)
    return logger


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
        values = struct.unpack(OBS_FORMAT, chunk)
        units.append(dict(zip(OBS_FIELDS, values)))
    return units


def build_response(actions):
    """actions: list of (unitId, direction) tuples"""
    header = struct.pack("<i", len(actions))
    body = b"".join(struct.pack(ACTION_FORMAT, uid, direction) for uid, direction in actions)
    return header + body


def validate_ranges(unit, logger):
    """관측값이 기대 범위를 벗어나면 경고 로그. reward/done은 범위가 없으므로 검사 대상에서 제외."""
    checks = [
        ("distanceToTarget", unit["distanceToTarget"], 0.0, 1.0),
        ("dotToTarget", unit["dotToTarget"], -1.0, 1.0),
        ("crossToTarget", unit["crossToTarget"], -1.0, 1.0),
    ]
    checks += [(f"h{i}", unit[f"h{i}"], 0.0, 1.0) for i in range(6)]

    ok = True
    for name, val, lo, hi in checks:
        if not (lo - 1e-4 <= val <= hi + 1e-4):
            logger.warning(f"[Unit {unit['unitId']}] {name}={val:.4f} 범위 [{lo},{hi}] 벗어남")
            ok = False
    return ok