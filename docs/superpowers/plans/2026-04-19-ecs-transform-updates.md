# Parallel Transform Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the per-frame main-thread transform-write loop in `Board.Tick` with a Burst-compiled `IJobParallelForTransform` that writes all enemy pool transforms in parallel across Unity's worker threads.

**Architecture:** One new job struct lives at the bottom of `Board.cs`. `Board` gains two native containers (`TransformAccessArray m_transforms`, `NativeArray<float2> m_poolPositions`) parallel to the existing `m_enemyPool[]`. A main-thread gather converts alive-enemy positions into pool-keyed positions each frame; the parallel job then writes every pool transform. `Logic.cs`, `LogicJobs.cs`, `GameData`, `Balance`, `GameDataIO`, and `Game` are all untouched.

**Tech Stack:** Unity 2022.3.62f2, C#, `com.unity.burst`, `com.unity.collections`, `com.unity.mathematics` (all already installed). `TransformAccessArray` ships in `UnityEngine.Jobs` (core engine), `IJobParallelForTransform` in `Unity.Jobs` — no new packages.

**Verification model:** Unity project, no unit-test suite. Task 1 is a no-op change (infrastructure only) and must compile clean. Task 2 is the behavior switch-over. User runs Play-mode, Burst inspector, leak detection, and Profiler at the end.

**Spec:** `docs/superpowers/specs/2026-04-19-ecs-transform-updates-design.md`

---

## File structure

All edits live in a single file: `Assets/Scripts/Board.cs`. Two tasks touch it, plus one user-verification task. Splitting the code changes into two tasks separates infrastructure (Task 1 — allocate, grow, dispose the TAA; behavior unchanged) from the actual switch-over (Task 2 — add the job and use the TAA to drive parallel transform writes).

No other files are created or modified in this plan.

---

## Task 1: Board native containers, allocation, growth, teardown

**Files:**
- Modify: `Assets/Scripts/Board.cs`

After this task, `m_transforms` and `m_poolPositions` exist, are kept in sync with the existing pool, and are disposed on teardown. The original main-thread transform loop in `Tick` still runs — there is **no behavior change**. The task is a pure infrastructure commit.

- [ ] **Step 1: Add two using directives**

Open `Assets/Scripts/Board.cs`. The top currently reads:

```csharp
using System;
using CommonTools;
using TMPro;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
```

Add two lines so the block reads:

```csharp
using System;
using CommonTools;
using TMPro;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Jobs;
```

- [ ] **Step 2: Add two fields**

Locate the existing pool fields inside the `Board` class (around lines 21-27):

```csharp
public int MaxEnemyPoolSize;
GameObject[] m_enemyPool;
int[] m_enemyPoolType;
int[] m_enemyPoolUnusedIndices;
int m_enemyPoolUnusedIndicesCount;
int[] m_enemyToPoolIndex;
int m_enemyPoolCount;
```

Add two new fields immediately after them:

```csharp
public int MaxEnemyPoolSize;
GameObject[] m_enemyPool;
int[] m_enemyPoolType;
int[] m_enemyPoolUnusedIndices;
int m_enemyPoolUnusedIndicesCount;
int[] m_enemyToPoolIndex;
int m_enemyPoolCount;
TransformAccessArray m_transforms;
NativeArray<float2> m_poolPositions;
```

- [ ] **Step 3: Allocate both containers in `Board.Init`**

Inside `Init`, find this block (around lines 52-56):

```csharp
m_enemyPool = new GameObject[MaxEnemyPoolSize];
m_enemyPoolType = new int[MaxEnemyPoolSize];
m_enemyToPoolIndex = new int[MaxEnemyPoolSize];
m_enemyPoolUnusedIndices = new int[MaxEnemyPoolSize];
m_enemyPoolUnusedIndicesCount = 0;
```

Add two allocation lines directly below:

```csharp
m_enemyPool = new GameObject[MaxEnemyPoolSize];
m_enemyPoolType = new int[MaxEnemyPoolSize];
m_enemyToPoolIndex = new int[MaxEnemyPoolSize];
m_enemyPoolUnusedIndices = new int[MaxEnemyPoolSize];
m_enemyPoolUnusedIndicesCount = 0;

m_poolPositions = new NativeArray<float2>(MaxEnemyPoolSize, Allocator.Persistent);
m_transforms = new TransformAccessArray(MaxEnemyPoolSize);
```

Note: `TransformAccessArray`'s constructor argument is a *capacity hint* — the array starts empty (length 0). `m_poolPositions` is a full-length `NativeArray<float2>` allocated once with `Allocator.Persistent` (lifetime tied to `Board`, disposed in `OnDestroy`).

- [ ] **Step 4: Clear TAA at the top of `Board.Hide`**

Find the existing `Hide` method (around lines 101-117). Its body currently starts with:

```csharp
public void Hide()
{
    for (int enemyIdx = 0; enemyIdx < m_enemyPoolCount; enemyIdx++)
    {
```

Insert a TAA clear+reallocate *before* the destroy loop:

