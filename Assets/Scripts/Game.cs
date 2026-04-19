using UnityEngine;
using CommonTools;
using System.Collections.Generic;
using TMPro;

namespace Survivor
{
    public class Game : Singleton<Game>
    {
        public Board Board;
        public Camera MainCamera;

        MainMenuVisual m_mainMenuVisual = new MainMenuVisual();
        GameOverVisual m_gameOverVisual = new GameOverVisual();
        PauseMenuVisual m_pauseMenuVisual = new PauseMenuVisual();

        GameData m_gameData = new GameData();
        MetaData m_metaData = new MetaData();
        Balance m_balance = new Balance();

        int m_screenShotIdx = 0;

        // Start is called before the first frame update
        void Start()
        {
            m_mainMenuVisual.Init(m_metaData);
            m_gameOverVisual.Init(m_metaData, m_gameData);
            m_pauseMenuVisual.Init();

            m_balance.LoadBalance();
            Logic.AllocateGameData(m_gameData, m_balance);
            Board.Init(m_metaData, m_gameData, m_balance, MainCamera);

            MetaDataIO.Load(m_metaData);
            Logic.Init(m_metaData);

            SetMenuState(MENU_STATE.MAIN_MENU);
        }

        public void SetMenuState(MENU_STATE newMenuState)
        {
            MENU_STATE oldMenuState = m_metaData.MenuState;
            Logic.SetMenuState(m_metaData, newMenuState);
            if (oldMenuState == MENU_STATE.MAIN_MENU)
            {
                m_mainMenuVisual.Hide();
            }
            else if (oldMenuState == MENU_STATE.GAME_OVER)
            {
                Board.Hide();
                m_gameOverVisual.Hide();
            }
            else if (oldMenuState == MENU_STATE.IN_GAME && newMenuState == MENU_STATE.PAUSE_MENU)
            {
                Board.Hide();
            }
            else if (oldMenuState == MENU_STATE.PAUSE_MENU)
            {
                m_pauseMenuVisual.Hide();
            }

            if (newMenuState == MENU_STATE.MAIN_MENU)
            {
                m_mainMenuVisual.Show();
            }
            else if (newMenuState == MENU_STATE.GAME_OVER)
            {
                m_gameOverVisual.Show();
            }
            else if (newMenuState == MENU_STATE.PAUSE_MENU)
            {
                m_pauseMenuVisual.Show();
            }
            else if (newMenuState == MENU_STATE.IN_GAME)
            {
                Board.Show();
            }
        }

        public void StartGame()
        {
            Board.StartGame();
            SetMenuState(MENU_STATE.IN_GAME);
        }

        public void ContinueGame()
        {
            GameDataIO.Load(m_gameData, m_balance);
            SetMenuState(MENU_STATE.IN_GAME);
        }

        // Update is called once per frame
        void Update()
        {
            if (m_metaData.MenuState == MENU_STATE.IN_GAME)
                Board.Tick(Time.deltaTime);

            if (Input.GetKeyUp("s"))
                captureScreenshot();
        }
        void captureScreenshot()
        {
            ScreenCapture.CaptureScreenshot("screenshot" + (m_screenShotIdx++) + ".png");
        }

        void OnDestroy()
        {
            Logic.FreeGameData(m_gameData);
            m_balance.Free();
        }
    }
}