using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;

namespace Orbit
{
    public class ClientSettings
    {
        internal static int rs2cPID = 0;
        internal static int runescapePID;
        internal static IntPtr gameHandle;
        internal static IntPtr jagOpenGL;
        internal static Process rs2client = null;
        internal static bool gameCrashed = false;
        internal static bool busyRecovering = false;
        internal static bool selfreviveRunning = false;

        internal static bool loggedin = false;
        internal static void SaveSettings(JObject settings)
        {
            string jsonString = settings.ToString();
            File.WriteAllText("settings.json", jsonString);
        }
        internal static JObject LoadSettings()
        {
            if (File.Exists("settings.json"))
            {
                string jsonString = File.ReadAllText("settings.json");
                JObject settings = JObject.Parse(jsonString);
                return settings;
            }
            else
            {
                return new JObject();
            }
        }
    }
}
