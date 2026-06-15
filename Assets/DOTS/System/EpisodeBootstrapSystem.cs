using Unity.Entities;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct EpisodeBootstrapSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        var entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new EpisodeState
        {
            NeedsReset = false,
            SkipAction = false,
            EpisodeCount = 0
        });
        state.Enabled = false; // 한 번만 실행
    }

    public void OnUpdate(ref SystemState state) { }
}