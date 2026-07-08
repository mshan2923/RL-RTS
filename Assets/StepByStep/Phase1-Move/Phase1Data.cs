using System.Runtime.InteropServices;

namespace RL_StepByStep
{
    /// <summary>
    /// Phase1: 목표물로 이동. dot/cross로 목표 방향을 정규화하여 wrap-around 불연속 문제를 피한다.
    /// 보상(reward)/종료(done) 계산은 Unity에서 수행한다 (RewardCalculator 참고) —
    /// Python은 실제 axialPosition, 맵 경계 등 진짜 상태를 모르고 정규화된 관측값만 보므로,
    /// 경계 이탈 같은 상황을 Python 쪽 distanceToTarget(0~1 clamp)만으로는 정확히 판정할 수 없다.
    ///
    /// Python struct 포맷: "&lt;i10fi"
    /// (unitId + distanceToTarget, dotToTarget, crossToTarget, h0~h5, reward + done)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Phase1Observation
    {
        public int unitId;

        /// <summary>목표까지 거리 / 맵 최대거리 (0~1, clamp됨 — 경계 판정용으로 쓰지 말 것)</summary>
        public float distanceToTarget;

        /// <summary>dot(정면, 목표방향). 1=정면 정렬, -1=반대. 좌우 구분 불가.</summary>
        public float dotToTarget;

        /// <summary>cross(정면, 목표방향). 부호로 좌(-)/우(+) 구분.</summary>
        public float crossToTarget;

        /// <summary>유닛 정면(h0) 기준 상대 방향 장애물 거리. 최댓값 1 = 장애물 없음.</summary>
        public float h0, h1, h2, h3, h4, h5;

        /// <summary>Unity에서 계산된 이번 스텝 보상. Python은 이 값을 그대로 학습에 사용한다.</summary>
        public float reward;

        /// <summary>1이면 이번 스텝으로 에피소드 종료(도달 또는 경계 이탈). Python은 여기서 리턴 계산을 끊는다.</summary>
        public int done;
    }

    /// <summary>
    /// Python struct 포맷: "&lt;i" (direction: 0~5).
    /// done은 Unity가 이미 계산해서 알고 있으므로(Phase1Observation.done) 왕복으로 돌려받을 필요가 없다.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Phase1Action
    {
        public int direction;
    }
}