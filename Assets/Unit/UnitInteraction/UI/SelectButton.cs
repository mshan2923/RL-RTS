using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class SelectButton : MonoBehaviour
{
    public GridLayoutGroup gridLayout;
    public RectTransform ContentRect;
    public GameObject SlotPrefab;

    public int initilizeAmount ;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    async void Start()
    {
        if (initilizeAmount <= 0)
        {
            for(int i = 0; i < initilizeAmount; i++)
            {
                var slot = GameObject.Instantiate(SlotPrefab, ContentRect);
            }

            await LateStart();   
        }
    }

    public async Task<int> Initilize(int amount)
    {
        var count = ContentRect.childCount;
            for(int i = 0; i < amount; i++)
            {
                var slot = GameObject.Instantiate(SlotPrefab, ContentRect);
            }

            await LateStart(count);   

            return count;
    }

    async Awaitable LateStart(int amount = 0)
    {
        while(ContentRect.childCount == amount)
        {
            await Awaitable.NextFrameAsync();

            var size = ContentRect.sizeDelta;//gridLayout.preferredHeight
            size.y = gridLayout.minHeight;
            ContentRect.sizeDelta = size;
        }
    }
}
