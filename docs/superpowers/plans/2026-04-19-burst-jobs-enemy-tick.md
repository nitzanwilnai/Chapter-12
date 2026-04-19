# Burst + Jobs Enemy Tick Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the four per-frame gameplay transformations in `Logic.Tick` (`movePlayer`, `moveEnemies`, `doEemyToEnemyCollision`, `checkEnemyOutOfBounds`) into `[BurstCompile]` jobs operating on `NativeArray<T>` data owned by `GameData` and `Balance`.

**Architecture:** `Logic.cs` stays the orchestrator; jobs live in a new `LogicJobs.cs`. `GameData` and `Balance` swap managed `Vector2[]` / `int[]` / `float[]` to `NativeArray<float2>` / `NativeArray<int>` / `NativeArray<float>` with `Allocator.Persistent` lifetimes tied to `Game.Start` / `Game.OnDestroy`. `Board.Tick` passes per-frame `NativeList<int>` scratch buffers (`Allocator.TempJob`) for added/removed indices. The job graph runs `MoveEnemies → CheckOutOfBounds → ComputeCollisionDisplacement → ApplyCollisionDisplacement → MovePlayer` and completes once per tick.

**Tech Stack:** Unity 2022.3.62f2, C#, `com.unity.burst`, `com.unity.collections`, `com.unity.mathematics`.

**Verification model:** This is a Unity project with no unit-test suite. Each task ends with opening Unity, confirming the Console has no compile errors, and (for behavior-changing tasks) a brief Play-mode smoke test. Commits follow.

**Spec:** `docs/superpowers/specs/2026-04-18-burst-jobs-enemy-tick-design.md`

---

## Task 1: Add Burst / Collections / Mathematics packages

**Files:**
- Modify: `Packages/manifest.json`

- [ ] **Step 1: Add three package entries to the `dependencies` object**

Open `Packages/manifest.json` and add these three lines alongside the existing `com.unity.*` entries (order within the object does not matter — alphabetic by convention):

```json
"com.unity.burst": "1.8.17",
"com.unity.collections": "1.5.1",
"com.unity.mathematics": "1.3.2",
```

Full example of the top of the `dependencies` object after the change:

```json
{
  "dependencies": {
    "com.unity.burst": "1.8.17",
    "com.unity.collab-proxy": "2.7.1",
    "com.unity.collections": "1.5.1",
    "com.unity.feature.2d": "2.0.1",
    "com.unity.ide.rider": "3.0.36",
    "com.unity.ide.visualstudio": "2.0.22",
    "com.unity.mathematics": "1.3.2",
    ...
  }
}
```

- [ ] **Step 2: Let Unity resolve the packages**

Open the project in Unity. Wait for the Package Manager to resolve. Verify `Packages/packages-lock.json` now contains entries for `com.unity.burst`, `com.unity.collections`, `com.unity.mathematics`.

Expected: no red Console errors. Yellow compile warnings from unrelated sources are OK.

- [ ] **Step 3: Commit**

```bash
git add Packages/manifest.json Packages/packages-lock.json
git commit -m "deps: add Burst, Collections, Mathematics packages"
```

---

## Task 2: Migrate `Balance.cs` to `NativeArray`

**Files:**
- Modify: `Assets/Scripts/Balance.cs`

- [ ] **Step 1: Swap the four field types and add `Free()`**

Replace the full contents of `Assets/Scripts/Balance.cs` with:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Unity.Collections;

namespace Survivor
{
    [Serializable]
    public class Balance
    {
        public int MaxEnemies;
        public int NumEnemyTypes;
        public int[] EnemyIDs;
        public string[] EnemyName;
        public string[] EnemyPrefabName;
        public NativeArray<float> EnemyVelocity;
        public NativeArray<float> EnemyRadius;

        public Dictionary<string, int> EnemyNameToID;
        public string[] EnemyIDToName;

        public NativeArray<int> SpawnDataID;
        public NativeArray<int> SpawnDataWeight;

        public float SpawnRadius;
        public float PlayerVelocity;
        public float PlayerRadius;
        public float SpawnTime;

        public void LoadBalance()
        {
            TextAsset asset = Resources.Load("balance") as TextAsset;
            LoadBalance(asset.bytes);
        }

        public void LoadBalance(byte[] array)
        {
            Free();

            Stream s = new MemoryStream(array);
            using (BinaryReader br = new BinaryReader(s))
            {
                int version = br.ReadInt32();

                MaxEnemies = br.ReadInt32();
                SpawnRadius = br.ReadSingle();
                PlayerVelocity = br.ReadSingle();
                PlayerRadius = br.ReadSingle();
                SpawnTime = br.ReadSingle();

                int numSpawnData = br.ReadInt32();
                SpawnDataID = new NativeArray<int>(numSpawnData, Allocator.Persistent);
                SpawnDataWeight = new NativeArray<int>(numSpawnData, Allocator.Persistent);
                for (int i = 0; i < numSpawnData; i++)
                {
                    SpawnDataID[i] = br.ReadInt32();
                    SpawnDataWeight[i] = br.ReadInt32();
                }

                NumEnemyTypes = br.ReadInt32();
                EnemyIDs = new int[NumEnemyTypes];
                EnemyName = new string[NumEnemyTypes];
                EnemyPrefabName = new string[NumEnemyTypes];
                EnemyVelocity = new NativeArray<float>(NumEnemyTypes, Allocator.Persistent);
                EnemyRadius = new NativeArray<float>(NumEnemyTypes, Allocator.Persistent);
                EnemyNameToID = new Dictionary<string, int>(NumEnemyTypes);
                EnemyIDToName = new string[NumEnemyTypes];
                for (int enemyIdx = 0; enemyIdx < NumEnemyTypes; enemyIdx++)
                {
                    EnemyIDs[enemyIdx] = br.ReadInt32();
                    EnemyName[enemyIdx] = br.ReadString();
                    EnemyPrefabName[enemyIdx] = br.ReadString();

                    EnemyNameToID.Add(EnemyName[enemyIdx], EnemyIDs[enemyIdx]);
                    EnemyIDToName[EnemyIDs[enemyIdx]] = EnemyName[enemyIdx];

                    EnemyVelocity[enemyIdx] = br.ReadSingle();
                    EnemyRadius[enemyIdx] = br.ReadSingle();
                }

                int magic = br.ReadInt32();
                Debug.Log(magic);
            }
        }

