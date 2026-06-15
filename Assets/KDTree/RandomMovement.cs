using Unity.Mathematics;
using UnityEngine;

public class RandomMovement : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    float3 RandomPos;
    float SpawnRadius = 20f;
    bool reset = false;
    public float Speed = 1f;

    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (reset)
        {
            RandomPos = UnityEngine.Random.insideUnitSphere * SpawnRadius;
            reset = false;
        }

        if (math.distancesq(gameObject.transform.position, RandomPos) > 0.01f)
        {
            gameObject.transform.position = math.lerp(gameObject.transform.position, RandomPos, Time.deltaTime * Speed);

        }
        else
        {
            reset = true;
        }
    }
}
