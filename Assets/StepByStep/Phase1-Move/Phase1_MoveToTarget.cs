using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>
    /// Phase1: 목표물로 이동하는 것만 학습. 장애물은 아직 없음(평지) -> h0~h5는 항상 1.
    /// 다음 페이즈에서 장애물이 추가되면 Phase2_* 클래스를 새로 만들면 되고, 이 클래스는 그대로 둔다.
    /// </summary>
    public class Phase1_MoveToTarget : RLPhaseBase<Unit, Phase1Observation, Phase1Action>
    {
        public override Phase1Observation Encode(Unit self)
        {
            var data = new Phase1Observation { unitId = self.unitId };

            Vector2 myWorld = HexUtil.AxialToWorld(self.axialPosition);
            Vector2 targetWorld = HexUtil.AxialToWorld(self.targetAxial);
            Vector2 toTarget = targetWorld - myWorld;

            float rawDistance = toTarget.magnitude; // 정규화 전 실제 월드 거리 - 보상 계산용
            float mapMaxDistance = self.gameManagerRef.MapRadius * 2f;
            data.distanceToTarget = Mathf.Clamp01(rawDistance / mapMaxDistance);

            Vector2 forward = HexUtil.FacingToVector(self.facingDirection);
            Vector2 targetDir = toTarget.normalized;

            data.dotToTarget = Vector2.Dot(forward, targetDir);
            data.crossToTarget = forward.x * targetDir.y - forward.y * targetDir.x;

            // Phase1: 장애물 없음 (평지) -> 항상 최댓값
            data.h0 = 1f; data.h1 = 1f; data.h2 = 1f;
            data.h3 = 1f; data.h4 = 1f; data.h5 = 1f;

            // 보상/종료는 Unity가 실제 상태(경계, raw 거리)로 계산한다.
            var (reward, done) = Phase1RewardCalculator.Compute(self, rawDistance, data.dotToTarget);
            data.reward = reward;
            data.done = done ? 1 : 0;

            // 다음 스텝의 거리 변화량 계산을 위해 이번 raw 거리를 저장.
            // done이면 다음은 새 에피소드이므로 리셋.
            if (done)
            {
                self.hasPrevDistance = false;
            }
            else
            {
                self.prevDistanceRaw = rawDistance;
                self.hasPrevDistance = true;
            }

            return data;
        }

        public override void Apply(Unit self, Phase1Action action)
        {
            int dir = ((action.direction % 6) + 6) % 6;
            self.facingDirection = dir;
            self.axialPosition = HexUtil.GetNeighbor(self.axialPosition, dir);
            self.SyncTransform();
        }
    }
}