        public void Free()
        {
            if (EnemyVelocity.IsCreated) EnemyVelocity.Dispose();
            if (EnemyRadius.IsCreated) EnemyRadius.Dispose();
            if (SpawnDataID.IsCreated) SpawnDataID.Dispose();
            if (SpawnDataWeight.IsCreated) SpawnDataWeight.Dispose();
        }
    }
}
```

- [ ] **Step 2: Compile check**

Switch focus to Unity. Wait for recompile. Expected: Console shows no errors. `Logic.cs` reads `balance.EnemyVelocity[i]`, `balance.EnemyRadius[i]`, `balance.SpawnDataID[i]`, `balance.SpawnDataWeight[i]` — all work identically on `NativeArray<T>` (same indexer syntax).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Balance.cs
git commit -m "refactor: move Balance per-enemy arrays to NativeArray"
```

---

## Task 3: Migrate `GameData` arrays to `NativeArray`, `PlayerDirection` to `float2`, add `Rng`

**Files:**
- Modify: `Assets/Scripts/GameData.cs`
- Modify: `Assets/Scripts/Logic.cs`
- Modify: `Assets/Scripts/GameDataIO.cs`
- Modify: `Assets/Scripts/Board.cs`

- [ ] **Step 1: Rewrite `GameData.cs`**

Replace the full contents of `Assets/Scripts/GameData.cs` with:

```csharp
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
```

- [ ] **Step 2: Rewrite `Logic.cs`**

Replace the full contents of `Assets/Scripts/Logic.cs` with (this converts all `Vector2` usages to `float2`, swaps array allocation to `NativeArray`, adds `FreeGameData`, and deletes the unused `removeEnemyArrayCopy` which would not compile against `NativeArray`):

```csharp
using UnityEngine;
using System;
using Unity.Collections;
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

        static void spawnEnemy(GameData gameData, Balance balance, Span<int> addedEnemyIndices, ref int addedEnemyCount)
        {
            int enemyIndex = gameData.DeadEnemyIndices[--gameData.DeadEnemyCount];
            gameData.AliveEnemyIndices[gameData.AliveEnemyCount++] = enemyIndex;
            addedEnemyIndices[addedEnemyCount++] = enemyIndex;

            float2 direction = gameData.PlayerDirection;
            float angle = UnityEngine.Random.value * 180.0f - 90.0f;
            if (math.lengthsq(direction) == 0.0f)
            {
                direction = new float2(0.0f, 1.0f);
                angle = UnityEngine.Random.value * 360.0f;
            }
            direction = RotateVector(direction, angle);
            gameData.EnemyPosition[enemyIndex] = math.normalizesafe(direction) * balance.SpawnRadius;
            gameData.EnemyType[enemyIndex] = getRandomEnemyTypeByWeight(balance);
        }

        private static int getRandomEnemyTypeByWeight(Balance balance)
        {
            int enemyType = 0;
            int totalWeight = 0;
            for (int spawnIdx = 0; spawnIdx < balance.SpawnDataID.Length; spawnIdx++)
            {
                totalWeight += balance.SpawnDataWeight[spawnIdx];
            }

            int randomWeight = UnityEngine.Random.Range(0, totalWeight);

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

        static void removeEnemy(GameData gameData, int enemyIndex, Span<int> removedEnemyIndices, ref int removedEnemyCount)
        {
            Debug.LogFormat("Removing enemy {0}", enemyIndex);
            int count = 0;
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
                if (gameData.AliveEnemyIndices[i] != enemyIndex)
                    gameData.AliveEnemyIndices[count++] = gameData.AliveEnemyIndices[i];
            gameData.AliveEnemyCount = count;

            gameData.DeadEnemyIndices[gameData.DeadEnemyCount++] = enemyIndex;
            removedEnemyIndices[removedEnemyCount++] = enemyIndex;
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
            Span<int> addedEnemyIndices,
            ref int addedEnemyCount,
            Span<int> removedEnemyIndices,
            ref int removedEnemyCount
            )
        {
            gameData.GameTime += dt;

            gameData.SpawnTime += dt;
            if (gameData.SpawnTime >= balance.SpawnTime)
            {
                gameData.SpawnTime -= balance.SpawnTime;
                if (canSpawnEnemy(gameData, balance))
                    spawnEnemy(gameData, balance, addedEnemyIndices, ref addedEnemyCount);
            }

            moveEnemies(gameData, balance, dt);

            checkEnemyOutOfBounds(gameData, balance, removedEnemyIndices, ref removedEnemyCount);

            doEemyToEnemyCollision(gameData, balance);

            movePlayer(gameData, balance, dt);

            gameOver = false;
        }

        static void moveEnemies(GameData gameData, Balance balance, float dt)
        {
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[i];
                float2 dir = -math.normalizesafe(gameData.EnemyPosition[enemyIndex]);
                int enemyType = gameData.EnemyType[enemyIndex];
                gameData.EnemyPosition[enemyIndex] = gameData.EnemyPosition[enemyIndex] + dir * balance.EnemyVelocity[enemyType] * dt;
            }
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

        static void checkEnemyOutOfBounds(GameData gameData, Balance balance, Span<int> removedEnemyIndices, ref int removedEnemyCount)
        {
            float distanceSqr = balance.SpawnRadius * balance.SpawnRadius * 1.1f;
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[i];
                if (math.lengthsq(gameData.EnemyPosition[enemyIndex]) > distanceSqr)
                    removeEnemy(gameData, enemyIndex, removedEnemyIndices, ref removedEnemyCount);
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
```

