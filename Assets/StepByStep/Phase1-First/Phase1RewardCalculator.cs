using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>
    /// 보상/에피소드 종료 계산. Unity가 실제 axialPosition, 맵 경계를 알고 있으므로 여기서 계산한다.
    /// Python은 정규화된 관측값(0~1 clamp)만 보기 때문에 경계 이탈 같은 상황을
    /// 정확히 판정할 수 없다 — 이게 "범위 밖 나가도 미세 페널티만 있던" 원인이었다.
    ///
    /// Phase가 바뀌면 이 클래스의 상수/로직만 조정하거나, Phase별로 별도 Calculator를 만들면 된다.
    /// </summary>
    public static class Phase1RewardCalculator
    {
        public const float DistRewardScale = 5f;    // 100 -> 20: loss가 90~110대로 튀는 건 보상 스케일 과다 신호
        public const float DotRewardScale = 1f;    // 방향 정렬도 보상 배율
        public const float StepPenalty = -0.1f;     // 매 스텝 기본 페널티 (방황 억제)
        public const float TargetReachedBonus = 20f; // 목표 도달 보너스
        public const float OutOfBoundsPenalty = -20f; // 맵 이탈 페널티 (거리 clamp로 숨겨지던 문제 해결)
        public const float TargetReachedDistance = 5f; // 월드 거리 기준 (axial 거리 아님, 아래 참고)

        /// <summary>
        /// 유닛의 이번 스텝 보상과 종료 여부를 계산한다.
        /// self.prevDistanceRaw(정규화 전 실제 월드 거리)를 기준으로 비교하며,
        /// 호출 후 self.prevDistanceRaw를 갱신하는 것은 호출자(Unit) 책임이 아니라 이 함수가 담당한다.
        /// </summary>
        public static (float reward, bool done) Compute(Unit self, float currDistanceRaw, float dotToTarget)
        {
            bool outOfBounds = !self.gameManagerRef.IsWithinBounds(self.axialPosition);
            if (outOfBounds)
            {
                return (OutOfBoundsPenalty, true);
            }

            if (currDistanceRaw <= TargetReachedDistance)
            {
                return (TargetReachedBonus, true);
            }

            float dotReward = dotToTarget * DotRewardScale;

            if (!self.hasPrevDistance)
            {
                // 에피소드 첫 스텝: 거리 변화량을 비교할 이전 값이 없음
                return (StepPenalty + dotReward, false);
            }

            float distReward = (self.prevDistanceRaw - currDistanceRaw) * DistRewardScale;

            Debug.Log($"dis : {distReward} , dot : {dotReward} ,");

            return (distReward + dotReward + StepPenalty, false);
        }
    }
}