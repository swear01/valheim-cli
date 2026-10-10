using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using System.Collections;

namespace ValheimCliBridge
{
    [BepInPlugin("swear01.ValheimCliBridge", "Valheim CLI Bridge", "0.2.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private Dispatcher dispatcher;
        private Server server;
        private ConfigEntry<bool> allowTeleport;
        private ConfigEntry<bool> allowControl;
        private readonly IInputBackend backend = new WindowsInput();
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
            allowControl = Config.Bind("Permissions", "AllowControl", false, "Allow short Windows keyboard/mouse actions for your local character, including joining clients. Requires game foreground. F12 revokes permission. Never enable while someone else is playing this character.");
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
                input = new InputController(backend);
                server = new Server(port.Value, token, dispatcher, error => Logger.LogError("CLI bridge connection failed: " + error.GetType().Name), StopInput, () => input.Epoch);
                Logger.LogInfo("CLI bridge listening on 127.0.0.1:" + port.Value + "; token stored in local config folder. Never share it.");
            }
            catch (Exception error)
            {
                Logger.LogError("CLI bridge did not start: " + error.GetType().Name +
                    (error is InvalidDataException ? "; stop the game, delete swear01.ValheimCliBridge.token from the config folder, and restart." : "; check the config folder permissions and whether the configured port is already in use."));
            }
        }
        private bool CanControl(Player local) => allowControl != null && allowControl.Value && backend.Supported &&
            local != null && ZNet.instance != null && !local.IsDead() && !local.IsTeleporting() &&
            !Console.IsVisible() && (Chat.instance == null || !Chat.instance.HasFocus()) && !Menu.IsVisible();
        private void Update()
        {
            if (allowControl != null && (backend.PanicPressed || input != null && input.PanicLatched)) allowControl.Value = false;
            if (input != null && input.Active && controlledPlayer != Player.m_localPlayer) input.Stop("player_changed");
            input?.Permit(CanControl(Player.m_localPlayer));
            dispatcher?.Pump();
        }
        private void OnDestroy()
        {
            input?.Dispose();
            if (input != null && input.Active) Logger.LogError("CLI input release failed during unload; tap and release the affected keys physically.");
            server?.Dispose();
        }
        private Response StopInput(string id)
        {
            input?.Stop();
            var active = input != null && input.Active;
            return new Response { id = id, ok = !active, state = active ? "release_failed" : "stopped", inputActive = active, inputState = input?.Reason, inputId = input?.Id,
                error = active ? "Key release failed; release the keys physically and inspect the game" : null };
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
                controlAllowed = CanControl(local), foreground = backend.Foreground,
                inputActive = input != null && input.Active, inputState = input?.Reason, inputId = input?.Id,
                dead = local != null && local.IsDead(),
                health = local != null ? local.GetHealth() : 0, maxHealth = local != null ? local.GetMaxHealth() : 0,
                stamina = local != null ? local.GetStamina() : 0, maxStamina = local != null ? local.GetMaxStamina() : 0,
                view = Camera.main != null ? PositionOf(Camera.main.transform.eulerAngles) : null
            };
            if (request.operation == "stop")
                return StopInput(request.id);
            if (request.operation == "input" || request.operation == "mouse")
            {
                if (!CanControl(local) || input == null) return Response.Error(request.id, "Requires Windows, a living local player, AllowControl=true, and no menu/chat/console", "cancelled");
                input.Permit(true);
                try { if (request.operation == "mouse") input.Motion(request); else input.Start(request); }
                catch (InvalidOperationException error) { return Response.Error(request.id, error.Message, "cancelled"); }
                catch (IOException) { return Response.Error(request.id, "Input may have started; use stop/status before a new action. Do not retry automatically."); }
                if (request.operation == "input") controlledPlayer = local;
                image = null;
                response.state = request.operation == "mouse" ? "applied" : "started";
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
