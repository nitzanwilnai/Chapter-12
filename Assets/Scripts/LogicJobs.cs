using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Survivor
{
    [BurstCompile]
    public struct MoveEnemiesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> AliveEnemyIndices;
        [ReadOnly] public NativeArray<int> EnemyType;
        [ReadOnly] public NativeArray<float> EnemyVelocity;
        [NativeDisableParallelForRestriction] public NativeArray<float2> EnemyPosition;
        public float Dt;

        public void Execute(int i)
        {
            int enemyIndex = AliveEnemyIndices[i];
            float2 p = EnemyPosition[enemyIndex];
            float2 dir = -math.normalizesafe(p);
            EnemyPosition[enemyIndex] = p + dir * EnemyVelocity[EnemyType[enemyIndex]] * Dt;
        }
    }

    [BurstCompile]
    public struct CheckOutOfBoundsJob : IJob
    {
        [ReadOnly] public NativeArray<int> AliveEnemyIndices;
        [ReadOnly] public NativeArray<float2> EnemyPosition;
        public int AliveEnemyCount;
        public float DistanceSqrLimit;
        public NativeList<int> RemovedEnemies;

        public void Execute()
        {
            for (int i = 0; i < AliveEnemyCount; i++)
            {
                int idx = AliveEnemyIndices[i];
                if (math.lengthsq(EnemyPosition[idx]) > DistanceSqrLimit)
                    RemovedEnemies.Add(idx);
            }
        }
    }

    [BurstCompile]
    public struct ComputeCollisionDisplacementJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> AliveEnemyIndices;
        [ReadOnly] public NativeArray<float2> EnemyPosition;
        [ReadOnly] public NativeArray<int> EnemyType;
        [ReadOnly] public NativeArray<float> EnemyRadius;
        public int AliveEnemyCount;
        public NativeArray<float2> Displacement;

        public void Execute(int i)
        {
            int idx_i = AliveEnemyIndices[i];
            float2 p_i = EnemyPosition[idx_i];
            float r_i = EnemyRadius[EnemyType[idx_i]];
            float2 accum = float2.zero;

            for (int j = 0; j < AliveEnemyCount; j++)
            {
                if (j == i) continue;
                int idx_j = AliveEnemyIndices[j];
                float2 p_j = EnemyPosition[idx_j];
                float r_j = EnemyRadius[EnemyType[idx_j]];
                float2 diff = p_i - p_j;
                float totalR = r_i + r_j;
                if (math.lengthsq(diff) <= totalR * totalR)
                {
                    float len = math.length(diff);
                    float2 dir = len > 0f ? diff / len : new float2(1f, 0f);
                    float overlap = totalR - len;
                    accum += dir * overlap * 0.5f;
                }
            }

            Displacement[i] = accum;
        }
    }

    [BurstCompile]
    public struct ApplyCollisionDisplacementJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> AliveEnemyIndices;
        [ReadOnly] public NativeArray<float2> Displacement;
        [NativeDisableParallelForRestriction] public NativeArray<float2> EnemyPosition;

        public void Execute(int i)
        {
            EnemyPosition[AliveEnemyIndices[i]] += Displacement[i];
        }
    }

    [BurstCompile]
    public struct MovePlayerJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> AliveEnemyIndices;
        [NativeDisableParallelForRestriction] public NativeArray<float2> EnemyPosition;
        public float2 PlayerOffset;

        public void Execute(int i)
        {
            EnemyPosition[AliveEnemyIndices[i]] -= PlayerOffset;
        }
    }
}
