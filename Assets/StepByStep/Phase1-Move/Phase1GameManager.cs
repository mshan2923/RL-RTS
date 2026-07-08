using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>
    /// Phase1: 단순히 씬에 배치된 목표물 하나의 위치를 모든 유닛에게 알려준다.
    /// 나중에 유닛마다 다른 목표, 랜덤 스폰, 다중 목표 등으로 확장하려면
    /// 이 클래스를 상속하거나 새 GameManagerBase 구현체를 만들면 된다.
    /// </summary>
    public class Phase1GameManager : GameManagerBase
    {
        [SerializeField] private Transform targetTransform;
        private Vector2Int targetAxial;

        void Awake()
        {
            RecalculateTarget();
        }

        private void RecalculateTarget()
        {
            if (targetTransform == null)
            {
                Debug.LogWarning("[Phase1GameManager] targetTransform이 지정되지 않았습니다.");
                return;
            }
            Vector2 targetWorld = new Vector2(targetTransform.position.x, targetTransform.position.z);
            targetAxial = WorldToAxialApprox(targetWorld);
        }

        // 간단한 근사 역변환 (필요시 HexUtil에 정식 WorldToAxial 추가해 교체 가능)
        private Vector2Int WorldToAxialApprox(Vector2 world)
        {
            float q = (Mathf.Sqrt(3f) / 3f * world.x - 1f / 3f * world.y) / HexUtil.HexSize;
            float r = (2f / 3f * world.y) / HexUtil.HexSize;
            return new Vector2Int(Mathf.RoundToInt(q), Mathf.RoundToInt(r));
        }

        public override Vector2Int GetTargetFor(Unit unit)
        {
            return targetAxial;
        }

        public override void ResetEpisode()
        {
            // Phase1은 목표 위치가 고정이라 재계산만. 나중에 랜덤 스폰 추가 시 여기서 처리.
            RecalculateTarget();
        }

        public override void RespawnUnit(Unit unit)
        {
            // 목표 근처에 계속 도달만 학습되는 걸 막기 위해, 매번 다른 스폰 지점에서 시작하게 한다.
            // mapRadius 내부의 무작위 셀을 고르되, 목표 셀과 겹치지 않도록 재시도.
            Vector2Int spawn;
            int guard = 0;
            do
            {
                spawn = RandomCellWithinRadius(mapRadius);
                guard++;
            }
            while (spawn == targetAxial && guard < 10);

            unit.axialPosition = spawn;
            unit.facingDirection = Random.Range(0, 6);
        }

        private Vector2Int RandomCellWithinRadius(int radius)
        {
            // axial 좌표계에서 반지름 내부 균등 샘플 (간단한 리젝션 샘플링)
            int q, r;
            do
            {
                q = Random.Range(-radius, radius + 1);
                r = Random.Range(-radius, radius + 1);
            }
            while (Mathf.Abs(q) + Mathf.Abs(q + r) + Mathf.Abs(r) > radius * 2);

            return new Vector2Int(q, r);
        }
    }
}
