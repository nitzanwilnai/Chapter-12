using UnityEngine;
using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Survivor
{
    public static class Logic
    {
        public static void AllocateGameData(GameData gameData, Balance balance)
        {
            if (gameData.EnemyPosition.IsCreated) return;

            gameData.EnemyPosition = new NativeArray<float2>(balance.MaxEnemies, Allocator.Persistent);
            gameData.EnemyType = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
            gameData.AliveEnemyIndices = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
            gameData.DeadEnemyIndices = new NativeArray<int>(balance.MaxEnemies, Allocator.Persistent);
        }

        public static void FreeGameData(GameData gameData)
        {
            if (gameData.EnemyPosition.IsCreated) gameData.EnemyPosition.Dispose();
            if (gameData.EnemyType.IsCreated) gameData.EnemyType.Dispose();
            if (gameData.AliveEnemyIndices.IsCreated) gameData.AliveEnemyIndices.Dispose();
            if (gameData.DeadEnemyIndices.IsCreated) gameData.DeadEnemyIndices.Dispose();
        }

        public static void Init(MetaData metaData)
        {
            metaData.MenuState = MENU_STATE.NONE;
        }

        public static void StartGame(GameData gameData, Balance balance)
        {
            gameData.InGame = true;

            gameData.Rng = new Unity.Mathematics.Random((uint)DateTime.Now.Ticks | 1u);

            gameData.GameTime = 0.0f;
            gameData.SpawnTime = 0.0f;

            gameData.PlayerDirection = float2.zero;

            for (int i = 0; i < balance.MaxEnemies; i++)
                gameData.DeadEnemyIndices[i] = balance.MaxEnemies - 1 - i;
            gameData.DeadEnemyCount = balance.MaxEnemies;
            gameData.AliveEnemyCount = 0;
        }

        static bool canSpawnEnemy(GameData gameData, Balance balance)
        {
            return gameData.DeadEnemyCount > 0 && gameData.AliveEnemyCount < balance.MaxEnemies;
        }

        static void spawnEnemy(GameData gameData, Balance balance, NativeList<int> addedEnemyIndices)
        {
            int enemyIndex = gameData.DeadEnemyIndices[--gameData.DeadEnemyCount];
            gameData.AliveEnemyIndices[gameData.AliveEnemyCount++] = enemyIndex;
            addedEnemyIndices.Add(enemyIndex);

            float2 direction = gameData.PlayerDirection;
            float angle = gameData.Rng.NextFloat() * 180.0f - 90.0f;
            if (math.lengthsq(direction) == 0.0f)
            {
                direction = new float2(0.0f, 1.0f);
                angle = gameData.Rng.NextFloat() * 360.0f;
            }
            direction = RotateVector(direction, angle);
            gameData.EnemyPosition[enemyIndex] = math.normalizesafe(direction) * balance.SpawnRadius;
            gameData.EnemyType[enemyIndex] = getRandomEnemyTypeByWeight(gameData, balance);
        }

        private static int getRandomEnemyTypeByWeight(GameData gameData, Balance balance)
        {
            int enemyType = 0;
            int totalWeight = 0;
            for (int spawnIdx = 0; spawnIdx < balance.SpawnDataID.Length; spawnIdx++)
            {
                totalWeight += balance.SpawnDataWeight[spawnIdx];
            }

            int randomWeight = gameData.Rng.NextInt(0, totalWeight);

            totalWeight = 0;
            for (int spawnIdx = 0; spawnIdx < balance.SpawnDataID.Length; spawnIdx++)
            {
                totalWeight += balance.SpawnDataWeight[spawnIdx];
                if (randomWeight < totalWeight)
                {
                    enemyType = balance.SpawnDataID[spawnIdx];
                    break;
                }
            }

            return enemyType;
        }

        private const double DegToRad = Math.PI / 180.0d;

        public static float2 RotateVector(float2 a, float degrees)
        {
            float radians = (float)(degrees * DegToRad);
            float ca = math.cos(radians);
            float sa = math.sin(radians);
            return new float2(ca * a.x - sa * a.y, sa * a.x + ca * a.y);
        }

        public static void Tick(
            MetaData metaData,
            GameData gameData,
            Balance balance,
            float dt,
            out bool gameOver,
            NativeList<int> addedEnemyIndices,
            NativeList<int> removedEnemyIndices
            )
        {
            gameData.GameTime += dt;

            gameData.SpawnTime += dt;
            if (gameData.SpawnTime >= balance.SpawnTime)
            {
                gameData.SpawnTime -= balance.SpawnTime;
                if (canSpawnEnemy(gameData, balance))
                    spawnEnemy(gameData, balance, addedEnemyIndices);
            }

            new MoveEnemiesJob
            {
                AliveEnemyIndices = gameData.AliveEnemyIndices,
                EnemyType = gameData.EnemyType,
                EnemyVelocity = balance.EnemyVelocity,
                EnemyPosition = gameData.EnemyPosition,
                Dt = dt,
            }.Schedule(gameData.AliveEnemyCount, 32).Complete();

            new CheckOutOfBoundsJob
            {
                AliveEnemyIndices = gameData.AliveEnemyIndices,
                EnemyPosition = gameData.EnemyPosition,
                AliveEnemyCount = gameData.AliveEnemyCount,
                DistanceSqrLimit = balance.SpawnRadius * balance.SpawnRadius * 1.1f,
                RemovedEnemies = removedEnemyIndices,
            }.Schedule().Complete();

            // Main-thread: fold the removed indices into AliveEnemyIndices / DeadEnemyIndices.
            for (int ri = 0; ri < removedEnemyIndices.Length; ri++)
            {
                int enemyIndex = removedEnemyIndices[ri];
                Debug.LogFormat("Removing enemy {0}", enemyIndex);
                int count = 0;
                for (int i = 0; i < gameData.AliveEnemyCount; i++)
                    if (gameData.AliveEnemyIndices[i] != enemyIndex)
                        gameData.AliveEnemyIndices[count++] = gameData.AliveEnemyIndices[i];
                gameData.AliveEnemyCount = count;
                gameData.DeadEnemyIndices[gameData.DeadEnemyCount++] = enemyIndex;
            }

            doEemyToEnemyCollision(gameData, balance);

            movePlayer(gameData, balance, dt);

            gameOver = false;
        }

        static void doEemyToEnemyCollision(GameData gameData, Balance balance)
        {
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex1 = gameData.AliveEnemyIndices[i];
                float radius1 = balance.EnemyRadius[gameData.EnemyType[enemyIndex1]];
                for (int j = i + 1; j < gameData.AliveEnemyCount; j++)
                {
                    int enemyIndex2 = gameData.AliveEnemyIndices[j];
                    float2 diff = gameData.EnemyPosition[enemyIndex1] - gameData.EnemyPosition[enemyIndex2];
                    float distance = radius1 + balance.EnemyRadius[gameData.EnemyType[enemyIndex2]];
                    float distanceSqr = distance * distance;
                    if (math.lengthsq(diff) <= distanceSqr)
                    {
                        float2 diffNormalized = math.normalizesafe(diff);
                        float2 midPoint = (gameData.EnemyPosition[enemyIndex1] + gameData.EnemyPosition[enemyIndex2]) / 2.0f;
                        float halfTotalRadius = (balance.EnemyRadius[gameData.EnemyType[enemyIndex1]] + balance.EnemyRadius[gameData.EnemyType[enemyIndex2]]) / 2.0f;
                        gameData.EnemyPosition[enemyIndex1] = midPoint + diffNormalized * halfTotalRadius;
                        gameData.EnemyPosition[enemyIndex2] = midPoint - diffNormalized * halfTotalRadius;
                    }
                }
            }
        }

        static void movePlayer(GameData gameData, Balance balance, float dt)
        {
            float2 playerPosition = gameData.PlayerDirection * balance.PlayerVelocity * dt;
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[i];
                gameData.EnemyPosition[enemyIndex] -= playerPosition;
            }
        }

        public static void MouseMove(GameData gameData, Vector2 mouseDownPos, Vector2 mouseCurrentPos)
        {
            Vector2 dir = (mouseCurrentPos - mouseDownPos).normalized;
            gameData.PlayerDirection = new float2(dir.x, dir.y);
        }

        public static void MouseUp(GameData gameData)
        {
            gameData.PlayerDirection = float2.zero;
        }

        static bool checkGameOver(MetaData metaData, GameData gameData, Balance balance)
        {
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[i];
                if (math.length(gameData.EnemyPosition[enemyIndex]) < balance.PlayerRadius)
                {
                    if (gameData.GameTime > metaData.BestTime)
                        metaData.BestTime = gameData.GameTime;

                    gameData.InGame = false;
                    return true;
                }
            }
            return false;
        }

        public static void SetMenuState(MetaData metaData, MENU_STATE newMenuState)
        {
            metaData.MenuState = newMenuState;
        }
    }
}
