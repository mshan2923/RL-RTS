using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct KDTreeBuildJob : IJob
{
    const int numDims = 3;

    [ReadOnly] public NativeArray<float3> srcPoints;
    [ReadOnly] public NativeArray<byte> srcMasks;

    public NativeArray<float3> points;
    [WriteOnly] public NativeArray<byte> teamMasks;
    public NativeArray<int> leftChilds;
    public NativeArray<int> rightChilds;
    public NativeArray<int> axies;
    public NativeReference<int> pivotIndex;

    public void Execute()
    {
        for (int i = 0; i < srcPoints.Length; i++)
        {
            points[i] = srcPoints[i];
            teamMasks[i] = srcMasks[i];
            leftChilds[i] = -1;
            rightChilds[i] = -1;
            axies[i] = -1;
        }

        var indices = Iota(srcPoints.Length, Allocator.Temp);
        var stack = new NativeList<BuildFrame>(Allocator.Temp);

        stack.Add(new BuildFrame
        {
            depth = 0,
            stIndex = 0,
            enIndex = srcPoints.Length - 1,
            parentPivotIndex = -1,
            direction = -1,
            isFirstTime = true
        });

        while (stack.Length > 0)
        {
            var frame = stack[stack.Length - 1];
            stack.RemoveAt(stack.Length - 1);

            int axis = frame.depth % numDims;
            int splitPoint = FindPivotIndex(indices, frame.stIndex, frame.enIndex, axis);

            if (frame.isFirstTime)
                pivotIndex.Value = indices[splitPoint];

            int pivotIndex1 = indices[splitPoint];
            axies[pivotIndex1] = axis;

            if (frame.parentPivotIndex > -1)
            {
                if (frame.direction == 0)
                    leftChilds[frame.parentPivotIndex] = pivotIndex1;
                else if (frame.direction == 1)
                    rightChilds[frame.parentPivotIndex] = pivotIndex1;
            }

            int leftEndIndex = splitPoint - 1;
            if (leftEndIndex >= frame.stIndex)
            {
                stack.Add(new BuildFrame
                {
                    depth = frame.depth + 1,
                    stIndex = frame.stIndex,
                    enIndex = leftEndIndex,
                    parentPivotIndex = pivotIndex1,
                    direction = 0,
                    isFirstTime = false
                });
            }

            int rightStartIndex = splitPoint + 1;
            if (rightStartIndex <= frame.enIndex)
            {
                stack.Add(new BuildFrame
                {
                    depth = frame.depth + 1,
                    stIndex = rightStartIndex,
                    enIndex = frame.enIndex,
                    parentPivotIndex = pivotIndex1,
                    direction = 1,
                    isFirstTime = false
                });
            }
        }

        stack.Dispose();
        indices.Dispose();
    }

    struct BuildFrame
    {
        public int depth;
        public int stIndex;
        public int enIndex;
        public int parentPivotIndex;
        public int direction;
        public bool isFirstTime;
    }

    NativeArray<int> Iota(int num, Allocator allocator)
    {
        var result = new NativeArray<int>(num, allocator);
        for (int i = 0; i < num; i++)
            result[i] = i;
        return result;
    }

    int FindPivotIndex(NativeArray<int> inds, int stIndex, int enIndex, int axis)
    {
        int splitPoint = FindSplitPoint(inds, stIndex, enIndex, axis);

        float3 pivot = points[inds[splitPoint]];
        SwapElements(inds, stIndex, splitPoint);

        int currPt = stIndex + 1;
        int endPt = enIndex;

        while (currPt <= endPt)
        {
            float3 curr = points[inds[currPt]];
            if (curr[axis] > pivot[axis])
            {
                SwapElements(inds, currPt, endPt);
                endPt--;
            }
            else
            {
                SwapElements(inds, currPt - 1, currPt);
                currPt++;
            }
        }

        return currPt - 1;
    }

    int FindSplitPoint(NativeArray<int> inds, int stIndex, int enIndex, int axis)
    {
        float a = points[inds[stIndex]][axis];
        float b = points[inds[enIndex]][axis];
        int midIndex = (stIndex + enIndex) / 2;
        float m = points[inds[midIndex]][axis];

        if (a > b)
        {
            if (m > a) return stIndex;
            if (b > m) return enIndex;
            return midIndex;
        }
        else
        {
            if (a > m) return stIndex;
            if (m > b) return enIndex;
            return midIndex;
        }
    }

    void SwapElements(NativeArray<int> inds, int a, int b)
    {
        int temp = inds[a];
        inds[a] = inds[b];
        inds[b] = temp;
    }
}

// 탐색 전용 struct (SearchNear Job에서 사용)
public struct KDTreeSearcher
{
    const int numDims = 3;

    [ReadOnly] public NativeArray<float3> points;
    [ReadOnly] public NativeArray<int> leftChilds;
    [ReadOnly] public NativeArray<int> rightChilds;
    [ReadOnly] public NativeArray<int> axies;
    [ReadOnly] public NativeArray<byte> teamMasks;
    [ReadOnly] public NativeReference<int> pivotIndexRef;

    public int FindNearest(float3 pt, int teamBit, bool excludeMode = false)
    {
        float bestSqDist = float.MaxValue;
        int bestIndex = -1;

        var stack = new NativeList<int>(Allocator.Temp);
        stack.Add(pivotIndexRef.Value);

        while (stack.Length > 0)
        {
            int pind = stack[stack.Length - 1];
            stack.RemoveAt(stack.Length - 1);

            float3 pt1 = points[pind];
            int leftChild = leftChilds[pind];
            int rightChild = rightChilds[pind];
            int ax = axies[pind];
            int currentMask = teamMasks[pind];

            float mySqDist = math.lengthsq(pt1 - pt);

            bool isTarget = excludeMode ?
                (currentMask & teamBit) == 0 :
                (currentMask & teamBit) != 0;

            if (isTarget && mySqDist < bestSqDist)
            {
                bestSqDist = mySqDist;
                bestIndex = pind;
            }

            float planeDist = pt[ax] - pt1[ax];
            int selector = planeDist <= 0 ? 0 : 1;
            int nearChild = selector == 0 ? leftChild : rightChild;
            int farChild = selector == 0 ? rightChild : leftChild;
            float sqPlaneDist = planeDist * planeDist;

            if (farChild > -1 && bestSqDist > sqPlaneDist)
                stack.Add(farChild);

            if (nearChild > -1)
                stack.Add(nearChild);
        }

        stack.Dispose();
        return bestIndex;
    }
}