```csharp
public void Hide()
{
    if (m_transforms.isCreated) m_transforms.Dispose();
    m_transforms = new TransformAccessArray(MaxEnemyPoolSize);

    for (int enemyIdx = 0; enemyIdx < m_enemyPoolCount; enemyIdx++)
    {
```

The existing destroy loop and the rest of `Hide` stay unchanged. Reason: the destroy loop calls `GameObject.Destroy` on every pool GameObject, which invalidates the `Transform` handles the TAA holds. Clearing the TAA first avoids stale handles. `m_poolPositions` stays allocated across Hide/Show cycles — it's a plain buffer with no GameObject handles.

Note the capitalization: `TransformAccessArray.isCreated` (camelCase) — it is a `UnityEngine.Jobs` type, unlike `NativeArray<T>.IsCreated` (PascalCase, `Unity.Collections`).

- [ ] **Step 5: Grow TAA in lockstep with the pool in `getFreeEnemyPoolIndex`**

Find the pool-growth branch in `getFreeEnemyPoolIndex` (around lines 210-219):

```csharp
if (m_enemyPoolCount < MaxEnemyPoolSize)
{
    m_enemyPool[m_enemyPoolCount] = AssetManager.Instance.GetEnemyGameObject(SpriteParent, balance.EnemyPrefabName[enemyType]);

    Debug.Log("m_enemyPool[" + m_enemyPoolCount + "] " + m_enemyPool[m_enemyPoolCount].name);

    m_enemyPoolType[m_enemyPoolCount] = enemyType;
    m_enemyPoolCount++;
    return m_enemyPoolCount - 1;
}
```

Insert a single `Add` call right after the Instantiate, before `m_enemyPoolCount++`. The updated branch reads:

```csharp
if (m_enemyPoolCount < MaxEnemyPoolSize)
{
    m_enemyPool[m_enemyPoolCount] = AssetManager.Instance.GetEnemyGameObject(SpriteParent, balance.EnemyPrefabName[enemyType]);

    Debug.Log("m_enemyPool[" + m_enemyPoolCount + "] " + m_enemyPool[m_enemyPoolCount].name);

    m_enemyPoolType[m_enemyPoolCount] = enemyType;
    m_transforms.Add(m_enemyPool[m_enemyPoolCount].transform);
    m_enemyPoolCount++;
    return m_enemyPoolCount - 1;
}
```

Since this method is the sole place new pool GameObjects are created (both by `Show` on Continue and by `Tick` for mid-game spawns), one line here keeps the TAA index-aligned with `m_enemyPool` at every growth point.

- [ ] **Step 6: Add `OnDestroy`**

Currently `Board` has no `OnDestroy`. Add a new method immediately after the existing `pauseGame()` method (around line 279), inside the `Board` class:

```csharp
void OnDestroy()
{
    if (m_transforms.isCreated) m_transforms.Dispose();
    if (m_poolPositions.IsCreated) m_poolPositions.Dispose();
}
```

Both guards are needed to handle the case where `Board` is destroyed before `Init` runs (e.g. Editor domain reload).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Board.cs
git commit -m "refactor: add TransformAccessArray and pool-positions NativeArray to Board

Infrastructure only — TAA grows with the pool, m_poolPositions is
allocated persistent, both disposed in OnDestroy. No behavior change;
the parallel transform job is wired up in the next commit."
```

---

## Task 2: Parallel transform job + Tick switch-over

**Files:**
- Modify: `Assets/Scripts/Board.cs`

This task adds the job struct and replaces the main-thread transform-write loop with a gather + Schedule + Complete pair. After this commit, enemy transforms are written in parallel across Unity's worker threads.

- [ ] **Step 1: Add the job struct at the bottom of `Board.cs`**

The bottom of `Board.cs` currently looks like:

```csharp
        void pauseGame()
        {
            Game.Instance.SetMenuState(MENU_STATE.PAUSE_MENU);
            GameDataIO.Save(gameData, balance);
            MetaDataIO.Save(metaData);
        }

        void OnDestroy()
        {
            if (m_transforms.isCreated) m_transforms.Dispose();
            if (m_poolPositions.IsCreated) m_poolPositions.Dispose();
        }
    }
}
```

Add the job struct between the closing brace of the `Board` class and the closing brace of the `namespace Survivor` block, so the bottom reads:

```csharp
        void pauseGame()
        {
            Game.Instance.SetMenuState(MENU_STATE.PAUSE_MENU);
            GameDataIO.Save(gameData, balance);
            MetaDataIO.Save(metaData);
        }

        void OnDestroy()
        {
            if (m_transforms.isCreated) m_transforms.Dispose();
            if (m_poolPositions.IsCreated) m_poolPositions.Dispose();
        }
    }

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
}
```

Notes:
- The struct is `public` so other files could reference it if needed, matching the access level of the jobs in `LogicJobs.cs`.
- No `[NativeDisableParallelForRestriction]` — each `i` receives a distinct `TransformAccess` and we only write to `PoolPositions[i]` implicitly via `transform.localPosition`. No aliasing.
- `IJobParallelForTransform.Schedule` takes the `TransformAccessArray` and an optional `JobHandle` — no count, no batch size. Unity manages batching.

- [ ] **Step 2: Replace the per-enemy transform loop in `Board.Tick`**

Find this block inside `Board.Tick` (around lines 161-167):

```csharp
for (int i = 0; i < gameData.AliveEnemyCount; i++)
{
    int enemyIndex = gameData.AliveEnemyIndices[i];
    int poolIndex = m_enemyToPoolIndex[enemyIndex];
    float2 pos = gameData.EnemyPosition[enemyIndex];
    m_enemyPool[poolIndex].transform.localPosition = new Vector3(pos.x, pos.y, 0f);
}
```

Replace it in full with:

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

The first loop is the main-thread gather: it reads alive-enemy positions and writes them into the pool-keyed buffer. The subsequent Schedule/Complete dispatches the parallel Burst job that writes every `m_enemyPool[i].transform.localPosition` across worker threads.

**Position in `Tick` matters.** The surrounding code flow (unchanged) is:

1. Call `Logic.Tick` (runs its own job graph internally, completes it).
2. Pool-reconciliation loops: iterate `addedEnemyIndices` (SetActive + getFreeEnemyPoolIndex → may grow `m_transforms`) and `removedEnemyIndices` (SetActive false + stash unused indices).
3. **This step** — gather + transform job. By this point `m_transforms` has been grown for any new spawns, `m_enemyToPoolIndex` is up to date, and `gameData.AliveEnemyCount`/`AliveEnemyIndices` reflect the post-removal state.
4. Update `m_boardGUI.GameTimeText`.
5. Dispose `addedEnemyIndices` and `removedEnemyIndices`.
6. `if (isGameOver) gameOver();`.

Do not reorder these steps.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Board.cs
git commit -m "perf: parallel transform writes via IJobParallelForTransform

Replaces the main-thread per-enemy transform.localPosition loop with
a Burst-compiled IJobParallelForTransform that writes all pool
transforms in parallel across worker threads. Gather from
gameData.EnemyPosition into m_poolPositions runs on main thread
before scheduling."
```