Note: the RotateVector rewrite fixes a latent bug — the original read the updated `a.x` when computing `a.y`. The float2 version computes both components from the input values.

- [ ] **Step 3: Update `GameDataIO.cs` Save/Load for `NativeArray<float2>`**

Replace the full contents of `Assets/Scripts/GameDataIO.cs` with (unchanged version number, unchanged format — just the type plumbing; the RNG-state write comes in Task 4):

```csharp
using System;
using System.IO;
using UnityEngine;
using Unity.Mathematics;

namespace Survivor
{
    public static class GameDataIO
    {
        public static void Save(GameData gameData, Balance balance)
        {
            Debug.LogFormat("SaveGame()");

            if (!Directory.Exists(Application.persistentDataPath + "/DODSurvivor"))
                Directory.CreateDirectory(Application.persistentDataPath + "/DODSurvivor");

            string fileName = Application.persistentDataPath + "/DODSurvivor/gamedata.dat";
            using (FileStream fs = File.Create(fileName))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                int version = 1;
                bw.Write(version);

                bw.Write(gameData.InGame);

                bw.Write(gameData.AliveEnemyCount);
                for (int i = 0; i < gameData.AliveEnemyCount; i++)
                    bw.Write(gameData.AliveEnemyIndices[i]);

                bw.Write(gameData.DeadEnemyCount);
                for (int i = 0; i < gameData.DeadEnemyCount; i++)
                    bw.Write(gameData.DeadEnemyIndices[i]);

                bw.Write(balance.MaxEnemies);

                for (int i = 0; i < balance.MaxEnemies; i++)
                {
                    bw.Write(gameData.EnemyPosition[i].x);
                    bw.Write(gameData.EnemyPosition[i].y);
                }

                for (int i = 0; i < balance.MaxEnemies; i++)
                    bw.Write(gameData.EnemyType[i]);

                bw.Write(gameData.PlayerDirection.x);
                bw.Write(gameData.PlayerDirection.y);

                bw.Write(gameData.GameTime);

                bw.Write(balance.NumEnemyTypes);
                for (int enemyType = 0; enemyType < balance.NumEnemyTypes; enemyType++)
                    bw.Write(balance.EnemyIDToName[enemyType]);
            }
        }

        public static void Load(GameData gameData, Balance balance)
        {
            string fileName = Application.persistentDataPath + "/DODSurvivor/gamedata.dat";
            if (File.Exists(fileName))
            {
                using (FileStream stream = File.Open(fileName, FileMode.Open))
                using (BinaryReader br = new BinaryReader(stream))
                {
                    int version = br.ReadInt32();

                    gameData.InGame = br.ReadBoolean();

                    gameData.AliveEnemyCount = br.ReadInt32();
                    for (int i = 0; i < gameData.AliveEnemyCount; i++)
                        gameData.AliveEnemyIndices[i] = br.ReadInt32();

                    gameData.DeadEnemyCount = br.ReadInt32();
                    for (int i = 0; i < gameData.DeadEnemyCount; i++)
                        gameData.DeadEnemyIndices[i] = br.ReadInt32();

                    int numEnemies = br.ReadInt32();
                    for (int i = 0; i < numEnemies; i++)
                    {
                        float x = br.ReadSingle();
                        float y = br.ReadSingle();
                        gameData.EnemyPosition[i] = new float2(x, y);
                    }

                    for (int i = 0; i < numEnemies; i++)
                        gameData.EnemyType[i] = br.ReadInt32();

                    float px = br.ReadSingle();
                    float py = br.ReadSingle();
                    gameData.PlayerDirection = new float2(px, py);

                    gameData.GameTime = br.ReadSingle();

                    int numEnemyTypes = br.ReadInt32();
                    for (int enemyType = 0; enemyType < numEnemyTypes; enemyType++)
                    {
                        string enemyIdentifier = br.ReadString();
                        int newType = balance.EnemyNameToID[enemyIdentifier];
                        if (newType != enemyType)
                        {
                            for (int i = 0; i < numEnemies; i++)
                            {
                                if (gameData.EnemyType[i] == enemyType)
                                {
                                    gameData.EnemyType[i] = newType;
                                }
                            }
                        }
                    }
                }
            }
        }

        public static bool SaveGameExists()
        {
            bool inGame = false;
            string fileName = Application.persistentDataPath + "/DODSurvivor/gamedata.dat";
            if (File.Exists(fileName))
            {
                using (FileStream stream = File.Open(fileName, FileMode.Open))
                using (BinaryReader br = new BinaryReader(stream))
                {
                    int version = br.ReadInt32();

                    inGame = br.ReadBoolean();
                }
            }
            return inGame;
        }
    }
}
```

