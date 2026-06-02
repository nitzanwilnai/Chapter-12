using System.Collections;
using System.Collections.Generic;
using CommonTools;
using UnityEngine;

namespace Survivor
{
    public class PauseMenuVisual
    {
        GameObject m_UI;
        public void Init()
        {
            m_UI = AssetManager.Instance.GetPauseMenuUI();
            m_UI.SetActive(false);

            GUIRef guiRef = m_UI.GetComponent<GUIRef>();
            guiRef.GetButton("Continue").onClick.AddListener(unpauseGame);
        }

        public void Show()
        {
            m_UI.SetActive(true);
        }

        public void Hide()
        {
            m_UI.SetActive(false);
        }

        void unpauseGame()
        {
            Game.Instance.SetMenuState(MENU_STATE.IN_GAME);
        }
    }
}