---

## Task 3: User verification

**Files:** none. Verification only — to be run by the user in Unity.

- [ ] **Step 1: Compile**

Open the project in Unity. Wait for recompile. Expected: no red Console errors. First-time Burst warnings on the new job are acceptable.

- [ ] **Step 2: Burst Inspector**

`Jobs > Burst > Open Inspector`. Find `Survivor.UpdateEnemyTransformsJob`. Confirm it Burst-compiles with no warnings and no managed-reference errors.

- [ ] **Step 3: Play-mode smoke test**

Enter Play → Main Menu → Start. Confirm enemies spawn, move toward the player, collide, and leave the ring. Visually indistinguishable from pre-stage-2 behavior.

- [ ] **Step 4: Save round-trip (regression check for `Show` + TAA rebuild)**

Play for ~10 seconds so enemies exist. Pause (which writes the save file). Exit Play. Re-enter Play → Main Menu → Continue. Expected: enemies resume at correct positions. This exercises the Show → getFreeEnemyPoolIndex path that must re-populate the (cleared-on-Hide) TAA.

- [ ] **Step 5: Leak detection**

`Jobs > Leak Detection > Full Stack Traces`. Enter/exit Play 2–3 times, including at least one pause/save/Continue cycle. Expected Console: no "A Native Collection has not been disposed" or "Memory leak detected" warnings.

- [ ] **Step 6: Profiler**

Enter Play. Open `Window > Analysis > Profiler > CPU Usage`. Under `Board.Tick` on the main thread, the per-enemy `transform.localPosition =` cost from the pre-change version should be gone. Parallel transform-write work should appear on worker threads.

- [ ] **Step 7: Report results**

Report back: which steps pass, any Console errors or unexpected behavior.

---

## Self-review

**Spec coverage:**
- New packages: none needed per spec → Task 1 Step 1 adds only `using`s. ✓
- Two new Board fields `m_transforms`, `m_poolPositions`: Task 1 Step 2. ✓
- Allocations in `Board.Init`: Task 1 Step 3. ✓
- TAA clear at top of `Hide`: Task 1 Step 4. ✓
- TAA growth hook in `getFreeEnemyPoolIndex`: Task 1 Step 5. ✓
- `Board.OnDestroy` with `isCreated`/`IsCreated` guards: Task 1 Step 6. ✓
- Job struct `UpdateEnemyTransformsJob` in `Board.cs` (not a separate file): Task 2 Step 1. ✓
- Gather + Schedule + Complete replacing the final per-enemy loop in `Tick`: Task 2 Step 2. ✓
- Verification: Task 3. ✓

**Placeholder scan:** No TBDs, TODOs, or hand-waved steps. Every code block is complete.

**Type consistency:** `m_transforms` (TransformAccessArray), `m_poolPositions` (NativeArray<float2>), `UpdateEnemyTransformsJob` (struct), `PoolPositions` (field on the job) — all used consistently between tasks. The `isCreated` vs. `IsCreated` capitalization asymmetry between `TransformAccessArray` and `NativeArray<T>` is explicitly flagged at both the field-disposal sites (Task 1 Step 4 and Task 1 Step 6).

**Scope:** Single implementation pass, single file touched. No scope drift.
