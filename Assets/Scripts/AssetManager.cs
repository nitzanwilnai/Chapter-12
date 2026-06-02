using System;
using System.Collections;
using System.Collections.Generic;
using CommonTools;
using UnityEngine;

namespace Survivor
{
public class AssetManager : Singleton<AssetManager>
{
    [SerializeField] GameObject m_playerPrefab;
    [SerializeField] GameObject[] m_enemyPrefabs;

    [SerializeField] GameObject m_UIInGame;
    [SerializeField] GameObject m_UIMainMenu;
    [SerializeField] GameObject m_UIGameOver;
    [SerializeField] GameObject m_UIPauseMenu;

        public GameObject GetPlayerGameObject(Transform enemyParent)
        {
            return Instantiate(m_playerPrefab, enemyParent);
        }

        public GameObject GetEnemyGameObject(Transform enemyParent, string enemyPrefabName)
        {
            for(int i = 0; i < m_enemyPrefabs.Length; i++)
            {
                if(m_enemyPrefabs[i].name == enemyPrefabName)
                    return Instantiate(m_enemyPrefabs[i], enemyParent);
            }
            Debug.LogError("Enemy prefab not found: " + enemyPrefabName);
            return null;
        }

        public GameObject GetInGameUI()
        {
            return Instantiate(m_UIInGame);
        }

        public GameObject GetMainMenuUI()
        {
            return Instantiate(m_UIMainMenu);
        }

        public GameObject GetGameOverUI()
        {
            return Instantiate(m_UIGameOver);
        }

        public GameObject GetPauseMenuUI()
        {
            return Instantiate(m_UIPauseMenu);
        }
    }
}