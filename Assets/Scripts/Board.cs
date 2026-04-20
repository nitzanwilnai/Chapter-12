using System;
using CommonTools;
using TMPro;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace Survivor
{
    public class BoardGUI
    {
        public GameObject UI;
        public TextMeshProUGUI GameTimeText;
    }

    public class Board : MonoBehaviour
    {
        GameObject m_player;
        public Transform SpriteParent;

        public int MaxEnemyPoolSize;
        GameObject[] m_enemyPool;
        int[] m_enemyPoolType;
        int[] m_enemyPoolUnusedIndices;
        int m_enemyPoolUnusedIndicesCount;
        int[] m_enemyToPoolIndex;
        int m_enemyPoolCount;
        TransformAccessArray m_transforms;
        NativeArray<float2> m_poolPositions;

        Camera m_mainCamera;
        Vector2 m_mouseDownPos;

        BoardGUI m_boardGUI;

        public GameObject InputCircleOut;
        public GameObject InputCircleIn;

        GameData gameData;
        MetaData metaData;
        Balance balance;

        public void Init(MetaData metaData, GameData gameData, Balance balance, Camera mainCamera)
        {
            m_mainCamera = mainCamera;

            this.metaData = metaData;
            this.gameData = gameData;
            this.balance = balance;

            m_player = AssetManager.Instance.GetPlayerGameObject(SpriteParent);
            m_player.transform.localPosition = Vector2.zero;

            m_enemyPool = new GameObject[MaxEnemyPoolSize];
            m_enemyPoolType = new int[MaxEnemyPoolSize];
            m_enemyToPoolIndex = new int[MaxEnemyPoolSize];
            m_enemyPoolUnusedIndices = new int[MaxEnemyPoolSize];
            m_enemyPoolUnusedIndicesCount = 0;

            m_poolPositions = new NativeArray<float2>(MaxEnemyPoolSize, Allocator.Persistent);
            m_transforms = new TransformAccessArray(MaxEnemyPoolSize);

            m_boardGUI = new BoardGUI();
            m_boardGUI.UI = AssetManager.Instance.GetInGameUI();

            GUIRef guiRef = m_boardGUI.UI.GetComponent<GUIRef>();
            m_boardGUI.GameTimeText = guiRef.GetTextGUI("GameTime");
            guiRef.GetButton("Pause").onClick.AddListener(pauseGame);

            m_player.SetActive(false);
            InputCircleOut.SetActive(false);

            hideUI();
        }

        public void StartGame()
        {
            Logic.StartGame(gameData, balance);
        }

        public void Show()
        {
            for (int enemyIdx = 0; enemyIdx < gameData.AliveEnemyCount; enemyIdx++)
            {
                int enemyIndex = gameData.AliveEnemyIndices[enemyIdx];
                int enemyType = gameData.EnemyType[enemyIndex];

                int poolIndex = getFreeEnemyPoolIndex(enemyType);
                m_enemyPool[poolIndex].SetActive(true);
                m_enemyToPoolIndex[enemyIndex] = poolIndex;
            }
            for (int enemyIdx = gameData.AliveEnemyCount; enemyIdx < MaxEnemyPoolSize; enemyIdx++)
            {
                m_enemyPoolType[enemyIdx] = -1;
            }

            m_enemyPoolUnusedIndicesCount = 0;

            m_player.SetActive(true);

            m_boardGUI.UI.SetActive(true);

            InputCircleOut.SetActive(false);
        }

        public void Hide()
        {
            if (m_transforms.isCreated) m_transforms.Dispose();
            m_transforms = new TransformAccessArray(MaxEnemyPoolSize);

            for (int enemyIdx = 0; enemyIdx < m_enemyPoolCount; enemyIdx++)
            {
                Debug.Log("HIDE() m_enemyPool[" + enemyIdx + "] " + m_enemyPool[enemyIdx].name);
                m_enemyPool[enemyIdx].SetActive(false);
                GameObject.Destroy(m_enemyPool[enemyIdx]);
                m_enemyPool[enemyIdx] = null;
                m_enemyPoolType[enemyIdx] = -1;
            }
            m_enemyPoolCount = 0;
            m_enemyPoolUnusedIndicesCount = 0;

            m_player.SetActive(false);

            hideUI();
        }

        public void hideUI()
        {
            m_boardGUI.UI.SetActive(false);
        }

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
                m_poolPositions[poolIndex] = gameData.EnemyPosition[enemyIndex];
            }

            new UpdateEnemyTransformsJob
            {
                PoolPositions = m_poolPositions,
            }.Schedule(m_transforms).Complete();

            m_boardGUI.GameTimeText.text = CommonVisual.GetTimeElapsedString(gameData.GameTime);

            addedEnemyIndices.Dispose();
            removedEnemyIndices.Dispose();

            if (isGameOver)
                gameOver();
        }

        int getFreeEnemyPoolIndex(int enemyType)
        {
            // check unused pool indices
            if (m_enemyPoolUnusedIndicesCount > 0)
            {
                int poolIndex = -1;
                for (int i = 0; i < m_enemyPoolUnusedIndicesCount; i++)
                {
                    int tempPoolIndex = m_enemyPoolUnusedIndices[i];
                    if (m_enemyPoolType[tempPoolIndex] == enemyType)
                    {
                        poolIndex = tempPoolIndex;
                        break;
                    }
                }

                if (poolIndex != -1)
                {
                    // if found, remove from unused indices array
                    int count = 0;
                    for (int i = 0; i < m_enemyPoolUnusedIndicesCount; i++)
                    {
                        if (m_enemyPoolUnusedIndices[i] != poolIndex)
                        {
                            m_enemyPoolUnusedIndices[count++] = m_enemyPoolUnusedIndices[i];
                        }
                    }
                    m_enemyPoolUnusedIndicesCount = count;
                    return poolIndex;
                }
            }

            if (m_enemyPoolCount < MaxEnemyPoolSize)
            {
                m_enemyPool[m_enemyPoolCount] = AssetManager.Instance.GetEnemyGameObject(SpriteParent, balance.EnemyPrefabName[enemyType]);

                Debug.Log("m_enemyPool[" + m_enemyPoolCount + "] " + m_enemyPool[m_enemyPoolCount].name);

                m_enemyPoolType[m_enemyPoolCount] = enemyType;
                m_transforms.Add(m_enemyPool[m_enemyPoolCount].transform);
                m_enemyPoolCount++;
                return m_enemyPoolCount - 1;
            }
            Debug.LogError("Enemy pool size exceeded!");
            return -1;
        }

        void handleInput()
        {
#if UNITY_EDITOR
            bool mouseDown = Input.GetMouseButtonDown(0);
            bool mouseMove = Input.GetMouseButton(0);
            bool mouseUp = Input.GetMouseButtonUp(0);
            Vector3 mousePosition = Input.mousePosition;
#else
bool mouseDown = (Input.touchCount > 0) && Input.GetTouch(0).phase == TouchPhase.Began;
bool mouseMove = (Input.touchCount > 0) && Input.GetTouch(0).phase == TouchPhase.Moved;
bool mouseUp = (Input.touchCount > 0) && (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled);
Vector3 mousePosition = Vector3.zero;
if (Input.touchCount > 0)
mousePosition = Input.GetTouch(0).position;
#endif
            Vector3 mouseWorldPos = m_mainCamera.ScreenToWorldPoint(mousePosition);
            Vector2 mouseLocalPos = SpriteParent.InverseTransformPoint(mouseWorldPos);

            if (mouseDown)

            {
                InputCircleOut.SetActive(true);
                m_mouseDownPos = mouseLocalPos;
            }

            if (mouseMove)
            {
                InputCircleOut.transform.position = m_mouseDownPos;
                Vector2 diff = (mouseLocalPos - m_mouseDownPos);
                float dist = diff.magnitude;
                if (dist > 1.0f)
                    dist = 1.0f;
                InputCircleIn.transform.localPosition = (mouseLocalPos - m_mouseDownPos).normalized * dist * ((1.0f - InputCircleIn.transform.localScale.x) / 2.0f);
                Logic.MouseMove(gameData, m_mouseDownPos, mouseLocalPos);
            }

            if (mouseUp)
            {
                InputCircleOut.SetActive(false);
                Logic.MouseUp(gameData);
            }
        }

        void gameOver()
        {
            Game.Instance.SetMenuState(MENU_STATE.GAME_OVER);
            MetaDataIO.Save(metaData);
            hideUI();
        }

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