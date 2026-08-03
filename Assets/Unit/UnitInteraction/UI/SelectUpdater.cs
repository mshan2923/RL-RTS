using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class SelectUpdater : MonoBehaviour
{

    public SelectButtonSpawner selectButton;
    public UnitEnumDB unitEnumDB; // 액션 리스트 조회용

    int selectAmount;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void OnEnable()
    {
        SelectUnitEnum.OnInvoke += Invoke;
        // SelectUnitEnum.OnUpdater += onUpdate;
        // SelectUnitEnum.OnEndInvoke += EndInvoke;
    }

    void OnDisable()
    {
        SelectUnitEnum.OnInvoke -= Invoke;
        // SelectUnitEnum.OnUpdater -= onUpdate;
        // SelectUnitEnum.OnEndInvoke -= EndInvoke;
    }


    void Invoke(UnitEnum unitEnum, Entity[] unitArray)
    {
        var actions = unitEnumDB.Types.Find(t => t.unitType == unitEnum).Pure;
        selectButton.Initilize(unitEnum, unitArray, actions);
    }
    void onUpdate(int selectAll)
    {
        selectAmount = selectAll;
        selectButton.UpdateUI(selectAll);
    }

    void EndInvoke(List<UnitEnum> activeTypes)
    {

    }
}