- [ ] **Step 4: Update `Board.cs` transform writeback for `float2`**

In `Assets/Scripts/Board.cs`, locate the per-enemy transform loop inside `Board.Tick` (currently around lines 161–166):

```csharp
for (int i = 0; i < gameData.AliveEnemyCount; i++)
{
    int enemyIndex = gameData.AliveEnemyIndices[i];
    int poolIndex = m_enemyToPoolIndex[enemyIndex];
    m_enemyPool[poolIndex].transform.localPosition = gameData.EnemyPosition[enemyIndex];
}
```

Replace it with:

```csharp
for (int i = 0; i < gameData.AliveEnemyCount; i++)
{
    int enemyIndex = gameData.AliveEnemyIndices[i];
    int poolIndex = m_enemyToPoolIndex[enemyIndex];
    float2 pos = gameData.EnemyPosition[enemyIndex];
    m_enemyPool[poolIndex].transform.localPosition = new Vector3(pos.x, pos.y, 0f);
}
```

Add `using Unity.Mathematics;` to the top of `Board.cs` if it is not already there.

- [ ] **Step 5: Compile and play-mode smoke test**

Switch to Unity, wait for recompile. Expected: no Console errors.

Enter Play mode → Main Menu → Start → confirm enemies spawn, move toward player, collide, and leave the ring. Exit Play. Expected behavior unchanged from before this task.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/GameData.cs Assets/Scripts/Logic.cs Assets/Scripts/GameDataIO.cs Assets/Scripts/Board.cs
git commit -m "refactor: move GameData to NativeArray + float2, add FreeGameData"
```

---

## Task 4: Replace `UnityEngine.Random` with `Unity.Mathematics.Random`, bump save version

**Files:**
- Modify: `Assets/Scripts/Logic.cs`
- Modify: `Assets/Scripts/GameDataIO.cs`

- [ ] **Step 1: Seed the RNG in `Logic.StartGame`**

In `Assets/Scripts/Logic.cs`, inside `StartGame`, add one line right after `gameData.InGame = true;`:

```csharp
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
```

- [ ] **Step 2: Replace `UnityEngine.Random` calls in `spawnEnemy`**

In `Assets/Scripts/Logic.cs`, in `spawnEnemy`, replace the two `UnityEngine.Random.value` calls. The full updated method:

```csharp
static void spawnEnemy(GameData gameData, Balance balance, Span<int> addedEnemyIndices, ref int addedEnemyCount)
{
    int enemyIndex = gameData.DeadEnemyIndices[--gameData.DeadEnemyCount];
    gameData.AliveEnemyIndices[gameData.AliveEnemyCount++] = enemyIndex;
    addedEnemyIndices[addedEnemyCount++] = enemyIndex;

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
```

Note the `getRandomEnemyTypeByWeight` call signature change — it now takes `gameData` as well.

- [ ] **Step 3: Update `getRandomEnemyTypeByWeight` signature and RNG call**

Replace the `getRandomEnemyTypeByWeight` method in `Logic.cs` with:

```csharp
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
```

- [ ] **Step 4: Bump save version to `2` and write/read RNG state**

Replace `GameDataIO.Save` in `Assets/Scripts/GameDataIO.cs`. The `version` constant changes to `2` and one extra write appears at the end:

```csharp
public static void Save(GameData gameData, Balance balance)
{
    Debug.LogFormat("SaveGame()");

    if (!Directory.Exists(Application.persistentDataPath + "/DODSurvivor"))
        Directory.CreateDirectory(Application.persistentDataPath + "/DODSurvivor");

    string fileName = Application.persistentDataPath + "/DODSurvivor/gamedata.dat";
    using (FileStream fs = File.Create(fileName))
    using (BinaryWriter bw = new BinaryWriter(fs))
    {
        int version = 2;
        bw.Write(version);

        bw.Write(gameData.InGame);

        bw.Write(gameData.AliveEnemyCount);
        for (int i = 0; i < gameData.AliveEnemyCount; i++)
            bw.Write(gameData.AliveEnemyIndices[i]);

        bw.Write(gameData.DeadEnemyCount);
        for (int i = 0; i < gameData.DeadEnemyCount; i++)
            bw.Write(gameData.DeadEnemyIndices[i]);

        bw.Write(balance.MaxEnemies);

        for (int i = 0; i < balance.MaxEnemies; i++)
        {
            bw.Write(gameData.EnemyPosition[i].x);
            bw.Write(gameData.EnemyPosition[i].y);
        }

        for (int i = 0; i < balance.MaxEnemies; i++)
            bw.Write(gameData.EnemyType[i]);

        bw.Write(gameData.PlayerDirection.x);
        bw.Write(gameData.PlayerDirection.y);

        bw.Write(gameData.GameTime);

        bw.Write(balance.NumEnemyTypes);
        for (int enemyType = 0; enemyType < balance.NumEnemyTypes; enemyType++)
            bw.Write(balance.EnemyIDToName[enemyType]);

        bw.Write(gameData.Rng.state);
    }
}
```

- [ ] **Step 5: Update `GameDataIO.Load` with version-2 RNG read + v1 compat**

Replace the `Load` method body in `Assets/Scripts/GameDataIO.cs`. After the enemy-type remap loop, add the trailing RNG-state read guarded by version:

```csharp
public static void Load(GameData gameData, Balance balance)
{
    string fileName = Application.persistentDataPath + "/DODSurvivor/gamedata.dat";
    if (File.Exists(fileName))
    {
        using (FileStream stream = File.Open(fileName, FileMode.Open))
        using (BinaryReader br = new BinaryReader(stream))
        {
            int version = br.ReadInt32();

            gameData.InGame = br.ReadBoolean();

            gameData.AliveEnemyCount = br.ReadInt32();
            for (int i = 0; i < gameData.AliveEnemyCount; i++)
                gameData.AliveEnemyIndices[i] = br.ReadInt32();

            gameData.DeadEnemyCount = br.ReadInt32();
            for (int i = 0; i < gameData.DeadEnemyCount; i++)
                gameData.DeadEnemyIndices[i] = br.ReadInt32();

            int numEnemies = br.ReadInt32();
            for (int i = 0; i < numEnemies; i++)
            {
                float x = br.ReadSingle();
                float y = br.ReadSingle();
                gameData.EnemyPosition[i] = new float2(x, y);
            }

            for (int i = 0; i < numEnemies; i++)
                gameData.EnemyType[i] = br.ReadInt32();

            float px = br.ReadSingle();
            float py = br.ReadSingle();
            gameData.PlayerDirection = new float2(px, py);

            gameData.GameTime = br.ReadSingle();

            int numEnemyTypes = br.ReadInt32();
            for (int enemyType = 0; enemyType < numEnemyTypes; enemyType++)
            {
                string enemyIdentifier = br.ReadString();
                int newType = balance.EnemyNameToID[enemyIdentifier];
                if (newType != enemyType)
                {
                    for (int i = 0; i < numEnemies; i++)
                    {
                        if (gameData.EnemyType[i] == enemyType)
                        {
                            gameData.EnemyType[i] = newType;
                        }
                    }
                }
            }

            if (version >= 2)
            {
                uint rngState = br.ReadUInt32();
                gameData.Rng = new Unity.Mathematics.Random { state = rngState };
            }
        }
    }
}
```

- [ ] **Step 6: Compile and play-mode test**

Switch to Unity, wait for recompile. Expected: no Console errors.

Enter Play, start a game, confirm enemies still spawn normally (RNG works). Pause to write the save file. Exit Play, re-enter, press Continue — enemies restore. Expected: Console shows `SaveGame()` then loads cleanly.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Logic.cs Assets/Scripts/GameDataIO.cs
git commit -m "refactor: use Unity.Mathematics.Random, persist RNG state in save v2"
```

---

## Task 5: Add `Game.OnDestroy` to dispose native arrays

**Files:**
- Modify: `Assets/Scripts/Game.cs`

- [ ] **Step 1: Add `OnDestroy` method**

Open `Assets/Scripts/Game.cs`. Add the following method immediately after `captureScreenshot` (keep the method inside the `Game` class):

```csharp
void OnDestroy()
{
    Logic.FreeGameData(m_gameData);
    m_balance.Free();
}
```

- [ ] **Step 2: Compile and leak-detection test**

Switch to Unity, wait for recompile. Expected: no Console errors.

In Unity: `Jobs > Leak Detection > Full Stack Traces`. Enter Play, start a game, exit Play. Re-enter Play, exit. Expected: no "Memory leak detected" messages in Console across Play session boundaries.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Game.cs
git commit -m "feat: dispose Balance and GameData native arrays in Game.OnDestroy"
```

---

## Task 6: Replace `Span<int>` scratch buffers with `NativeList<int>`

**Files:**
- Modify: `Assets/Scripts/Logic.cs`
- Modify: `Assets/Scripts/Board.cs`

- [ ] **Step 1: Change `Logic.spawnEnemy` and `Logic.removeEnemy` signatures**

In `Assets/Scripts/Logic.cs`, replace `spawnEnemy` with:

```csharp
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
```

Replace `removeEnemy` with:

```csharp
static void removeEnemy(GameData gameData, int enemyIndex, NativeList<int> removedEnemyIndices)
{
    Debug.LogFormat("Removing enemy {0}", enemyIndex);
    int count = 0;
    for (int i = 0; i < gameData.AliveEnemyCount; i++)
        if (gameData.AliveEnemyIndices[i] != enemyIndex)
            gameData.AliveEnemyIndices[count++] = gameData.AliveEnemyIndices[i];
    gameData.AliveEnemyCount = count;

    gameData.DeadEnemyIndices[gameData.DeadEnemyCount++] = enemyIndex;
    removedEnemyIndices.Add(enemyIndex);
}
```

- [ ] **Step 2: Change `Logic.checkEnemyOutOfBounds` signature**

Replace `checkEnemyOutOfBounds` with:

```csharp
static void checkEnemyOutOfBounds(GameData gameData, Balance balance, NativeList<int> removedEnemyIndices)
{
    float distanceSqr = balance.SpawnRadius * balance.SpawnRadius * 1.1f;
    for (int i = 0; i < gameData.AliveEnemyCount; i++)
    {
        int enemyIndex = gameData.AliveEnemyIndices[i];
        if (math.lengthsq(gameData.EnemyPosition[enemyIndex]) > distanceSqr)
            removeEnemy(gameData, enemyIndex, removedEnemyIndices);
    }
}
```

- [ ] **Step 3: Change `Logic.Tick` signature**

Replace the entire `Logic.Tick` method with:

```csharp
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

    moveEnemies(gameData, balance, dt);

    checkEnemyOutOfBounds(gameData, balance, removedEnemyIndices);

    doEemyToEnemyCollision(gameData, balance);

    movePlayer(gameData, balance, dt);

    gameOver = false;
}
```

- [ ] **Step 4: Update `Board.Tick` to allocate and pass `NativeList<int>`**

In `Assets/Scripts/Board.cs`, replace the body of `Board.Tick` (locate the `stackalloc` block). The updated method in full:

```csharp
public void Tick(float dt)
{
    handleInput();

    bool isGameOver;
    var removedEnemyIndices = new NativeList<int>(balance.MaxEnemies, Allocator.TempJob);
    var addedEnemyIndices = new NativeList<int>(balance.MaxEnemies, Allocator.TempJob);

    Logic.Tick(
        metaData,
        gameData,
        balance,
        dt,
        out isGameOver,
        addedEnemyIndices,
        removedEnemyIndices
        );

    for (int i = 0; i < addedEnemyIndices.Length; i++)
    {
        int enemyIndex = addedEnemyIndices[i];
        int enemyType = gameData.EnemyType[enemyIndex];

        int poolIndex = getFreeEnemyPoolIndex(enemyType);
        m_enemyPool[poolIndex].SetActive(true);
        m_enemyToPoolIndex[enemyIndex] = poolIndex;
    }

    for (int i = 0; i < removedEnemyIndices.Length; i++)
    {
        int enemyIndex = removedEnemyIndices[i];
        int poolIndex = m_enemyToPoolIndex[enemyIndex];
        m_enemyPool[poolIndex].SetActive(false);

        m_enemyPoolUnusedIndices[m_enemyPoolUnusedIndicesCount++] = poolIndex;
    }

    for (int i = 0; i < gameData.AliveEnemyCount; i++)
    {
        int enemyIndex = gameData.AliveEnemyIndices[i];
        int poolIndex = m_enemyToPoolIndex[enemyIndex];
        float2 pos = gameData.EnemyPosition[enemyIndex];
        m_enemyPool[poolIndex].transform.localPosition = new Vector3(pos.x, pos.y, 0f);
    }

    m_boardGUI.GameTimeText.text = CommonVisual.GetTimeElapsedString(gameData.GameTime);

    addedEnemyIndices.Dispose();
    removedEnemyIndices.Dispose();

    if (isGameOver)
        gameOver();
}
```

Add `using Unity.Collections;` to the top of `Board.cs` if not already present.

- [ ] **Step 5: Compile and play-mode test**

Switch to Unity, wait for recompile. Expected: no Console errors.

Enter Play, start a game, play for ~30 seconds. Confirm enemies spawn, move, collide, leave the ring. Exit Play. Expected: no leak warnings (Leak Detection still on from Task 5).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Logic.cs Assets/Scripts/Board.cs
git commit -m "refactor: replace Span<int> scratch with NativeList<int> (TempJob)"
```

