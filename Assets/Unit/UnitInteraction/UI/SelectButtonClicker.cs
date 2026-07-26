using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class SelectButtonClicker : MonoBehaviour, Ikill
{
    public Button button;
    public UnitEnum unitEnum;

    public SelectButtonSpawner buttonSpawner;
    UnitEnumInterface action;
    UnitActionContext context;

    private void Start() 
    {
        button.onClick.AddListener(OnClickButton);
    }


    public void Bind(UnitEnumInterface action, UnitActionContext ctx)
    {
        this.action = action;
        this.context = ctx;
        button.GetComponentInChildren<TMP_Text>().text = action.GetLabel(ctx);
        button.interactable = action.CanExecute(ctx);
    }

    public void OnClickButton() => action.Execute(context);

    public void invoke(UnitEnum unitEnum)
    {
        if (this.unitEnum == unitEnum)
            GameObject.Destroy(gameObject);
    }
}