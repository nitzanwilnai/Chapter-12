# Design: Burst + Jobs for the Enemy Tick

**Date:** 2026-04-18
**Scope:** `Assets/Scripts/Logic.cs`, `Assets/Scripts/GameData.cs`, `Assets/Scripts/Balance.cs`, `Assets/Scripts/GameDataIO.cs`, `Assets/Scripts/Board.cs`, `Assets/Scripts/Game.cs`, `Packages/manifest.json`
**Out of scope:** Unity Entities / DOTS ECS (stage 2, separate spec)

## Goal

Convert the four per-frame gameplay transformations in `Logic.Tick` — `movePlayer`, `moveEnemies`, `doEemyToEnemyCollision`, `checkEnemyOutOfBounds` — into `[BurstCompile]` jobs operating on `NativeArray<T>` data owned by `GameData` and `Balance`. Preserve the book's "Board calls Logic, Logic transforms data" architecture: Burst is an implementation detail of `Logic`, not a new layer. Leave the door open for stage 2, which will replace `Board`'s GameObject pool with ECS entities that read positions out of the same native arrays.

## Non-goals

- No `com.unity.entities` / DOTS ECS in this pass.
- No frame-pipelining. Each tick does `Schedule → Complete` within a single frame.
- No `IJobParallelForBatch`, custom `NativeContainer`s, or spatial hashing for collision.
- No asmdef split. Code stays in `Assembly-CSharp`.
- `BalanceParser.cs` unchanged — tool-time output is still `balance.bytes`; only the runtime reader swaps to native arrays.
- No unit tests. Verification is Play-mode + Profiler + Burst inspector, matching the rest of the codebase.

## Architecture

### Packages

Add to `Packages/manifest.json`:

- `com.unity.burst` — `[BurstCompile]` attribute and native codegen
- `com.unity.collections` — `NativeArray<T>`, `NativeList<T>`
- `com.unity.mathematics` — `float2`, `math.*`, `Unity.Mathematics.Random`

`IJob` / `IJobParallelFor` ship with the core engine on Unity 2022.3; no separate jobs package is needed.

### File layout

- Jobs live in a new `Assets/Scripts/LogicJobs.cs` as `[BurstCompile] struct`s in the `Survivor` namespace.
- `Logic.cs` stays the orchestrator. Its public `Tick` signature is preserved from `Board`'s point of view; internally it now `Schedule()`s jobs instead of running inline loops.
- `Balance`, `MetaData`, `MetaDataIO`, all `*Visual` classes: unchanged.

## Data layout

### `GameData`

Swap managed arrays to native containers; add RNG state. `PlayerDirection` becomes `float2` so Logic/jobs can consume it without conversion.

```csharp
public class GameData
{
    public bool InGame;

    public NativeArray<int>   AliveEnemyIndices;
    public int                AliveEnemyCount;
    public NativeArray<int>   DeadEnemyIndices;
    public int                DeadEnemyCount;

    public float              SpawnTime;

    public NativeArray<float2> EnemyPosition;
    public NativeArray<int>    EnemyType;

    public float2             PlayerDirection;

    public float              GameTime;

    public Unity.Mathematics.Random Rng;
}
```

`Board` converts `Vector2` ↔ `float2` at the input/output boundary (input handling, transform writeback).

### `Balance`

Per-enemy-type arrays become native. Strings and dictionaries stay managed — they're load-time only and never touched by jobs.

```csharp
public NativeArray<float> EnemyVelocity;
public NativeArray<float> EnemyRadius;
public NativeArray<int>   SpawnDataID;
public NativeArray<int>   SpawnDataWeight;

// Unchanged (managed):
public string[] EnemyName;
public string[] EnemyPrefabName;
public Dictionary<string, int> EnemyNameToID;
public string[] EnemyIDToName;
public int[] EnemyIDs;
public int MaxEnemies, NumEnemyTypes;
public float SpawnRadius, PlayerVelocity, PlayerRadius, SpawnTime;
```

### Lifecycle

Native memory must be freed or Unity leaks it across Editor play sessions.

- `Logic.AllocateGameData(gameData, balance)` — existing; allocates all four native arrays with `Allocator.Persistent`. Guarded with `if (gameData.EnemyPosition.IsCreated) return;` to tolerate hot-reload.
- `Logic.FreeGameData(gameData)` — **new**. Disposes all four native arrays. Idempotent via `.IsCreated` checks.
- `Balance.LoadBalance` — populates native arrays with `Allocator.Persistent`. Guarded the same way.
- `Balance.Free()` — **new**. Disposes Balance's native arrays.
- `Game.OnDestroy()` — **new MonoBehaviour hook**: calls `Logic.FreeGameData(m_gameData); m_balance.Free();`

## Job breakdown

Four `[BurstCompile]` job structs in `LogicJobs.cs`. Batch size `32` for all `IJobParallelFor`s (reasonable default for hundreds of enemies; tunable later).