---

## Task 7: Add `MoveEnemiesJob` and schedule it

**Files:**
- Create: `Assets/Scripts/LogicJobs.cs`
- Modify: `Assets/Scripts/Logic.cs`

- [ ] **Step 1: Create `LogicJobs.cs` with `MoveEnemiesJob`**

Create `Assets/Scripts/LogicJobs.cs`:

```csharp
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
```

The `[NativeDisableParallelForRestriction]` attribute is required because `Execute(i)` writes to `EnemyPosition[AliveEnemyIndices[i]]` rather than `EnemyPosition[i]` — the indirection looks like an aliasing risk to the safety system, but `AliveEnemyIndices` is guaranteed to contain unique values in `[0, MaxEnemies)`, so distinct `i` values always produce distinct writes.

- [ ] **Step 2: Replace the managed `moveEnemies` call in `Logic.Tick` with a Schedule+Complete**

In `Assets/Scripts/Logic.cs`, in `Tick`, replace the line `moveEnemies(gameData, balance, dt);` with:

```csharp
new MoveEnemiesJob
{
    AliveEnemyIndices = gameData.AliveEnemyIndices,
    EnemyType = gameData.EnemyType,
    EnemyVelocity = balance.EnemyVelocity,
    EnemyPosition = gameData.EnemyPosition,
    Dt = dt,
}.Schedule(gameData.AliveEnemyCount, 32).Complete();
```

