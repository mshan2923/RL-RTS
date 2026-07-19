// using Unity.Collections;
// using Unity.Entities;
// using Unity.Mathematics;
// using Unity.Transforms;

// public partial struct SendSystem : ISystem
// {
//     // ── 보상 튜닝 값 (1단계: 기지로 이동하는 행동 학습용) ──
//     private const float ArrivalRadius = 1.0f;
//     private const float ApproachReward = 2.0f;   // 기지에 가까워졌을 때
//     private const float RetreatPenalty = 1.0f;   // 기지에서 멀어졌을 때
//     private const float FacingBonus = 0.1f;       // 기지 방향을 바라보고 있을 때
//     private const float ArrivalBonus = 50.0f;     // 기지에 도착했을 때

//     private int _step;
//     private float _prevCaptureRatio; // 2단계(점령 비율 기반 보상)에서 사용 예정, 현재는 미사용

//     private EntityQuery _tileQuery;
//     private EntityQuery _baseQuery;
//     private EntityQuery _unitQuery;

//     public void OnCreate(ref SystemState state)
//     {
//         state.RequireForUpdate<WebSocketManagerComponent>();
//         state.RequireForUpdate<RLConfig>();

//         _tileQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<HexTile>().Build(ref state);
//         _baseQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<BaseComponent>().Build(ref state);
//         _unitQuery = new EntityQueryBuilder(Allocator.Temp)
//             .WithAll<LocalTransform, UnitComponent, DetectWallNormalize, MoveTargetComponent>().Build(ref state);
//     }

//     public void OnUpdate(ref SystemState state)
//     {
//         if (!SystemAPI.TryGetSingleton<RLConfig>(out var config) || config.IsInferenceMode) return;
//         if (!SystemAPI.TryGetSingleton<MapConfig>(out var mapConfig)) return;
//         if (!SystemAPI.TryGetSingleton<EpisodeState>(out var episodeState)) return;
//         if (!SystemAPI.TryGetSingleton<BaseComponent>(out var baseComponent)) return;

//         var manager = SystemAPI.ManagedAPI.GetSingleton<WebSocketManagerComponent>();

//         // 에피소드 초기화 가드
//         if (episodeState.NeedsReset)
//         {
//             _step = 0;
//             _prevCaptureRatio = 0f;
//         }
//         _step++;

//         // 1. 전체 맵 타일 점령 비율 계산
//         var tileArray = _tileQuery.ToComponentDataArray<HexTile>(Allocator.Temp);
//         int capturedTiles = 0;
//         for (int i = 0; i < tileArray.Length; i++)
//         {
//             if (tileArray[i].OwnerID == GroupType.Ally) capturedTiles++;
//         }
//         float captureRatio = tileArray.Length > 0 ? (float)capturedTiles / tileArray.Length : 0f;

//         // 2. 아군 기지 위치 및 맵 최대 거리(정규화 기준) 계산
//         float3 basePos = baseComponent.Position;
//         float maxDist = math.sqrt(mapConfig.Width * mapConfig.Width + mapConfig.Height * mapConfig.Height);
//         var baseArray = _baseQuery.ToComponentDataArray<BaseComponent>(Allocator.Temp);
//         for (int i = 0; i < baseArray.Length; i++)
//         {
//             if (baseArray[i].Team == GroupType.Ally)
//             {
//                 basePos = baseArray[i].Position;
//                 break;
//             }
//         }

//         // 3. 유닛 데이터 수집
//         var transforms = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
//         var units = _unitQuery.ToComponentDataArray<UnitComponent>(Allocator.Temp);
//         var unitDetectWall = _unitQuery.ToComponentDataArray<DetectWallNormalize>(Allocator.Temp); // 현재 미사용 (벽 회피 보상 재도입 시 사용)
//         var moveTargets = _unitQuery.ToComponentDataArray<MoveTargetComponent>(Allocator.Temp);

