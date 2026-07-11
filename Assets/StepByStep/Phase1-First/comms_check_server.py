"""
Phase1 통신 확인용 서버. 학습 코드는 별도로 작성 예정이므로,
여기서는 수신 데이터 구조/범위 확인 + 랜덤 액션 응답만 한다.

프로토콜:
  수신: [count:int32][Phase1Observation * count]
        Phase1Observation = "<i6f" (unitId + distance, dot, cross, h0~h5 => 실제로는 i + 8f)
  송신: [count:int32][(unitId:int32, direction:int32) * count]
"""

import socket
import struct
import random

# unitId(int) + distanceToTarget, dotToTarget, crossToTarget, h0~h5 (float 9개)
OBS_FORMAT = "<i9f"
OBS_SIZE = struct.calcsize(OBS_FORMAT)


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
        unit_id, dist, dot, cross, h0, h1, h2, h3, h4, h5 = struct.unpack(OBS_FORMAT, chunk)
        units.append({
            "unitId": unit_id,
            "distanceToTarget": dist,
            "dotToTarget": dot,
            "crossToTarget": cross,
            "h": [h0, h1, h2, h3, h4, h5],
        })
    return units


def print_units(units):
    print(f"\n=== 배치 수신: {len(units)}개 유닛 ===")
    for u in units:
        h = u["h"]
        print(
            f"[Unit {u['unitId']}] dist={u['distanceToTarget']:.3f} "
            f"dot={u['dotToTarget']:.3f} cross={u['crossToTarget']:.3f} "
            f"h=[{', '.join(f'{v:.2f}' for v in h)}]"
        )
        # 범위 확인
        checks = [("dist", u["distanceToTarget"], 0, 1),
                  ("dot", u["dotToTarget"], -1, 1),
                  ("cross", u["crossToTarget"], -1, 1)]
        checks += [(f"h{i}", h[i], 0, 1) for i in range(6)]
        for name, val, lo, hi in checks:
            if not (lo <= val <= hi):
                print(f"  ⚠ 경고: {name}={val:.3f} 이(가) [{lo},{hi}] 범위를 벗어남")


def decide_random_actions(units):
    return [(u["unitId"], random.randint(0, 5)) for u in units]


def build_response(actions):
    header = struct.pack("<i", len(actions))
    body = b"".join(struct.pack("<ii", uid, direction) for uid, direction in actions)
    return header + body


def start_server(host="127.0.0.1", port=5555):
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((host, port))
    server.listen(1)
    print(f"대기 중... {host}:{port}")

    conn, addr = server.accept()
    print(f"연결됨: {addr}")

    try:
        while True:
            header = recv_exact(conn, 4)
            if header is None:
                print("연결 종료됨")
                break

            count = struct.unpack("<i", header)[0]
            data_bytes = recv_exact(conn, OBS_SIZE * count)
            if data_bytes is None:
                print("데이터 수신 중 연결 끊김")
                break

            units = parse_batch(data_bytes, count)
            print_units(units)

            actions = decide_random_actions(units)
            conn.sendall(build_response(actions))

    except KeyboardInterrupt:
        print("서버 종료")
    finally:
        conn.close()
        server.close()


if __name__ == "__main__":
    start_server()
