using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>뾰족(pointy-top) 육각 그리드 좌표 수학. axial(q, r) 좌표계 사용.</summary>
    public static class HexUtil
    {
        // pointy-top 기준 6방향 axial offset. 인덱스 순서는 라벨일 뿐이며
        // facingDirection 만큼 shift해서 상대좌표로 쓴다.
        private static readonly Vector2Int[] Directions =
        {
            new Vector2Int(1, 0),
            new Vector2Int(1, -1),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0),
            new Vector2Int(-1, 1),
            new Vector2Int(0, 1),
        };

        public const float HexSize = 1f;

        public static Vector2Int GetNeighbor(Vector2Int axial, int direction)
        {
            return axial + Directions[((direction % 6) + 6) % 6];
        }

        public static Vector2 AxialToWorld(Vector2Int axial)
        {
            float x = HexSize * (Mathf.Sqrt(3f) * axial.x + Mathf.Sqrt(3f) / 2f * axial.y);
            float y = HexSize * (3f / 2f * axial.y);
            return new Vector2(x, y);
        }

        /// <summary>
        /// facingDirection(0~5)을 단위 방향 벡터로 변환.
        ///
        /// 이전 버전은 "angle = direction*60°(반시계 가정)"으로 별도 계산했는데,
        /// 실제 GetNeighbor()가 만드는 axial offset은 AxialToWorld 변환상 시계방향으로 배치되어 있어서
        /// 0/3번 방향만 우연히 일치하고 나머지(1,2,4,5)는 좌우 반전된 값이 나오는 버그가 있었다.
        /// 그 결과 dot/cross 관측이 실제 이동 결과와 절반 이상의 방향에서 어긋나 있었고,
        /// 이게 지속적인 방향 편향 학습의 원인이었을 가능성이 높다.
        ///
        /// 수정: 별도 각도 공식 대신, GetNeighbor + AxialToWorld로 "실제로 그 방향 이동 시 향하는
        /// 벡터"를 직접 계산해서 이동 로직과 100% 일치하도록 단일 진실 소스로 통일한다.
        /// </summary>
        public static Vector2 FacingToVector(int facingDirection)
        {
            Vector2Int neighbor = GetNeighbor(Vector2Int.zero, facingDirection);
            Vector2 world = AxialToWorld(neighbor);
            return world.normalized;
        }

        public static int AxialDistance(Vector2Int a, Vector2Int b)
        {
            int q1 = a.x, r1 = a.y;
            int q2 = b.x, r2 = b.y;
            return (Mathf.Abs(q1 - q2) + Mathf.Abs(q1 + r1 - q2 - r2) + Mathf.Abs(r1 - r2)) / 2;
        }
    }
}