Also delete the static `moveEnemies` method — it is now dead.

Add these `using` lines at the top of `Logic.cs` if not already present:

```csharp
using Unity.Jobs;
```

(The file should already have `using Unity.Collections;` and `using Unity.Mathematics;` from earlier tasks.)

- [ ] **Step 3: Compile and play-mode test**

Switch to Unity, wait for recompile. Expected: no Console errors.

Enter Play, start a game. Confirm enemies walk toward center same as before. Exit Play.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/LogicJobs.cs Assets/Scripts/Logic.cs
git commit -m "feat: jobify moveEnemies as Burst IJobParallelFor"
```

---

## Task 8: Add `CheckOutOfBoundsJob` and schedule it

**Files:**
- Modify: `Assets/Scripts/LogicJobs.cs`
- Modify: `Assets/Scripts/Logic.cs`

- [ ] **Step 1: Add `CheckOutOfBoundsJob` to `LogicJobs.cs`**

Append to `Assets/Scripts/LogicJobs.cs` inside the `Survivor` namespace:

```csharp
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
```

- [ ] **Step 2: Replace `checkEnemyOutOfBounds` call in `Logic.Tick`**

In `Assets/Scripts/Logic.cs`, in `Tick`, replace the line `checkEnemyOutOfBounds(gameData, balance, removedEnemyIndices);` with:

```csharp
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
```

Delete the static `checkEnemyOutOfBounds` and `removeEnemy` methods — they are now dead.

- [ ] **Step 3: Compile and play-mode test**

Switch to Unity, wait for recompile. Expected: no Console errors.

Enter Play, start a game, wait long enough for enemies to leave the ring. Confirm Console logs `Removing enemy N` entries and that the enemy count stays bounded. Exit Play.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/LogicJobs.cs Assets/Scripts/Logic.cs
git commit -m "feat: jobify checkEnemyOutOfBounds as Burst IJob"
```

