using Unity.Entities;
using UnityEngine;

public class RLConfigAuthoring : MonoBehaviour
{
    public int MaxSteps = 200;
    public float DetectionRange = 10f;
    public float CaptureThreshold = 0.8f;
    public bool IsInferenceMode = false;

    class Baker : Baker<RLConfigAuthoring>
    {
        public override void Bake(RLConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new RLConfig
            {
                MaxSteps = authoring.MaxSteps,
                DetectionRange = authoring.DetectionRange,
                CaptureThreshold = authoring.CaptureThreshold,
                IsInferenceMode = authoring.IsInferenceMode
            });
        }
    }
}

public struct RLConfig : IComponentData
{
    public int MaxSteps;
    public float DetectionRange;
    public float CaptureThreshold;
    public bool IsInferenceMode;
}