//         // 4. 기지 도착 여부 판단 (반경 ArrivalRadius 이내)
//         bool arrived = false;
//         for (int i = 0; i < units.Length; i++)
//         {
//             if (math.length(basePos - transforms[i].Position) < ArrivalRadius)
//             {
//                 arrived = true;
//                 break;
//             }
//         }

//         bool done = arrived || _step >= config.MaxSteps;
//         if (done)
//         {
//             _step = 0;
//             _prevCaptureRatio = 0f;
//             episodeState.NeedsReset = true;
//             episodeState.SkipAction = true;
//             SystemAPI.SetSingleton(episodeState);
//         }

//         // 5. 유닛별 상태/보상 계산 후 큐 전송
//         for (int i = 0; i < units.Length; i++)
//         {
//             var cell = HexMetrics.WorldToOffset(transforms[i].Position);
//             var euler = math.EulerXYZ(transforms[i].Rotation.value);
//             int yaw = HexMetrics.WorldYawToIndex(math.degrees(euler.y));

//             float3 toBase = moveTargets[i].BasePose - transforms[i].Position;
//             float currentBaseDist = math.length(toBase);
//             float normalizedBaseDist = currentBaseDist / maxDist;
//             float baseDir = (math.degrees(math.atan2(toBase.x, toBase.z)) + 180f) / 360f;

//             float reward = CalculateApproachReward(moveTargets[i], transforms[i].Position, yaw, baseDir, currentBaseDist);

//             // TargetInfo(점령 지점 타게팅)는 2단계에서 사용 예정이라 현재는 비활성화 상태로 전송
//             manager.StateQueue.Enqueue(new WebSocketManager.StateData
//             {
//                 UnitId = units[i].Id,
//                 Col = cell.x,
//                 Row = cell.y,
//                 Yaw = yaw,
//                 Reward = reward,
//                 Done = done,
//                 CaptureRatio = captureRatio,
//                 BaseDist = normalizedBaseDist,
//                 BaseDir = baseDir,
//                 N0 = 1f,
//                 N1 = 1f,
//                 N2 = 1f,
//                 N3 = 1f,
//                 N4 = 1f,
//                 N5 = 1f,
//                 TargetActive = 0f,
//                 TargetDist = 0f,
//                 TargetDir = 0f
//             });
//         }

//         // 6. 타일 캡처 플래그 후처리
//         for (int i = 0; i < tileArray.Length; i++)
//         {
//             var t = tileArray[i];
//             if (t.JustCaptured)
//             {
//                 t.JustCaptured = false;
//                 tileArray[i] = t;
//             }
//         }
//         _tileQuery.CopyFromComponentDataArray(tileArray);
//         _prevCaptureRatio = captureRatio;

//         tileArray.Dispose();
//         baseArray.Dispose();
//         transforms.Dispose();
//         units.Dispose();
//         unitDetectWall.Dispose();
//         moveTargets.Dispose();
//     }

//     // 1단계 보상: 기지 접근 + 기지 방향 응시 + 도착 보너스
//     private static float CalculateApproachReward(in MoveTargetComponent moveTarget, float3 currentPos, int yaw, float baseDir, float currentBaseDist)
//     {
//         float reward = 0f;

//         // 전진 보상: 직전 위치보다 기지에 가까워졌으면 +, 멀어지거나 그대로면 -
//         float prevDistSq = math.distancesq(moveTarget.BasePose, moveTarget.PrevPosition);
//         float currDistSq = math.distancesq(moveTarget.BasePose, currentPos);
//         reward += (prevDistSq - currDistSq) > 0f ? ApproachReward : -RetreatPenalty;

//         // 방향 보상: 기지 방향(0~5 헥스 인덱스)을 정확히 바라보고 있으면 추가 점수
//         int targetYawIndex = math.clamp((int)math.round(baseDir * 6f) % 6, 0, 5);
//         if (yaw == targetYawIndex)
//         {
//             reward += FacingBonus;
//         }

//         // 도착 보상: 기지 반경 안에 들어오면 큰 보상
//         if (currentBaseDist < ArrivalRadius)
//         {
//             reward += ArrivalBonus;
//         }

//         return reward;
//     }
// }