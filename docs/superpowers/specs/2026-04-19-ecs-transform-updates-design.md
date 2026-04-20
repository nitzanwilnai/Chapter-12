# Design: Parallel Transform Updates via `TransformAccessArray`

**Date:** 2026-04-19
**Scope:** `Assets/Scripts/Board.cs` only.
**Out of scope:** Unity Entities (DOTS), Entities Graphics, any change to `Logic`, `GameData`, `Balance`, `GameDataIO`, `LogicJobs`, or `Game`.

## Goal

Replace the per-frame main-thread loop in `Board.Tick` that sets `m_enemyPool[poolIndex].transform.localPosition` for every alive enemy with a Burst-compiled `IJobParallelForTransform` that writes all pool transforms in parallel across Unity's worker threads. Achieves the stated user intent — "update positions using ECS instead of setting the transform for each GameObject individually" — without converting enemies to DOTS entities.

## Non-goals

- No `com.unity.entities` package. Enemies stay as GameObjects with `SpriteRenderer`.
- No chaining onto `Logic.Tick`'s job graph. Pool reconciliation (SetActive, `m_enemyToPoolIndex`, pool growth) stays on the main thread between the two `.Complete()` calls.
- No change to `Logic`, `LogicJobs`, `GameData`, `Balance`, `GameDataIO`, or `Game`. Honors the stage-1 spec's promise that "stage 2 is entirely on the rendering side."
- No jobification of the player `Transform`. Player stays at origin; movement shifts enemies relative to the player.
- No new file. The single job struct lives in `Board.cs` alongside its only caller.
- No new Unity packages. `TransformAccessArray` ships in `UnityEngine.Jobs` (core engine); `IJobParallelForTransform` is in `Unity.Jobs` (already available through existing Burst/Collections deps).

## Architecture

### Data flow

```
gameData.EnemyPosition (NativeArray<float2>, keyed by enemyIndex)
           |
           | (main-thread gather: alive-enemy → pool slot)
           v
m_poolPositions       (NativeArray<float2>, keyed by poolIndex)
           |
           | (IJobParallelForTransform over m_transforms)
           v
m_enemyPool[poolIndex].transform.localPosition
```

The gather runs on the main thread after `Logic.Tick` returns and after pool reconciliation (removal fold, add/remove SetActive, pool growth). It consumes `gameData.AliveEnemyIndices` (already compacted by Logic) and `m_enemyToPoolIndex` (already updated by reconciliation). The transform job then runs in parallel and writes every `m_enemyPool[i].transform.localPosition` across worker threads.

### Why two `.Complete()` per frame

`Logic.Tick` completes its own five-job graph so that the main thread can safely perform pool reconciliation — which is unavoidably main-thread work: `GameObject.SetActive`, managed-array writes into `m_enemyToPoolIndex`, and `Instantiate` calls during pool growth are not Burst-compatible. After reconciliation the transform job schedules with its own `.Complete()`. Two stalls, but non-idle: the main thread is *doing* reconciliation while the workers between them complete, so the gap cost is the fence overhead (low μs) rather than an idle wait.

Chaining the transform job onto Logic's handle would require moving pool reconciliation into jobs, which is a substantially larger project (entity-ification of the pool) for marginal frame-time benefit at this scale. Explicitly deferred.

## Board state additions

Two new fields on `Board`:

```csharp
TransformAccessArray m_transforms;       // capacity MaxEnemyPoolSize, grows with the pool
NativeArray<float2>  m_poolPositions;    // length MaxEnemyPoolSize, keyed by poolIndex
```

`m_transforms` starts empty and grows once per pool-slot allocation (never shrinks during play; cleared on `Hide`). `m_poolPositions` is allocated once to full capacity; inactive pool slots carry stale values the job still writes to their (invisible) GameObjects.

## The job

Appended to `Board.cs`, inside the `Survivor` namespace, outside the `Board` class:

```csharp
[BurstCompile]
public struct UpdateEnemyTransformsJob : IJobParallelForTransform
{
    [ReadOnly] public NativeArray<float2> PoolPositions;

    public void Execute(int i, TransformAccess transform)
    {
        float2 p = PoolPositions[i];
        transform.localPosition = new Vector3(p.x, p.y, 0f);
    }
}
```

No `[NativeDisableParallelForRestriction]`: each iteration's `TransformAccess` is supplied by the Jobs system and is guaranteed distinct, so there is no aliasing for the safety checker to worry about. `PoolPositions` is read-only. The job is a pure array-to-transform copy — Burst-optimal.

