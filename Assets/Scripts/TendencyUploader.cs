using System;
using UnityEngine;
using UnityEngine.UI;

public class TendencyUploader : MonoBehaviour
{
    public RLManager rlManager;
    public Slider AllyTendencySlider;
    public Slider EnemyTendencySlider;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        AllyTendencySlider.onValueChanged.AddListener(OnAllyTendencyChanged);
        EnemyTendencySlider.onValueChanged.AddListener(OnEnemyTendencyChanged);

        AllyTendencySlider.value = rlManager.AllyData.AttackTendency;
        EnemyTendencySlider.value = rlManager.EnmyData.AttackTendency;
    }

    private void OnEnemyTendencyChanged(float arg0)
    {
        rlManager.EnmyData.AttackTendency = Mathf.RoundToInt(arg0);
    }

    private void OnAllyTendencyChanged(float arg0)
    {
        rlManager.AllyData.AttackTendency = Mathf.RoundToInt(arg0);
    }

}
