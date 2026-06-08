// 타일 GameObject에 부착
using UnityEngine;

public class HexTileView : MonoBehaviour
{
    [SerializeField] Material neutralMat;
    [SerializeField] Material allyMat;
    [SerializeField] Material enemyMat;

    private Renderer _renderer;
    private GroupType _lastOwner = GroupType.None;

    void Awake() => _renderer = GetComponent<Renderer>();

    public void UpdateView(GroupType owner)
    {
        if (owner == _lastOwner) return; // 변경 없으면 스킵
        _lastOwner = owner;

        _renderer.material = owner switch
        {
            GroupType.Ally => allyMat,
            GroupType.Enmy => enemyMat,
            _ => neutralMat
        };
    }
}