### 1. `MovePlayerJob : IJobParallelFor`

Shifts every alive enemy by the player-velocity delta.

```
Fields:
  [ReadOnly] NativeArray<int>    AliveEnemyIndices
             NativeArray<float2> EnemyPosition
             float2              PlayerOffset   // = PlayerDirection * PlayerVelocity * dt
Execute(i):
  EnemyPosition[AliveEnemyIndices[i]] -= PlayerOffset;
```

### 2. `MoveEnemiesJob : IJobParallelFor`

Each enemy walks toward origin at its type's velocity.

```
Fields:
  [ReadOnly] NativeArray<int>    AliveEnemyIndices
  [ReadOnly] NativeArray<int>    EnemyType
  [ReadOnly] NativeArray<float>  EnemyVelocity
             NativeArray<float2> EnemyPosition
             float dt
Execute(i):
  idx = AliveEnemyIndices[i];
  p = EnemyPosition[idx];
  dir = -math.normalizesafe(p);
  EnemyPosition[idx] = p + dir * EnemyVelocity[EnemyType[idx]] * dt;
```

### 3. Two-pass collision

Pass A — `ComputeCollisionDisplacementJob : IJobParallelFor`. Race-free: each i only writes its own slot in `Displacement`.

```
Fields:
  [ReadOnly] NativeArray<int>    AliveEnemyIndices
  [ReadOnly] NativeArray<float2> EnemyPosition
  [ReadOnly] NativeArray<int>    EnemyType
  [ReadOnly] NativeArray<float>  EnemyRadius
             int                 AliveEnemyCount
             NativeArray<float2> Displacement   // length == AliveEnemyCount
Execute(i):
  idx_i = AliveEnemyIndices[i]; p_i = EnemyPosition[idx_i]; r_i = EnemyRadius[EnemyType[idx_i]];
  accum = float2.zero;
  for j in 0..AliveEnemyCount:
    if (j == i) continue;
    idx_j = AliveEnemyIndices[j]; p_j = EnemyPosition[idx_j]; r_j = EnemyRadius[EnemyType[idx_j]];
    diff = p_i - p_j;
    totalR = r_i + r_j;
    if (math.lengthsq(diff) <= totalR*totalR) {
      dir = math.normalizesafe(diff);
      overlap = totalR - math.length(diff);
      accum += dir * overlap * 0.5f;
    }
  Displacement[i] = accum;
```

Pass B — `ApplyCollisionDisplacementJob : IJobParallelFor`.

```
Fields:
  [ReadOnly] NativeArray<int>    AliveEnemyIndices
  [ReadOnly] NativeArray<float2> Displacement
             NativeArray<float2> EnemyPosition
Execute(i):
  EnemyPosition[AliveEnemyIndices[i]] += Displacement[i];
```

**Behavior change from current code:** collisions become symmetric and simultaneous rather than order-dependent. This is a correctness improvement, called out explicitly in the chapter text.

### 4. `CheckOutOfBoundsJob : IJob` (serial, Burst)

Scans alive enemies, appends removed indices to a `NativeList<int>`. Kept serial to avoid `NativeQueue<int>.ParallelWriter` complexity for marginal gain at a few-hundred-enemy scale.

```
Fields:
  [ReadOnly] NativeArray<int>    AliveEnemyIndices
  [ReadOnly] NativeArray<float2> EnemyPosition
             int                 AliveEnemyCount
             float               DistanceSqrLimit   // = SpawnRadius^2 * 1.1
             NativeList<int>     RemovedEnemies
Execute():
  for i in 0..AliveEnemyCount:
    idx = AliveEnemyIndices[i];
    if (math.lengthsq(EnemyPosition[idx]) > DistanceSqrLimit)
      RemovedEnemies.Add(idx);
```

`Logic` still owns the actual removal (moving indices from alive → dead) on the main thread after `Complete()` — this serializes the `AliveEnemyIndices` mutation.

## Tick orchestration

`Logic.Tick` preserves its public role. Signature changes to take `NativeList<int>` scratch instead of `Span<int>`:

