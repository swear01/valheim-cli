using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimCliBridge
{
    // Change the arguments at the original game call, preserving Player.SetControls and other patches.
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    public static class GameInput
    {
        private static readonly System.Reflection.FieldInfo Blocking = AccessTools.Field(typeof(Player), "m_blocking");
        internal static void Validate() { if (Blocking == null) throw new InvalidOperationException("Game blocking state unavailable"); }
        public static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold,
            ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold,
            ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || __instance != Player.m_localPlayer) return;
            var frame = plugin.ControlFrame(__instance);
            if (frame == null) return;
            movedir = new Vector3(frame.X, 0, frame.Z);
            attack = frame.Attack; attackHold = frame.AttackHold; secondaryAttack = frame.Secondary; secondaryAttackHold = frame.SecondaryHold;
            __instance.m_autoRun = false;
            block = __instance.ToggleBlock ? (bool)Blocking.GetValue(__instance) != frame.BlockHold : frame.Block;
            blockHold = frame.BlockHold; jump = frame.Jump; crouch = frame.Crouch; run = frame.Run; autoRun = false; dodge = frame.Dodge;
        }
    }

    public static class GameActions
    {
        private static readonly System.Reflection.MethodInfo Interact = AccessTools.Method(typeof(Player), "Interact", new[] { typeof(GameObject), typeof(bool), typeof(bool) });
        private static readonly System.Reflection.MethodInfo Placement = AccessTools.Method(typeof(Player), "UpdatePlacement", new[] { typeof(bool), typeof(float) });
        private static readonly System.Reflection.FieldInfo PlacePressed = AccessTools.Field(typeof(Player), "m_placePressedTime");
        private static readonly System.Reflection.FieldInfo Rotation = AccessTools.Field(typeof(Player), "m_placeRotation");
        public static void Execute(Player local, Request r)
        {
            switch (r.action)
            {
                case "interact":
                    var target = local.GetHoverObject();
                    if (target == null || Interact == null) throw new ActionRefusedException("No interactable object under the game's crosshair");
                    Interact.Invoke(local, new object[] { target, false, false }); break;
                case "slot":
                    if (local.GetInventory().GetItemAt(r.slot.Value - 1, 0) == null) throw new ActionRefusedException("Hotbar slot is empty");
                    local.UseHotbarItem(r.slot.Value); break;
                case "inventory":
                    if (InventoryGui.instance == null) throw new ActionRefusedException("Inventory UI unavailable");
                    if (InventoryGui.IsVisible()) InventoryGui.instance.Hide(); else InventoryGui.instance.Show(null); break;
                case "build-menu":
                    if (!local.InPlaceMode() || Hud.instance == null) throw new ActionRefusedException("Equip a building tool first");
                    Hud.instance.TogglePieceSelection(); break;
                case "hide": local.HideHandItems(); break;
                case "guardian": if (!local.StartGuardianPower()) throw new InvalidOperationException("Game refused guardian power"); break;
                case "place":
                    if (!local.InPlaceMode() || Placement == null || PlacePressed == null) throw new ActionRefusedException("Building tool or placement API unavailable");
                    PlacePressed.SetValue(local, Time.time);
                    Placement.Invoke(local, new object[] { true, Time.deltaTime }); break;
                case "rotate":
                    if (!local.InPlaceMode() || Rotation == null) throw new ActionRefusedException("Building tool or rotation API unavailable");
                    Rotation.SetValue(local, (int)Rotation.GetValue(local) + r.scroll.Value); break;
                default: throw new ActionRefusedException("Unknown game action");
            }
        }
    }
}
