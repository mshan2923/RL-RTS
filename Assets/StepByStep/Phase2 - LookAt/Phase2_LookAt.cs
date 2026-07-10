using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>facing 없이 순수 이동만, PPO 연속 액션(dx,dy) 버전.</summary>
    public class Phaase2_ToTarget : RLPhaseBase<Phase2Unit, Phase2Observation, Phase2Action>
    {
        public const float OutOfBoundsPenalty = -5f;
        public const float TargetReachedBonus = 100f; // 10 -> 60: 거리 변화 보상(최대 ±50대)에 묻히지 않도록 상향
        public const float DistRewardScale = 20f;
        public const float StepPenalty = -0.05f;
        public const float MinTargetReachedDistance = 1f;

        private float GetTargetReachedDistance(Phase2Unit self)
        {
            // 버그 수정: 이전엔 Time.deltaTime만 썼는데, 실제 이동 공식(SyncTransform)은
            // Speed * Mathf.Max(Interval, Time.deltaTime)를 쓴다. Interval이 프레임 deltaTime보다
            // 크면(보통 그렇다 - 스텝 간격을 일부러 늘려두니까) 임계값이 실제 이동량보다 작게 잡혀서
            // 여전히 목표를 지나쳐버렸다. 반드시 같은 공식을 써야 한다.
            float stepMoveDist = self.Speed * Mathf.Max(Phase2StepManager.Instance.Interval, Time.deltaTime);
            return Mathf.Max(MinTargetReachedDistance, stepMoveDist * 1.5f);
        }

        private readonly StringBuilder logBuffer = new StringBuilder();
        private int logCount;
        private const int LogFlushEvery = 20; // 이 개수만큼 모이면 한 번에 출력

        private void LogStep(Phase2Observation data, float currDist, bool outOfBounds)
        {
            logBuffer.AppendLine(
                $"dx={data.dx:F3} dy={data.dy:F3} dist={currDist:F3} oob={outOfBounds} done={data.done} reward={data.reward:F4}");
            logCount++;

            if (logCount >= LogFlushEvery)
            {
                Debug.Log(logBuffer.ToString());
                logBuffer.Clear();
                logCount = 0;
            }
        }

        public override Phase2Observation Encode(Phase2Unit self)
        {
            var data = new Phase2Observation { unitId = self.unitId };

            Vector3 targetWorld3 = self.targetTransform.position;
            var toTarget = targetWorld3 - self.transform.position;
            float currDist = toTarget.magnitude;

            float mapMaxDist = self.mapRadius * 2f;
            data.dx = Mathf.Clamp(toTarget.x / mapMaxDist, -2f, 2f);
            // 버그 수정: toTarget.y가 아니라 toTarget.z를 써야 한다. 이동이 XZ 평면에서 일어나서
            // y는 항상 0에 가까우므로, dy=toTarget.y였던 이전 버전은 사실상 항상 0을 주고 있었다.
            // 즉 에이전트는 목표까지의 z축 거리 정보를 아예 관측하지 못하고 있었다 -
            // 이게 보상 버그보다 더 근본적인 문제였을 가능성이 높다.
            data.dy = Mathf.Clamp(toTarget.z / mapMaxDist, -2f, 2f);

            float3 temp = toTarget / mapMaxDist;
            bool outOfBounds = math.any(math.abs(temp) > 2f);

            if (outOfBounds)
            {
                data.reward = OutOfBoundsPenalty;
                data.done = 1;
            }
            else if (currDist <= GetTargetReachedDistance(self))
            {
                data.reward = TargetReachedBonus;
                data.done = 1;
            }
            else if (!self.hasPrevDist)
            {
                data.reward = StepPenalty;
                data.done = 0;
            }
            else
            {
                data.done = 0;

                // 버그 수정: 기존 dotReward = Dot(self.direction, move)는 "내가 정한 방향대로
                // 움직였는가"만 측정한다. 근데 이동 로직(SyncTransform) 자체가 항상 self.direction으로만
                // 움직이므로, 방향을 잘 골랐든 못 골랐든 이 dot은 구조적으로 항상 ~1.0에 가깝게 나온다.
                // 즉 목표와 전혀 무관하게 거의 항상 최대 보상(20)이 나왔던 것.
                // 목표에 실제로 가까워졌는지를 보려면 거리 변화량을 써야 한다.
                data.reward = (self.prevDist - currDist) * DistRewardScale + StepPenalty;
            }

            self.prevDist = currDist;
            self.PrevPos = self.transform.position;
            self.hasPrevDist = true;

            LogStep(data, currDist, outOfBounds);

            return data;
        }

        public override void Apply(Phase2Unit self, Phase2Action action)
        {
            var rot = Quaternion.LookRotation(new Vector3(action.dx, 0, action.dy));
            self.direction = rot * Vector3.forward;
            self.SyncTransform();
        }
    }
}