---

## Task 9: Add two-pass collision jobs and schedule them

**Files:**
- Modify: `Assets/Scripts/LogicJobs.cs`
- Modify: `Assets/Scripts/Logic.cs`

- [ ] **Step 1: Add `ComputeCollisionDisplacementJob` and `ApplyCollisionDisplacementJob` to `LogicJobs.cs`**

Append to `Assets/Scripts/LogicJobs.cs` inside the `Survivor` namespace:

```csharp
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
```

- [ ] **Step 2: Replace `doEemyToEnemyCollision` call in `Logic.Tick`**

In `Assets/Scripts/Logic.cs`, in `Tick`, replace the line `doEemyToEnemyCollision(gameData, balance);` with:

```csharp
int aliveCount = gameData.AliveEnemyCount;
var displacement = new NativeArray<float2>(aliveCount, Allocator.TempJob);

new ComputeCollisionDisplacementJob
{
    AliveEnemyIndices = gameData.AliveEnemyIndices,
    EnemyPosition = gameData.EnemyPosition,
    EnemyType = gameData.EnemyType,
    EnemyRadius = balance.EnemyRadius,
    AliveEnemyCount = aliveCount,
    Displacement = displacement,
}.Schedule(aliveCount, 32).Complete();

new ApplyCollisionDisplacementJob
{
    AliveEnemyIndices = gameData.AliveEnemyIndices,
    Displacement = displacement,
    EnemyPosition = gameData.EnemyPosition,
}.Schedule(aliveCount, 32).Complete();

displacement.Dispose();
```

Delete the static `doEemyToEnemyCollision` method.

- [ ] **Step 3: Compile and play-mode test**

Switch to Unity, wait for recompile. Expected: no Console errors.

Enter Play, start a game. Confirm enemies push each other apart on contact the same visible way as before. There will be a subtle difference — collisions are now symmetric and simultaneous rather than order-dependent. Exit Play.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/LogicJobs.cs Assets/Scripts/Logic.cs
git commit -m "feat: jobify enemy-enemy collision as two-pass Burst IJobParallelFor"
```

---

## Task 10: Add `MovePlayerJob` and schedule it

**Files:**
- Modify: `Assets/Scripts/LogicJobs.cs`
- Modify: `Assets/Scripts/Logic.cs`

- [ ] **Step 1: Add `MovePlayerJob` to `LogicJobs.cs`**

Append to `Assets/Scripts/LogicJobs.cs` inside the `Survivor` namespace:

```csharp
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
```

- [ ] **Step 2: Replace `movePlayer` call in `Logic.Tick`**

In `Assets/Scripts/Logic.cs`, in `Tick`, replace the line `movePlayer(gameData, balance, dt);` with:

```csharp
float2 playerOffset = gameData.PlayerDirection * balance.PlayerVelocity * dt;
new MovePlayerJob
{
    AliveEnemyIndices = gameData.AliveEnemyIndices,
    EnemyPosition = gameData.EnemyPosition,
    PlayerOffset = playerOffset,
}.Schedule(gameData.AliveEnemyCount, 32).Complete();
```

Delete the static `movePlayer` method.

- [ ] **Step 3: Compile and play-mode test**

Switch to Unity, wait for recompile. Expected: no Console errors.

Enter Play, start a game, drag to push the player around. Confirm enemy positions shift relative to the world the same way as before. Exit Play.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/LogicJobs.cs Assets/Scripts/Logic.cs
git commit -m "feat: jobify movePlayer as Burst IJobParallelFor"
```

---

## Task 11: Chain the job graph into a single `Complete()`

**Files:**
- Modify: `Assets/Scripts/Logic.cs`

- [ ] **Step 1: Rewrite `Logic.Tick` to thread one `JobHandle`**

The goal is to replace the five `Schedule(...).Complete()` calls with a single chained `JobHandle` and one final `Complete()`. Locate the current `Logic.Tick` and replace it fully with:

