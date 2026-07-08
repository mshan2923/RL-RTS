using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>facing 없이 순수 이동만. Phase0_FaceTarget과 이름이 헷갈려서 별도로 분리.</summary>
    public class PureMove_ToTarget : RLPhaseBase<PureMoveUnit, PureMoveObservation, PureMoveAction>
    {
        public const float OutOfBoundsPenalty = -5f;
        public const float TargetReachedBonus = 10f;
        public const float DistRewardScale = 20f;
        public const float StepPenalty = -0.02f;
        public const float TargetReachedDistance = 1f;

        public override PureMoveObservation Encode(PureMoveUnit self)
        {
            var data = new PureMoveObservation { unitId = self.unitId };

            Vector2 myWorld = HexUtil.AxialToWorld(self.axialPosition);
            Vector3 targetWorld3 = self.targetTransform.position;
            Vector2 targetWorld = new Vector2(targetWorld3.x, targetWorld3.z);
            Vector2 toTarget = targetWorld - myWorld;

            float mapMaxDist = self.mapRadius * 2f;
            data.dx = Mathf.Clamp(toTarget.x / mapMaxDist, -2f, 2f);
            data.dy = Mathf.Clamp(toTarget.y / mapMaxDist, -2f, 2f);

            bool outOfBounds = HexUtil.AxialDistance(self.axialPosition, Vector2Int.zero) > self.mapRadius;
            float currDist = toTarget.magnitude;

            if (outOfBounds)
            {
                data.reward = OutOfBoundsPenalty;
                data.done = 1;
            }
            else if (currDist <= TargetReachedDistance)
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
                data.reward = (self.prevDist - currDist) * DistRewardScale + StepPenalty;
                data.done = 0;
            }

            self.prevDist = currDist;
            self.hasPrevDist = true;

            return data;
        }

        public override void Apply(PureMoveUnit self, PureMoveAction action)
        {
            Debug.Log("Apply");
            int dir = ((action.direction % 6) + 6) % 6;
            self.axialPosition = HexUtil.GetNeighbor(self.axialPosition, dir);
            self.SyncTransform();
        }
    }
}