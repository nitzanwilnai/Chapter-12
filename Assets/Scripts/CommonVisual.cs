using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Survivor
{
    public static class CommonVisual
    {
        public static string GetTimeElapsedString(float time)
        {
            string timeString = "";
            int m = Mathf.FloorToInt(time / 60.0f);
            int s = Mathf.FloorToInt(time - m * 60.0f);
            if (m >= 10)
                timeString += m;
            else
                timeString += "0" + m;
            timeString += ":";
            if (s >= 10)
                timeString += s;
            else
                timeString += "0" + s;

            return timeString;
        }
    }
}