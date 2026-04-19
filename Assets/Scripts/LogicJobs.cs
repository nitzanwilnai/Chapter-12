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
}
