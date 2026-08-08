using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

partial struct MoveToTarget : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) { }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var moveLength = 5f * MapConfig.FixedStepSize;
        var data = state.World.GetExistingSystemManaged<ObstacleSystem>().data.AsReadOnly();

        var job = new MoveToTargetJob
        {
            moveLength = moveLength,
            obstacleData = data
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }

    [BurstCompile]
    public partial struct MoveToTargetJob : IJobEntity
    {
        public float moveLength;
        [ReadOnly] public NativeParallelHashMap<int2, HexTile>.ReadOnly obstacleData; // 실제 타입명은 ObstacleSystem.data 타입에 맞춰 조정 필요

        public void Execute(ref MoveTargetComponent move, ref LocalTransform trans)
        {
            var currentPos = trans.Position;
            var targetPos = move.MoveTo;

            var dis = math.distance(currentPos, targetPos);
            if (dis < 0.1f) return;

            var direction = math.normalize(targetPos - currentPos);
            var nextPos = currentPos + direction * math.min(moveLength, dis);

            var offset = HexMetrics.WorldToOffset(nextPos);
            bool isBlocked = false;

            if (obstacleData.IsCreated)
            {
                if (obstacleData.TryGetValue(offset, out var tile))
                {
                    if (tile.OwnerID == GroupType.Wall)
                        isBlocked = true;
                }
            }

            if (!isBlocked)
            {
                move.PrevPosition = currentPos;
                trans.Position = nextPos;
                trans.Rotation = quaternion.LookRotationSafe(direction, math.up());
            }
        }
    }
}