```csharp
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

    int aliveCount = gameData.AliveEnemyCount;
    float2 playerOffset = gameData.PlayerDirection * balance.PlayerVelocity * dt;
    float distanceSqrLimit = balance.SpawnRadius * balance.SpawnRadius * 1.1f;
    var displacement = new NativeArray<float2>(aliveCount, Allocator.TempJob);

    JobHandle h = default;

    h = new MoveEnemiesJob
    {
        AliveEnemyIndices = gameData.AliveEnemyIndices,
        EnemyType = gameData.EnemyType,
        EnemyVelocity = balance.EnemyVelocity,
        EnemyPosition = gameData.EnemyPosition,
        Dt = dt,
    }.Schedule(aliveCount, 32, h);

    h = new CheckOutOfBoundsJob
    {
        AliveEnemyIndices = gameData.AliveEnemyIndices,
        EnemyPosition = gameData.EnemyPosition,
        AliveEnemyCount = aliveCount,
        DistanceSqrLimit = distanceSqrLimit,
        RemovedEnemies = removedEnemyIndices,
    }.Schedule(h);

    h = new ComputeCollisionDisplacementJob
    {
        AliveEnemyIndices = gameData.AliveEnemyIndices,
        EnemyPosition = gameData.EnemyPosition,
        EnemyType = gameData.EnemyType,
        EnemyRadius = balance.EnemyRadius,
        AliveEnemyCount = aliveCount,
        Displacement = displacement,
    }.Schedule(aliveCount, 32, h);

    h = new ApplyCollisionDisplacementJob
    {
        AliveEnemyIndices = gameData.AliveEnemyIndices,
        Displacement = displacement,
        EnemyPosition = gameData.EnemyPosition,
    }.Schedule(aliveCount, 32, h);

    h = new MovePlayerJob
    {
        AliveEnemyIndices = gameData.AliveEnemyIndices,
        EnemyPosition = gameData.EnemyPosition,
        PlayerOffset = playerOffset,
    }.Schedule(aliveCount, 32, h);

    h.Complete();
    displacement.Dispose();

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

    gameOver = false;
}
```

Note `aliveCount` is captured once before scheduling. The removal-fold runs only after `h.Complete()`, matching the spec's single-schedule design.

- [ ] **Step 2: Compile and play-mode test**

Switch to Unity, wait for recompile. Expected: no Console errors.

Enter Play, start a game, play for ~60 seconds. Confirm: enemies spawn, move toward center, collide, leave the ring, save/resume still works. No leaks, no Burst exceptions in Console. Exit Play.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Logic.cs
git commit -m "perf: chain job graph into single Complete() per tick"
```

---

## Task 12: Verification checklist

**Files:** none — verification only.

- [ ] **Step 1: Burst Inspector**

In Unity: `Jobs > Burst > Open Inspector`. In the inspector, locate each of the five jobs by name:
- `Survivor.MoveEnemiesJob`
- `Survivor.CheckOutOfBoundsJob`
- `Survivor.ComputeCollisionDisplacementJob`
- `Survivor.ApplyCollisionDisplacementJob`
- `Survivor.MovePlayerJob`

For each, confirm: no compilation errors, no yellow warnings about managed types or uncompilable operations.

- [ ] **Step 2: Leak Detection — full session**

In Unity: `Jobs > Leak Detection > Full Stack Traces`. Enter Play, start game, play 30s, pause (triggers save), exit Play, re-enter Play, Continue, play 30s, exit Play. Expected Console across the whole flow: no "Memory leak detected" or "A Native Collection has not been disposed" entries.

- [ ] **Step 3: Save/Load round trip**

Enter Play → Main Menu → Start → play 15 seconds → Pause (writes save v2). Exit Play. Re-enter Play → Main Menu → Continue. Expected: enemy count, positions, alive/dead lists, and game time all resume where they left off.

- [ ] **Step 4: Stress test**

Open `Assets/Data/Balance.asset` in Inspector. Raise `NumEnemies` and the `SpawnData` weights so many enemies stay alive at once (e.g. `NumEnemies = 2000`, shorter `SpawnTime`). Run `DOD > Balance > Parse Local` to rebuild `balance.bytes`. Enter Play. Open Profiler (`Window > Analysis > Profiler`) → CPU Usage → look for the `Logic.Tick` / job worker threads. Expected: frame time dominated by collision (still O(n²)), but overall cost is a large fraction lower than the pre-jobs managed version at the same enemy count.

Revert the Balance asset changes after stress testing if you do not want them in the next commit.

- [ ] **Step 5: Final commit (if any tuning from stress test)**

Only if you made code changes during stress testing (e.g. changed batch size from 32). Otherwise skip.

```bash
git status   # verify clean
```

---

## Self-Review (done before delivery)

**Spec coverage:**
- Packages added (spec §Architecture / Packages): Task 1. ✓
- File layout (`LogicJobs.cs` new, `Logic.cs` orchestrator): Tasks 7–11. ✓
- GameData NativeArray fields + float2 PlayerDirection + Rng: Task 3 step 1. ✓
- Balance NativeArray fields: Task 2. ✓
- Logic.AllocateGameData / FreeGameData: Task 3 step 2. ✓
- Balance.Free: Task 2. ✓
- Game.OnDestroy: Task 5. ✓
- Four jobs — MovePlayer, MoveEnemies, two-pass collision, CheckOutOfBounds: Tasks 7–10. ✓
- Burst attribute, [ReadOnly], [NativeDisableParallelForRestriction]: in each job definition. ✓
- Tick orchestration with one Complete(): Task 11. ✓
- Tick order MoveEnemies → CheckOutOfBounds → Collision → MovePlayer: Task 11. ✓
- NativeList<int> scratch in Board.Tick: Task 6. ✓
- Spawn stays on main thread: reflected in Task 6 and preserved in Task 11. ✓
- Unity.Mathematics.Random, save version 2 with v1 compat: Task 4. ✓
- Board.cs float2 → Vector3 transform writeback: Task 3 step 4. ✓

**Placeholder scan:** No TBDs, TODOs, or hand-waved steps. Every code block is complete.

**Type consistency:** Job struct field names and `.Schedule()` assignment keys match between the job-definition tasks (7–10) and the final chained `Tick` in Task 11. `aliveCount` is consistently captured before `Schedule` calls. `removedEnemyIndices`/`addedEnemyIndices` are `NativeList<int>` from Task 6 onward.

**Scope:** Single implementation pass; stage 2 (ECS for transforms) explicitly deferred per spec.
