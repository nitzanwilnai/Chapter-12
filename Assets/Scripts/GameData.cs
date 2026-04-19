using Unity.Collections;
using Unity.Mathematics;

namespace Survivor
{
    public class GameData
    {
        public bool InGame;

        public NativeArray<int> AliveEnemyIndices;
        public int AliveEnemyCount;
        public NativeArray<int> DeadEnemyIndices;
        public int DeadEnemyCount;

        public float SpawnTime;

        public NativeArray<float2> EnemyPosition;
        public NativeArray<int> EnemyType;

        public float2 PlayerDirection;

        public float GameTime;

        public Unity.Mathematics.Random Rng;
    }
}
