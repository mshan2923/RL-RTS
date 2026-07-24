using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class SelectUpdater : MonoBehaviour , UnitEnumInterface
{
    public SelectButton selectButton;

    public void EndInvoke(UnitEnum unitEnum)
    {
        Debug.Log($"End");
    }

    //! SelectUnitEnum 에 연결해둬 이벤트 받기
    public void Invoke(UnitEnum unitEnum, NativeArray<Entity> unitArray)
    {
        //var StartIndex = await selectButton.Initilize(unitArray.Length);
        Debug.Log($"New Icon : {-1}");
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
 
    }

}
