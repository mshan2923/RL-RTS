using System.Collections;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;

public class HexInferenceSystem : MonoBehaviour
{
    [SerializeField] ModelAsset modelAsset;
    const int k_LayersPerFrame = 20;

    private Model _model;
    private Worker _worker;


    public struct InferenceRequest
    {
        public int UnitId;
        public int Col;
        public int Row;
        public int Yaw;
        public float CaptureRatio;
    }



    void Start()
    {
        _model = ModelLoader.Load(modelAsset);
        _worker = new Worker(_model, BackendType.GPUCompute);
    }

    void Update()
    {
        while (WebSocketManager.StateQueue.TryDequeue(out var req))
            StartCoroutine(RunInference(req));
    }

    IEnumerator RunInference(WebSocketManager.StateData req)
    {
        using var input = new Tensor<float>(new TensorShape(1, 4),
                          new[] { (float)req.Col, (float)req.Row, (float)req.Yaw, req.CaptureRatio });

        var schedule = _worker.ScheduleIterable(input);
        int it = 0;
        while (schedule.MoveNext())
        {
            if (++it % k_LayersPerFrame == 0)
                yield return null;
        }

        var output = _worker.PeekOutput("policy") as Tensor<float>;
        var cpuOutput = output.ReadbackAndClone();

        int action = 0;
        float max = float.MinValue;
        for (int i = 0; i < 6; i++)
        {
            if (cpuOutput[0, i] > max) { max = cpuOutput[0, i]; action = i; }
        }
        cpuOutput.Dispose();

        // 결과를 ActionQueue에 넣기
        WebSocketManager.ActionQueue.Enqueue(new WebSocketManager.ActionData
        {
            UnitId = req.UnitId,
            Action = action
        });
    }

    void OnDestroy()
    {
        _worker?.Dispose();
    }


}