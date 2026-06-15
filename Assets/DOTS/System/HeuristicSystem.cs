// using Unity.Collections;
// using Unity.Entities;
// using Unity.Mathematics;
// using Unity.Transforms;
// using UnityEngine;

// [UpdateInGroup(typeof(SimulationSystemGroup))]
// [UpdateBefore(typeof(MoveToTarget))]
// public partial struct HeuristicSystem : ISystem
// {
//     private KDTreeStruct _kdTree;
//     private NativeList<float3> _points;
//     private NativeList<byte> _masks;
//     private bool _built;

//     private NativeHashMap<int2, int> _cellIndexMap;

//     public void OnCreate(ref SystemState state)
//     {
//         _points = new NativeList<float3>(Allocator.Persistent);
//         _masks = new NativeList<byte>(Allocator.Persistent);
//         _cellIndexMap = new NativeHashMap<int2, int>(256, Allocator.Persistent);
//         _built = false;
//     }

//     public void OnUpdate(ref SystemState state)
//     {
//         if (!SystemAPI.TryGetSingleton<RLConfig>(out var config)) return;
//         if (config.IsInferenceMode) return;

//         // 최초 1회 빌드
//         if (!_built)
//         {
//             int idx = 0;
//             foreach (var tile in SystemAPI.Query<RefRO<HexTile>>())
//             {
//                 _points.Add(HexMetrics.OffsetToWorld(new int2(tile.ValueRO.X, tile.ValueRO.Z)));
//                 _masks.Add((byte)tile.ValueRO.OwnerID);
//                 _cellIndexMap.TryAdd(new int2(tile.ValueRO.X, tile.ValueRO.Z), idx);
//                 idx++;
//             }

//             if (_points.Length == 0) return;
//             _kdTree.MakeFromPoints(_points.AsArray(), _masks.AsArray(), Allocator.Persistent);
//             _built = true;
//         }

//         // 점령 변경 시 마스크 업데이트
//         bool dirty = false;
//         foreach (var tile in SystemAPI.Query<RefRO<HexTile>>())
//         {
//             if (tile.ValueRO.JustCaptured) { dirty = true; break; }
//         }

//         if (dirty)
//         {
//             int i = 0;
//             foreach (var tile in SystemAPI.Query<RefRO<HexTile>>())
//                 _masks[i++] = (byte)tile.ValueRO.OwnerID;
//             _kdTree.UpdateMasks(_masks.AsArray());
//         }

//         // 유닛마다 가장 가까운 미점령 셀로 이동
//         foreach (var (transform, moveTarget) in
//             SystemAPI.Query<RefRO<LocalTransform>, RefRW<MoveTargetComponent>>())
//         {
//             var cell = HexMetrics.WorldToOffset(transform.ValueRO.Position);

//             if (!_cellIndexMap.TryGetValue(new int2(cell.x, cell.y), out int cellIndex)) continue;

//             // 현재 셀 임시로 제외
//             _masks[cellIndex] = (byte)GroupType.Ally;
//             _kdTree.UpdateMasks(_masks.AsArray());

//             int nearest = _kdTree.FindNearest(transform.ValueRO.Position, (int)GroupType.None, Ally: false);
//             Debug.Log($"nearest={nearest} target={_points[nearest]} current={transform.ValueRO.Position} cellMask={_masks[cellIndex]}");

//             // 마스크 복원
//             _masks[cellIndex] = (byte)GroupType.None;
//             _kdTree.UpdateMasks(_masks.AsArray());

//             if (nearest == -1) continue;

//             moveTarget.ValueRW.Target = _points[nearest]; // ← 여기가 빠져있었어요
//         }
//     }

//     public void OnDestroy(ref SystemState state)
//     {
//         if (_built) _kdTree.DisposeArrays();
//         _points.Dispose();
//         _masks.Dispose();
//         _cellIndexMap.Dispose();
//     }
// }