using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using System.Collections;
using HarmonyLib;

namespace ValheimCliBridge
{
    [BepInPlugin("swear01.ValheimCliBridge", "Valheim CLI Bridge", "0.2.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private Dispatcher dispatcher;
        private Server server;
        private ConfigEntry<bool> allowTeleport;
        private ConfigEntry<bool> allowControl;
        internal static Plugin Instance;
        private Harmony harmony;
        private bool controlReady, appliedControl;
        private InputController input;
        private Player controlledPlayer;
        private bool capturing;
        private Player imagePlayer;
        private string image, capturedAt, captureError;
        private int imageWidth, imageHeight;
        private float capturedTime = -1;
        private void Awake()
        {
            var enabled = Config.Bind("Bridge", "Enabled", true, "Enable local CLI bridge. Restart after changes.");
            var port = Config.Bind("Bridge", "Port", 28761, new ConfigDescription("Local loopback port. Restart after changes.", new AcceptableValueRange<int>(1024, 65535)));
            allowTeleport = Config.Bind("Permissions", "AllowTeleport", false, "Allow the local host to teleport their own character. Client writes are refused.");
            allowControl = Config.Bind("Permissions", "AllowControl", false, "Allow bounded game-internal actions for your local character, including joining clients. Requires game focus. F12 revokes permission. Never enable while someone else is playing this character.");
            if (!enabled.Value) return;
            try
            {
                var tokenPath = Path.Combine(Paths.ConfigPath, "swear01.ValheimCliBridge.token");
                if (!File.Exists(tokenPath))
                {
                    using (var file = new FileStream(tokenPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(file)) writer.Write(Protocol.CreateToken());
                }
                var token = File.ReadAllText(tokenPath).Trim();
                if (token.Length != 64 || token.Any(c => !"0123456789abcdef".Contains(c))) throw new InvalidDataException("Invalid local token file");
                dispatcher = new Dispatcher(Execute);
                input = new InputController();
                Instance = this;
                GameInput.Validate();
                harmony = new Harmony("swear01.ValheimCliBridge");
                harmony.PatchAll(typeof(Plugin).Assembly);
                controlReady = true;
                server = new Server(port.Value, token, dispatcher, error => Logger.LogError("CLI bridge connection failed: " + error.GetType().Name), StopInput, () => input.Epoch);
                Logger.LogInfo("CLI bridge listening on 127.0.0.1:" + port.Value + "; token stored in local config folder. Never share it.");
            }
            catch (Exception error)
            {
                controlReady = false;
                Instance = null;
                harmony?.UnpatchSelf();
                input?.Dispose();
                Logger.LogError("CLI bridge did not start: " + error.GetType().Name +
                    (error is InvalidDataException ? "; stop the game, delete swear01.ValheimCliBridge.token from the config folder, and restart." : "; check the config folder permissions and whether the configured port is already in use."));
            }
        }
        private bool CanControl(Player local) => controlReady && allowControl != null && allowControl.Value && Application.isFocused &&
            local != null && ZNet.instance != null && !local.IsDead() && !local.IsTeleporting() &&
            !local.InCutscene() && local.GetComponent<ZNetView>() != null && local.GetComponent<ZNetView>().IsOwner() &&
            !Console.IsVisible() && (Chat.instance == null || !Chat.instance.HasFocus()) && !Menu.IsVisible() && !TextInput.IsVisible() && !Minimap.InTextInput();
        private static bool CanMove(Player local) => !InventoryGui.IsVisible() && !StoreGui.IsVisible() && !Minimap.IsOpen() &&
            !Hud.IsPieceSelectionVisible() && !GameCamera.InFreeFly() && !PlayerCustomizaton.IsBarberGuiVisible() && !Hud.InRadial() && GameInput.CanTakeInput(local);
        internal ControlFrame ControlFrame(Player local)
        {
            if (input == null || controlledPlayer != local) return null;
            if (input.Active && (!CanControl(local) || !CanMove(local))) input.Stop("game_input_blocked");
            if (local.GetStamina() <= 0) input.ExhaustRun();
            var frame = input.Sample();
            if (frame != null) { appliedControl = true; return frame; }
            if (appliedControl) { appliedControl = false; return new ControlFrame(); }
            return null;
        }
        private void Update()
        {
            if (allowControl != null && Input.GetKey(KeyCode.F12)) { allowControl.Value = false; input?.Stop("panic"); }
            if (input != null && controlledPlayer != Player.m_localPlayer)
            {
                input.Stop("player_changed");
                controlledPlayer = Player.m_localPlayer;
                appliedControl = false;
            }
            input?.Permit(CanControl(Player.m_localPlayer));
            dispatcher?.Pump();
        }
        private void OnApplicationFocus(bool focused) { if (!focused) input?.Stop("focus_lost"); }
        private void OnDestroy()
        {
            controlReady = false;
            input?.Dispose();
            server?.Dispose();
            if (appliedControl && controlledPlayer != null && controlledPlayer == Player.m_localPlayer)
                controlledPlayer.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false, false);
            Instance = null;
            harmony?.UnpatchSelf();
        }
        private Response StopInput(string id)
        {
            input?.Stop();
            return new Response { id = id, ok = true, state = "stopped", inputActive = false, inputState = input?.Reason, inputId = input?.Id };
        }
        private Response Execute(Request request)
        {
            var local = Player.m_localPlayer;
            var net = ZNet.instance;
            var response = new Response
            {
                id = request.id, ok = true, state = "observed", version = "0.2.0",
                inWorld = local != null && net != null,
                host = net != null && net.IsServer(),
                teleportAllowed = net != null && net.IsServer() && allowTeleport.Value,
                world = local != null && net != null ? net.GetWorldName() : null,
                player = local != null ? local.GetPlayerName() : null,
                position = local != null ? PositionOf(local.transform.position) : null,
                teleporting = local != null && local.IsTeleporting(),
                controlAllowed = CanControl(local), foreground = Application.isFocused,
                inputActive = input != null && input.Active, inputState = input?.Reason, inputId = input?.Id,
                dead = local != null && local.IsDead(),
                health = local != null ? local.GetHealth() : 0, maxHealth = local != null ? local.GetMaxHealth() : 0,
                stamina = local != null ? local.GetStamina() : 0, maxStamina = local != null ? local.GetMaxStamina() : 0,
                view = Camera.main != null ? PositionOf(Camera.main.transform.eulerAngles) : null
            };
            if (request.operation == "stop")
                return StopInput(request.id);
            if (request.IsControl)
            {
                if (!CanControl(local) || input == null) return Response.Error(request.id, "Requires local player ownership, game focus, AllowControl=true, and no menu/chat/console/text input", "cancelled");
                if ((request.operation == "input" || request.operation == "look" || request.operation == "action" && request.action != "inventory" && request.action != "build-menu") && !CanMove(local))
                    return Response.Error(request.id, "Game controller input is blocked; close blocking UI first", "cancelled");
                if (request.operation == "action" && request.action != "inventory" && !(request.action == "build-menu" && Hud.IsPieceSelectionVisible()) && !GameActions.CanTakeInput(local))
                    return Response.Error(request.id, "Game player input is blocked", "cancelled");
                if (request.operation == "ui" && !InventoryGui.IsVisible() && !StoreGui.IsVisible() && !Hud.IsPieceSelectionVisible())
                    return Response.Error(request.id, "Open a game UI first", "cancelled");
                if ((request.operation == "action" || request.operation == "ui") && input.Active)
                    return Response.Error(request.id, "Stop movement/combat before a UI or discrete action", "cancelled");
                input.Permit(true);
                try { input.CheckEpoch(request); if (request.operation == "input") input.Start(request); }
                catch (InvalidOperationException error) { return Response.Error(request.id, error.Message, "cancelled"); }
                if (request.operation == "input") controlledPlayer = local;
                else if (request.operation == "look") local.SetMouseLook(new Vector2((float)(request.yaw ?? 0), -(float)(request.pitch ?? 0)));
                else if (request.operation == "action") GameActions.Execute(local, request);
                else GameUi.Execute(request);
                image = null;
                response.state = request.operation == "input" || request.operation == "action" && request.action == "place" ? "started" : "applied";
                response.inputActive = input.Active;
                response.inputState = input.Reason;
                response.inputId = input.Id;
            }
            if (request.operation == "observe")
            {
                if (!response.inWorld) return Response.Error(request.id, "No local player in world", "cancelled");
                response.inventory = local.GetInventory().GetAllItems().Where(item => item != null).Take(512).Select(item => new InventoryItem
                {
                    name = item.m_shared.m_name, count = item.m_stack, quality = item.m_quality,
                    column = item.m_gridPos.x, row = item.m_gridPos.y, equipped = item.m_equipped
                }).ToArray();
                if (captureError != null)
                {
                    var failure = captureError;
                    captureError = null;
                    if (imagePlayer == local)
                    {
                        response.captureError = failure;
                        response.state = "capture_failed";
                        return response;
                    }
                }
                if (image != null && imagePlayer == local && Time.realtimeSinceStartup - capturedTime < 0.5f)
                {
                    response.image = image; response.imageWidth = imageWidth; response.imageHeight = imageHeight; response.capturedAt = capturedAt;
                }
                else
                {
                    response.state = "capture_pending";
                    if (!capturing) { capturing = true; StartCoroutine(Capture(local)); }
                }
            }
            if (request.operation == "players")
                response.players = (Player.GetAllPlayers() ?? Enumerable.Empty<Player>()).Where(p => p != null).Select(p => p.GetPlayerName()).ToArray();
            if (request.operation == "teleport")
            {
                if (!response.inWorld) return Response.Error(request.id, "No local player in world", "cancelled");
                if (!response.teleportAllowed) return Response.Error(request.id, "Teleport requires local host and AllowTeleport=true", "cancelled");
                if (local.IsDead() || local.IsTeleporting()) return Response.Error(request.id, "Player dead or already teleporting", "cancelled");
                input?.Stop("teleport");
                if (input != null && input.Active) return Response.Error(request.id, "Cannot release active input", "cancelled");
                image = null;
                var target = new Vector3((float)request.x.Value, (float)request.y.Value, (float)request.z.Value);
                if (!local.TeleportTo(target, local.transform.rotation, true)) return Response.Error(request.id, "Game refused teleport", "cancelled");
                response.state = "started";
                response.destination = PositionOf(target);
                response.teleporting = true;
                Logger.LogInfo("CLI teleport started; request " + request.id);
            }
            return response;
        }
        private IEnumerator Capture(Player expectedPlayer)
        {
            yield return new WaitForEndOfFrame();
            Texture2D source = null, scaled = null;
            RenderTexture target = null;
            var previous = RenderTexture.active;
            try
            {
                if (Player.m_localPlayer != expectedPlayer || expectedPlayer == null) yield break;
                source = ScreenCapture.CaptureScreenshotAsTexture();
                var scale = Math.Min(1f, Math.Min(1280f / source.width, 720f / source.height));
                imageWidth = Math.Max(1, (int)(source.width * scale)); imageHeight = Math.Max(1, (int)(source.height * scale));
                target = RenderTexture.GetTemporary(imageWidth, imageHeight, 0);
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                scaled = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);
                scaled.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
                scaled.Apply();
                image = Convert.ToBase64String(scaled.EncodeToPNG());
                imagePlayer = expectedPlayer; capturedTime = Time.realtimeSinceStartup; capturedAt = DateTime.UtcNow.ToString("o"); captureError = null;
            }
            catch (Exception error) { image = null; imagePlayer = expectedPlayer; captureError = "Screenshot failed: " + error.GetType().Name; }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (source != null) Destroy(source);
                if (scaled != null) Destroy(scaled);
                capturing = false;
            }
        }
        private static Position PositionOf(Vector3 position) => new Position { x = position.x, y = position.y, z = position.z };
    }
}
