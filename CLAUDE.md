# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity 2022.3.62f2 sample project ("Survivor", a Vampire-Survivors-style game) for Chapter 12 of *Data-Oriented Design for Games* by Nitzan Wilnai (Manning). All gameplay code lives under `Assets/Scripts/` in the `Survivor` namespace; shared utilities live in `Assets/Scripts/Tools/` under `CommonTools`.

Open the project in Unity and press Play on `Assets/Scenes/MainGameScene.unity`. There is no test suite or CLI build script; builds and Play-mode runs happen from the Unity Editor.

Editor menu actions:
- `DOD/Balance/Parse Local` — runs `BalanceParser.ParseLocal()` which assigns IDs to every `EnemySO` under `Assets/Data/Enemies/`, validates `Assets/Data/Balance.asset`, serializes the balance to `Assets/Resources/balance.bytes`, and refreshes the AssetDatabase. Run this whenever any `*SO` asset under `Assets/Data/` changes — the runtime only reads `balance.bytes`, never the ScriptableObjects directly.

In Play mode, pressing `s` calls `ScreenCapture.CaptureScreenshot` (see `Game.captureScreenshot`).

## DOD architecture (critical — do not violate)

This codebase is a deliberate exemplar of the book's DOD style. Keep new code in the same shape:

- **Data classes are plain POCOs with public fields only.** `GameData`, `MetaData`, `Balance` have no methods, no properties, no behavior — only arrays and scalars. `Balance` is the exception: it owns its binary deserializer (`LoadBalance`) because it's effectively read-only config.
- **Logic is a `static` class of pure functions.** `Logic.cs` takes `(GameData, MetaData, Balance, …)` in, mutates the data classes, and returns results via `out`/`ref` or `Span<T>` scratch buffers. It never holds state and never touches `UnityEngine` GameObjects. New gameplay rules go here as new static methods — do not add instance state to `Logic`.
- **Board is the one MonoBehaviour bridge between Logic and the scene.** `Board.Tick` allocates `Span<int>` scratch buffers with `stackalloc`, calls `Logic.Tick`, then reconciles the enemy `GameObject` pool against the returned added/removed index lists. This is the template for any new system that needs to render Logic state: stack-allocated spans in, reconciliation loop out.
- **Visual classes are plain C# (not MonoBehaviour).** `MainMenuVisual`, `GameOverVisual`, `PauseMenuVisual` are `new`ed by `Game` and hold references to instantiated UI prefabs. They read from `GameData`/`MetaData` on `Show()` and push strings into `TextMeshProUGUI` — they never mutate game data. Add new screens the same way.
- **Data-in / transformation / data-out.** When extending `Logic.Tick`, pass new scratch buffers (`Span<int>`, counts) the same way `addedEnemyIndices`/`removedEnemyIndices` are threaded through — don't return `List<T>` or allocate per-frame.

## Tool-time vs runtime split

The `BalanceSO` / `EnemySO` ScriptableObjects under `Assets/Data/` are **tool-time only** — designers edit them in the Inspector. `BalanceParser.parse()` serializes them to `Assets/Resources/balance.bytes` with a leading `version` int and trailing `magic = 123456789` sentinel. At runtime, `Balance.LoadBalance()` reads the bytes with `BinaryReader` and builds the parallel arrays (`EnemyIDs`, `EnemyName`, `EnemyVelocity`, `EnemyRadius`, etc.) plus `EnemyNameToID` / `EnemyIDToName` lookup tables.

When adding a field to balance, edit all three in lockstep: `BalanceSO` (authoring), `BalanceParser.parse` (write order), `Balance.LoadBalance` (read order, same sequence). Mismatched order silently corrupts data until you hit the magic number.

## Save/load and ID remapping

`GameDataIO` / `MetaDataIO` write to `Application.persistentDataPath + "/DODSurvivor/"`. The non-obvious part is in `GameDataIO.Load`: after reading `gameData.EnemyType[]` it re-reads the enemy name table from the save file and remaps each saved type to the current `balance.EnemyNameToID[name]`. This is how the save file survives enemy reordering in `BalanceSO`. Preserve this pattern — persist **names**, remap to **IDs** on load.

## Game flow and state machine

`Game` (Singleton MonoBehaviour) owns the only instances of `GameData`, `MetaData`, `Balance` and drives the `MENU_STATE` enum (`NONE / MAIN_MENU / IN_GAME / GAME_OVER / PAUSE_MENU`). `Game.SetMenuState` is the single point that hides the outgoing screen and shows the incoming one. `Game.Update` only calls `Board.Tick` when `MenuState == IN_GAME`. New menu states must be handled in both the old-state and new-state branches of `SetMenuState`.

## UI access via GUIRef

UI prefabs have a `GUIRef` component holding named arrays of `Button`/`TextMeshProUGUI`/etc. Visual classes fetch controls by string name (`guiRef.GetTextGUI("GameTime")`, `guiRef.GetButton("Pause")`). When adding a UI element, add it to the appropriate `GUIRef*` array in the prefab's Inspector and look it up by name — do not use `GameObject.Find` or `transform.Find`.

## Enemy pool specifics

`Board` maintains a GameObject pool separately from the logical alive/dead index lists in `GameData`. Pool slots are typed (`m_enemyPoolType[i]`) and reused only when the requested `enemyType` matches (`getFreeEnemyPoolIndex`). `m_enemyToPoolIndex[gameDataEnemyIndex] = poolIndex` is the bridge between the two index spaces — when reading `Board.Tick`, remember that "enemy index" in `gameData` arrays is **not** the same as "pool index" in `m_enemyPool`.