Required additions at the top of `Board.cs`:

```csharp
using Unity.Burst;
using UnityEngine.Jobs;
```

## Tick integration

The current final loop in `Board.Tick`:

```csharp
for (int i = 0; i < gameData.AliveEnemyCount; i++)
{
    int enemyIndex = gameData.AliveEnemyIndices[i];
    int poolIndex = m_enemyToPoolIndex[enemyIndex];
    float2 pos = gameData.EnemyPosition[enemyIndex];
    m_enemyPool[poolIndex].transform.localPosition = new Vector3(pos.x, pos.y, 0f);
}
```

Becomes a gather + a parallel job schedule:

```csharp
for (int i = 0; i < gameData.AliveEnemyCount; i++)
{
    int enemyIndex = gameData.AliveEnemyIndices[i];
    int poolIndex = m_enemyToPoolIndex[enemyIndex];
    m_poolPositions[poolIndex] = gameData.EnemyPosition[enemyIndex];
}

new UpdateEnemyTransformsJob
{
    PoolPositions = m_poolPositions,
}.Schedule(m_transforms).Complete();
```

Placement: replaces the final per-enemy transform loop, runs AFTER the add/remove pool-reconciliation loops (which may grow `m_transforms`) and BEFORE the `m_boardGUI.GameTimeText.text =` update and the `NativeList` disposals. `IJobParallelForTransform.Schedule` takes the `TransformAccessArray` and an optional `JobHandle` dependency — no count, no batch size. Unity manages batching internally (~one batch per worker thread).

## Lifecycle

**`Board.Init` — allocate once:**
```csharp
m_poolPositions = new NativeArray<float2>(MaxEnemyPoolSize, Allocator.Persistent);
m_transforms = new TransformAccessArray(MaxEnemyPoolSize);  // capacity hint; starts empty
```

**`Board.getFreeEnemyPoolIndex` — grow TAA in lockstep with the pool.** After the existing `m_enemyPool[m_enemyPoolCount] = AssetManager.Instance.GetEnemyGameObject(...)` line:

```csharp
m_transforms.Add(m_enemyPool[m_enemyPoolCount].transform);
```

This also covers the Show/Continue flow — when the pool is rebuilt after a `Hide`, every fresh `getFreeEnemyPoolIndex` call re-adds to the (empty-after-Hide) TAA.

**`Board.Hide` — clear TAA before destroying GameObjects.** The existing `Hide` destroys every pool GameObject; destroying the underlying GameObject invalidates its `Transform` handle inside the TAA. Add at the top of `Hide`, before the destroy loop:

```csharp
if (m_transforms.isCreated) m_transforms.Dispose();
m_transforms = new TransformAccessArray(MaxEnemyPoolSize);
```

`m_poolPositions` stays allocated across Hide/Show — it's just a `float2` buffer with no GameObject handles.

**`Board.OnDestroy` — new method, disposes both containers.**
```csharp
void OnDestroy()
{
    if (m_transforms.isCreated) m_transforms.Dispose();
    if (m_poolPositions.IsCreated) m_poolPositions.Dispose();
}
```

Note the capitalization asymmetry: `TransformAccessArray.isCreated` (camelCase, `UnityEngine.Jobs`) vs. `NativeArray<T>.IsCreated` (PascalCase, `Unity.Collections`).

## Verification

1. **Compile.** Open Unity, Console clean. Both new usings (`UnityEngine.Jobs`, `Unity.Burst`) resolve.
2. **Burst Inspector.** `Jobs > Burst > Open Inspector` — confirm `Survivor.UpdateEnemyTransformsJob` Burst-compiles with no warnings, no managed references.
3. **Play-mode smoke.** Start a game → enemies spawn, visually move toward player, collide, leave the ring. Indistinguishable from pre-change behavior.
4. **Save round-trip (regression for Show).** Pause (writes save), exit Play, re-enter, Continue — enemies resume at correct positions. Validates that `Show` clears the TAA and that `getFreeEnemyPoolIndex` re-adds fresh transforms.
5. **Leak detection.** `Jobs > Leak Detection > Full Stack Traces`. Enter/exit Play a few times including at least one pause/save and Continue. No "Native Collection has not been disposed" warnings.
6. **Profiler comparison.** Open Profiler, `CPU Usage`. The pre-change per-enemy `transform.localPosition =` main-thread work should vanish from the main-thread profile and appear on worker threads under `Board.Tick`.

Verification is Play-mode + Profiler + Burst inspector, consistent with the rest of the project (no unit tests).
