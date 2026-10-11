using System;
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
            var lockState = AccessTools.Method(typeof(ZCursor), "set_LockState") ?? throw new MissingMethodException("Native cursor lock policy unavailable");
            var visible = AccessTools.Method(typeof(ZCursor), "SetVisible") ?? throw new MissingMethodException("Native cursor visibility policy unavailable");
            return new MethodBase[] { lockState, visible };
        }
        public static bool Prefix()
        {
            if (Plugin.Instance == null || Application.isFocused) return true;
            // The native camera/menu update these every frame, even without focus.
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
            return false;
        }
    }
}
