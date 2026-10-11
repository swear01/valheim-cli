using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var lockSetter = AccessTools.Method(typeof(Cursor), "set_lockState") ?? throw new MissingMethodException("Unity cursor lock API unavailable");
            var visibilitySetter = AccessTools.Method(typeof(Cursor), "set_visible") ?? throw new MissingMethodException("Unity cursor visibility API unavailable");
            var replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, lockSetter))
                {
                    instruction.operand = AccessTools.Method(typeof(BackgroundCursor), nameof(SetLockState));
                    replaced++;
                }
                else if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, visibilitySetter))
                {
                    instruction.operand = AccessTools.Method(typeof(BackgroundCursor), nameof(SetVisible));
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new MissingMethodException("Native cursor write path unavailable");
        }
        public static void SetLockState(CursorLockMode value)
        {
            if (Plugin.Instance != null && !Application.isFocused)
            {
                value = CursorLockMode.None;
                if (Cursor.lockState == value) return;
            }
            Cursor.lockState = value;
        }
        public static void SetVisible(bool value)
        {
            if (Plugin.Instance != null && !Application.isFocused)
            {
                value = true;
                if (Cursor.visible == value) return;
            }
            Cursor.visible = value;
        }
    }
}
