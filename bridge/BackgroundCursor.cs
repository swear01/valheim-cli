using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimCliBridge
{
    [HarmonyPatch]
    public static class BackgroundCursor
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZCursor), "set_LockState");
            yield return AccessTools.Method(typeof(ZCursor), "SetVisible");
        }
        public static bool Prefix()
        {
            if (Plugin.Instance == null || Application.isFocused) return true;
            // The native camera/menu update these every frame, even without focus.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return false;
        }
    }
}
