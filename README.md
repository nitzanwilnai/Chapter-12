# Chapter 12 — Jobs, Burst & TransformAccessArray

Unity sample project for **Chapter 12** of *Data-Oriented Design for Games* by Nitzan Wilnai (Manning). It is a Vampire-Survivors-style game ("Survivor") whose **entire enemy simulation has been moved off the main thread** using the Unity C# Job System, Burst, and `TransformAccessArray`, so the game stays fast with thousands of enemies on screen.

## What this chapter demonstrates

Building on Chapter 11 (typed enemy pools, multi-enemy-type balance), Chapter 12 converts the per-frame update from main-thread `for` loops into a chain of parallel, Burst-compiled jobs:

- **C# Job System** — the whole enemy update runs as scheduled jobs.
- **Burst** — every job is `[BurstCompile]`d to native SIMD code.
- **`TransformAccessArray`** — enemy `GameObject` transforms are written in parallel via an `IJobParallelForTransform`, instead of a main-thread loop.
- **`NativeArray<float2>` / `Unity.Mathematics`** — enemy data lives in native containers using `float2`/`math` for Burst compatibility.

## The job pipeline (`LogicJobs.cs` + `Board.cs`)

Each frame `Logic.Tick` schedules and chains:

1. `MoveEnemiesJob` (`IJobParallelFor`) — advance every enemy toward the player.
2. `CheckOutOfBoundsJob` (`IJob`) — collect enemies past the despawn radius into a `NativeList`.
3. `ComputeCollisionDisplacementJob` (`IJobParallelFor`) — compute enemy-vs-enemy separation.
4. `ApplyCollisionDisplacementJob` (`IJobParallelFor`) — apply that separation.
5. `MovePlayerJob` (`IJobParallelFor`) — shift all enemies by the player's movement.
6. `UpdateEnemyTransformsJob` (`IJobParallelForTransform`, in `Board.cs`) — push the simulated positions onto the pooled enemy transforms via the `TransformAccessArray`.

The main thread then folds the removed enemy indices back into the alive/dead index lists.

## Architecture (Data-Oriented Design)

- **`GameData` / `MetaData` / `Balance`** — plain data containers (POCOs and native arrays), no behavior.
- **`Logic`** — a `static` class of pure functions plus the job structs; it mutates the data and never touches GameObjects.
- **`Board`** — the single MonoBehaviour bridge: schedules the jobs, owns the `TransformAccessArray`, and reconciles the enemy GameObject pool against the added/removed index lists.
- **Visual classes** (`MainMenuVisual`, `GameOverVisual`, `PauseMenuVisual`) — plain C#, read data and drive the UI.

## Running

- Unity **2022.3.62f2**.
- Open `Assets/Scenes/MainGameScene.unity` and press **Play**.
- After editing any `*SO` asset under `Assets/Data/`, run the editor menu **DOD ▸ Balance ▸ Parse Local** to regenerate `Assets/Resources/balance.bytes` (the runtime reads the bytes, not the ScriptableObjects).
- Press `s` in Play mode to capture a screenshot.

## Related

See the sibling **Chapter-12-only-move** project for an experimentation sandbox that isolates just the enemy-movement step and compares several parallelization techniques (plain jobs, Burst-only, ECS, parallel transform sync) across branches.
