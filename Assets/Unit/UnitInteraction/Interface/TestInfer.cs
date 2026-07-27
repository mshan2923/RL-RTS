using UnityEngine;
using UnityEngine.EventSystems;

public class TestInfer : UnitEnumInterface
{
    public bool CanExecute(UnitActionContext ctx)
    {
        return true;
    }

    public void Execute(UnitActionContext ctx)
    {
        Debug.Log("Execute");
    }

    public string GetLabel(UnitActionContext ctx)
    {
        
        return $"{ctx.unitEnum} Test";
    }
}
