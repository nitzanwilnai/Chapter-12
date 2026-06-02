using UnityEngine;
using System.IO;

namespace Survivor
{
    public enum MENU_STATE { NONE, MAIN_MENU, IN_GAME, GAME_OVER, PAUSE_MENU };
    public class MetaData
    {
        public float BestTime;

        public MENU_STATE MenuState;
    }
}