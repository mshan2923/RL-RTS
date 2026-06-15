using Unity.Entities;

public struct EpisodeState : IComponentData
{
    public bool NeedsReset;
    public bool SkipAction; // 리셋 프레임에 ReceiveSystem 스킵용
    public int EpisodeCount;
}