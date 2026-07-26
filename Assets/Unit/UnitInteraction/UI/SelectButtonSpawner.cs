using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class SelectButtonSpawner : MonoBehaviour
{
    public GridLayoutGroup gridLayout;
    public RectTransform ContentRect;
    public GameObject SlotPrefab;

    public int initilizeAmount ;
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    CancellationTokenSource _cts;

    public HashSet<UnitEnum> key;

    int selectAmount = 0;


    async void Start()
    {
        key = new ();
    }
    void OnDestroy()
    {
        
    }

    public void Initilize(UnitEnum unitEnum, NativeArray<Entity> unitArray, List<UnitEnumInterface> actions)
    {
        if (unitArray.Length == 0 && selectAmount > 0)
        {
            DestroySlotsFor(unitEnum);
            return;
        }

        if (!key.Contains(unitEnum))
        {
            key.Add(unitEnum);
            var ctx = new UnitActionContext { unitEnum = unitEnum, Entities = unitArray };
            CreateSlotFor(unitEnum, actions, ctx);
        }
    }

    public void DestroySlotsFor(UnitEnum unitEnum)
    {
        if (!key.Remove(unitEnum)) return; // 이미 없으면 할 일 없음

        for (int i = ContentRect.childCount - 1; i >= 0; i--)
        {
            var child = ContentRect.GetChild(i);
            if (child.TryGetComponent<SelectButtonClicker>(out var c) && c.unitEnum == unitEnum) // 필터링 필수
                Destroy(child.gameObject);
        }
    }

    public async void UpdateUI(int selectAll)
    {
        selectAmount = selectAll;

        //Initilize 에서 수집하고 여기서 생성 제거를 관리 한다면??


        _cts?.Cancel();               // 이전 진행 중이던 것 취소
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var count = ContentRect.childCount;

        try
        {
            await LateStartTask(selectAmount, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private void CreateSlotFor(UnitEnum unitEnum, List<UnitEnumInterface> actions, UnitActionContext ctx)
    {
        for (int i = 0; i < actions.Count; i++)
        {
            var slot = GameObject.Instantiate(SlotPrefab, ContentRect);
            var click = slot.GetComponent<SelectButtonClicker>();

            var button = slot.GetComponent<Button>();
            click.button = button;
            click.unitEnum = unitEnum;
            click.buttonSpawner = this;
            // click.index = ContentRect.childCount - 1;
            click.Bind(actions[i], ctx);
        }
    }

    public void ReleaseButton(UnitEnum unitEnum)
    {
        //!단순 클릭 구분 

        if (selectAmount > 0)
            key.Remove(unitEnum);
    }

    async Task LateStartTask(int amount, CancellationToken token)
    {
        while (ContentRect.childCount != amount)
        {
            token.ThrowIfCancellationRequested();
            await Awaitable.NextFrameAsync();

            var size = ContentRect.sizeDelta;
            size.y = gridLayout.minHeight;
            ContentRect.sizeDelta = size;
        }
    }



}