```csharp
public static void Tick(
    MetaData metaData, GameData gameData, Balance balance, float dt,
    out bool gameOver,
    NativeList<int> addedEnemyIndices,
    NativeList<int> removedEnemyIndices)
{
    gameData.GameTime += dt;

    // Spawn stays serial on main thread (mutates alive/dead lists, draws RNG).
    gameData.SpawnTime += dt;
    if (gameData.SpawnTime >= balance.SpawnTime) {
        gameData.SpawnTime -= balance.SpawnTime;
        if (canSpawnEnemy(gameData, balance))
            spawnEnemy(gameData, balance, addedEnemyIndices);
    }

    float2 playerOffset = gameData.PlayerDirection * balance.PlayerVelocity * dt;
    var displacement = new NativeArray<float2>(gameData.AliveEnemyCount, Allocator.TempJob);

    JobHandle h = default;
    h = new MoveEnemiesJob { /* ... */ }.Schedule(gameData.AliveEnemyCount, 32, h);
    h = new CheckOutOfBoundsJob             { RemovedEnemies = removedEnemyIndices, /* ... */ }
          .Schedule(h);
    h = new ComputeCollisionDisplacementJob { Displacement = displacement, /* ... */ }
          .Schedule(gameData.AliveEnemyCount, 32, h);
    h = new ApplyCollisionDisplacementJob   { Displacement = displacement, /* ... */ }
          .Schedule(gameData.AliveEnemyCount, 32, h);
    h = new MovePlayerJob  { /* ... */ }.Schedule(gameData.AliveEnemyCount, 32, h);

    h.Complete();
    displacement.Dispose();

    // Main-thread: fold removedEnemyIndices into AliveEnemyIndices / DeadEnemyIndices.
    for (int i = 0; i < removedEnemyIndices.Length; i++)
        removeEnemyFromAliveList(gameData, removedEnemyIndices[i]);

    gameOver = false;
}
```

Dependency chain: `MoveEnemies → CheckOutOfBounds → ComputeDisplacement → ApplyDisplacement → MovePlayer`. Matches the current `Logic.Tick` order. All linear; no parallel branches.

**One intentional semantic difference from the current code:** the existing `checkEnemyOutOfBounds` mutates `AliveEnemyIndices` in place, so collision sees the post-removal alive list. In this design, `CheckOutOfBoundsJob` only *records* indices into `removedEnemyIndices`; the actual `AliveEnemyIndices` mutation happens on the main thread after `h.Complete()`. As a result, a just-left-the-ring enemy participates in one final collision pass before being removed. The visual impact is negligible (the enemy is at the ring edge, far from other enemies), and the simplification is worth the clarity of a single job graph with a single `Complete()`.

### `Board.Tick` changes

Replace the two `stackalloc Span<int>` scratch buffers with `NativeList<int>(balance.MaxEnemies, Allocator.TempJob)` allocated at the top of `Board.Tick`, passed into `Logic.Tick`, consumed for pool reconciliation, and disposed at the end. Pool reconciliation (`getFreeEnemyPoolIndex`, `m_enemyToPoolIndex`, transform writeback loop) is unchanged in logic — it just reads from `gameData.EnemyPosition` as `NativeArray<float2>` and converts to `Vector3` at the `transform.localPosition` assignment.

## Random

`UnityEngine.Random` is managed and non-Burst-compatible; its state is also a global singleton, so it can't be serialized with `GameData`. Replace with `Unity.Mathematics.Random`:

- `GameData.Rng` seeded in `Logic.StartGame`: `gameData.Rng = new Unity.Mathematics.Random((uint)DateTime.Now.Ticks | 1u);` (the `| 1u` guards the non-zero-seed requirement).
- `spawnEnemy` and `getRandomEnemyTypeByWeight` call `gameData.Rng.NextFloat()` / `gameData.Rng.NextInt(...)`. Spawn remains main-thread, so no cross-thread RNG concerns.

## Save/load impact

`GameDataIO` format version bumps `1 → 2`:

- All existing fields are written/read identically — `BinaryWriter.Write(float)` doesn't care whether the source is `Vector2[]` or `NativeArray<float2>`; reads address `.x` / `.y` on `float2` the same way.
- New trailing field: `uint rngState = gameData.Rng.state;` written at the end; read back on load.
- `Load` checks the leading `version` int. For `v1` files, skip the RNG read and leave `Rng` as seeded by `StartGame`. For `v2`, restore it.

This adds deterministic-replay capability that the current game doesn't have (`UnityEngine.Random` state was never saved).

`MetaDataIO` is unchanged. `BalanceParser` is unchanged.

## Verification

1. **Play mode smoke test.** Enter Play, start a game, confirm enemies spawn, move toward player, collide, leave the ring. Confirm pause/resume and Continue from save both work.
2. **Burst inspector.** `Jobs > Burst > Open Inspector` — confirm all four jobs Burst-compile with no warnings, no managed references, no fallback-to-managed exceptions.
3. **Save round-trip.** Save, exit Play, re-enter, Continue — enemy positions, types, and alive/dead lists restore in place; RNG state restored.
4. **Stress.** Raise `BalanceSO.NumEnemies` (rebuild `balance.bytes` via `DOD/Balance/Parse Local`), compare `Logic.Tick` profiler marker cost against the current managed implementation at matching enemy counts.
5. **Leak detection.** Enable `Jobs > Leak Detection → Full`, enter/exit Play repeatedly in the Editor, confirm no native-allocation leak warnings.

## Stage-2 readiness

After this pass, `gameData.EnemyPosition` is a `NativeArray<float2>` — the exact shape an ECS rendering system wants. Stage 2 (replace `Board`'s GameObject pool with ECS entities for transform updates) is confined to the rendering layer: `Logic` and `GameData` will not need further changes.
