using System.Collections;
using System.Collections.Generic;
using CommonTools;
using TMPro;
using UnityEngine;

namespace Survivor
{
public class GameOverGUI
{
    public GameObject UI;

    public TextMeshProUGUI GameTime;
    public TextMeshProUGUI m_bestTime;
}

public class GameOverVisual
{
    GameOverGUI m_gameOverGUI;

    MetaData metaData;
    GameData gameData;

    public void Init(MetaData metaData, GameData gameData)
    {
        this.metaData = metaData;
        this.gameData = gameData;

        m_gameOverGUI = new GameOverGUI();
        m_gameOverGUI.UI = AssetManager.Instance.GetGameOverUI();
        m_gameOverGUI.UI.SetActive(false);

        GUIRef guiRef = m_gameOverGUI.UI.GetComponent<GUIRef>();
        m_gameOverGUI.GameTime = guiRef.GetTextGUI("GameTime");
        m_gameOverGUI.m_bestTime = guiRef.GetTextGUI("BestTime");

        guiRef.GetButton("Retry").onClick.AddListener(Game.Instance.StartGame);
        guiRef.GetButton("Quit").onClick.AddListener(goToMainMenu);
    }

    public void Show()
    {
        m_gameOverGUI.GameTime.text = CommonVisual.GetTimeElapsedString(gameData.GameTime);
        m_gameOverGUI.m_bestTime.text = CommonVisual.GetTimeElapsedString(metaData.BestTime);
        m_gameOverGUI.UI.SetActive(true);
    }

    public void Hide()
    {
        m_gameOverGUI.UI.SetActive(false);
    }

    void goToMainMenu()
    {
        Game.Instance.SetMenuState(MENU_STATE.MAIN_MENU);
    }
}
}