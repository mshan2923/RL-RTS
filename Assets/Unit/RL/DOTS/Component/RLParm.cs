using Unity.Entities;
using Unity.Mathematics;

public struct RLParm : IComponentData // 혹은 그냥 buffer/singleton에 담을 struct
{
    public float3 Direction;
    public float Distance;
    public float SelfHP;
    public float TargetHP;
    
            //? 우선 모델을 작게 시작
        // public float AllyDPS;
        // public float EnmyDPS;
        // public float Cooltime;
        // public float AllyShells;
        // public float EnmyShells;
}
