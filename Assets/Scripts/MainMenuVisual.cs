using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using CommonTools;
using UnityEngine.UI;

namespace Survivor
{
    public class MainMenuGUI
    {
        public GameObject UI;
        public TextMeshProUGUI BestTime;
        public Button ContinueButton;
    }

    public class MainMenuVisual
    {
        MainMenuGUI m_mainMenuGUI;

        MetaData metaData;

        public void Init(MetaData metaData)
        {
            this.metaData = metaData;

            m_mainMenuGUI = new MainMenuGUI();
            m_mainMenuGUI.UI = AssetManager.Instance.GetMainMenuUI();
            m_mainMenuGUI.UI.SetActive(false);

            GameObject GO = MonoBehaviour.Instantiate(m_mainMenuGUI.UI);

            GUIRef guiRef = m_mainMenuGUI.UI.GetComponent<GUIRef>();
            m_mainMenuGUI.BestTime = guiRef.GetTextGUI("BestTime");

            guiRef.GetButton("Start").onClick.AddListener(Game.Instance.StartGame);
            m_mainMenuGUI.ContinueButton = guiRef.GetButton("Continue");
            m_mainMenuGUI.ContinueButton.onClick.AddListener(Game.Instance.ContinueGame);
        }

        public void Show()
        {
            m_mainMenuGUI.UI.SetActive(true);

            m_mainMenuGUI.BestTime.text = CommonVisual.GetTimeElapsedString(metaData.BestTime);
            m_mainMenuGUI.ContinueButton.interactable = GameDataIO.SaveGameExists();
        }

        public void Hide()
        {
            m_mainMenuGUI.UI.SetActive(false);
        }
    }
}