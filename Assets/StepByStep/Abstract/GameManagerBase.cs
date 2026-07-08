using UnityEngine;

namespace RL_StepByStep
{
    /// <summary>
    /// 게임 매니저 추상화. 페이즈마다 필요한 기능(목표물 지정, 스폰, 에피소드 리셋 등)이
    /// 다르므로, 공통으로 쓰이는 것만 여기 두고 나머지는 하위 클래스에서 구현한다.
    /// </summary>
    public abstract class GameManagerBase : MonoBehaviour
    {
        [SerializeField] protected int mapRadius = 10;
        public int MapRadius => mapRadius;

        /// <summary>주어진 유닛의 현재 목표 axial 좌표를 반환.</summary>
        public abstract Vector2Int GetTargetFor(Unit unit);

        /// <summary>에피소드/스텝 리셋이 필요할 때 호출 (Phase마다 의미가 다를 수 있음).</summary>
        public abstract void ResetEpisode();

        /// <summary>유닛이 에피소드를 종료(도달/실패)했을 때, 다음 에피소드를 위해 위치/상태를 리스폰.</summary>
        public abstract void RespawnUnit(Unit unit);

        /// <summary>주어진 axial 좌표가 맵 경계 안쪽인지. 기본 구현은 원형 맵(mapRadius) 기준.
        /// 맵 모양이 다르면(사각형, 커스텀 등) 하위 클래스에서 override.</summary>
        public virtual bool IsWithinBounds(Vector2Int axialPosition)
        {
            return HexUtil.AxialDistance(axialPosition, Vector2Int.zero) <= mapRadius;
        }
    }
}