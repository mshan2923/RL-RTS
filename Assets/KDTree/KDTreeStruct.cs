using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public struct KDTreeStruct
{
    public int pivotIndex;
    const int numDims = 3;

    [ReadOnly] NativeArray<float3> points;
    [ReadOnly] NativeArray<int> leftChilds;
    [ReadOnly] NativeArray<int> rightChilds;
    [ReadOnly] NativeArray<int> axies;
    [ReadOnly] NativeArray<byte> teamMasks;

    public void MakeFromPoints(NativeArray<float3> points1, NativeArray<byte> masks1, Allocator allocator = Allocator.Persistent)
    {
        NativeArray<int> indices = Iota(points1.Length, Allocator.Temp);

        points = new NativeArray<float3>(points1, allocator);
        teamMasks = new NativeArray<byte>(masks1, allocator);
        leftChilds = new NativeArray<int>(points1.Length, allocator);
        rightChilds = new NativeArray<int>(points1.Length, allocator);
        axies = new NativeArray<int>(points1.Length, allocator);

        for (int i = 0; i < points1.Length; i++)
        {
            leftChilds[i] = -1;
            rightChilds[i] = -1;
            axies[i] = -1;
        }

        // 빌드 스택
        var stack = new NativeList<BuildFrame>(Allocator.Temp);
        stack.Add(new BuildFrame
        {
            depth = 0,
            stIndex = 0,
            enIndex = points.Length - 1,
            parentPivotIndex = -1,
            direction = -1,
            isFirstTime = true
        });

        while (stack.Length > 0)
        {
            // 스택에서 꺼내기
            var frame = stack[stack.Length - 1];
            stack.RemoveAt(stack.Length - 1);

            int axis1 = frame.depth % numDims;
            int splitPoint = FindPivotIndex(indices, frame.stIndex, frame.enIndex, axis1);

            if (frame.isFirstTime)
                pivotIndex = indices[splitPoint];

            int pivotIndex1 = indices[splitPoint];
            axies[pivotIndex1] = axis1;

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

    void SwapElements(NativeArray<int> arr, int a, int b)
    {
        int temp = arr[a];
        arr[a] = arr[b];
        arr[b] = temp;
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

    public int FindPivotIndex(NativeArray<int> inds, int stIndex, int enIndex, int axis)
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

    public NativeArray<int> Iota(int num, Allocator allocator)
    {
        NativeArray<int> result = new NativeArray<int>(num, allocator);
        for (int i = 0; i < num; i++)
            result[i] = i;
        return result;
    }

    public int FindNearest(float3 pt, int teamBit, bool Ally = false)
    {
        float bestSqDist = float.MaxValue;
        int bestIndex = -1;

        var stack = new NativeList<int>(Allocator.Temp);
        stack.Add(pivotIndex);

        while (stack.Length > 0)
        {
            int pind = stack[stack.Length - 1];
            stack.RemoveAt(stack.Length - 1);

            float3 pt1 = points[pind];
            int leftChild = leftChilds[pind];
            int rightChild = rightChilds[pind];
            int ax = axies[pind];
            int currentMask = teamMasks[pind];

            float3 relative = pt1 - pt;
            float mySqDist = math.lengthsq(relative);

            bool isTarget = Ally ?
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

            // 먼 쪽 먼저 스택에 (나중에 처리)
            if (farChild > -1 && bestSqDist > sqPlaneDist)
                stack.Add(farChild);

            // 가까운 쪽 나중에 스택에 (먼저 처리)
            if (nearChild > -1)
                stack.Add(nearChild);
        }

        stack.Dispose();
        return bestIndex;
    }
    public void UpdateMasks(NativeArray<byte> newMasks)
    {
        for (int i = 0; i < newMasks.Length; i++)
            teamMasks[i] = newMasks[i];
    }

    public void DisposeArrays()
    {
        points.Dispose();
        teamMasks.Dispose();
        leftChilds.Dispose();
        rightChilds.Dispose();
        axies.Dispose();
    }

    public void DisposeArrays(JobHandle handle)
    {
        points.Dispose(handle);
        teamMasks.Dispose(handle);
        leftChilds.Dispose(handle);
        rightChilds.Dispose(handle);
        axies.Dispose(handle);
    }
}