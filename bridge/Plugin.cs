using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ValheimCliBridge
{
    [BepInPlugin("swear01.ValheimCliBridge", "Valheim CLI Bridge", "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private Dispatcher dispatcher;
        private Server server;
        private ConfigEntry<bool> allowTeleport;
        private void Awake()
        {
            var enabled = Config.Bind("Bridge", "Enabled", true, "Enable local CLI bridge. Restart after changes.");
            var port = Config.Bind("Bridge", "Port", 28761, new ConfigDescription("Local loopback port. Restart after changes.", new AcceptableValueRange<int>(1024, 65535)));
            allowTeleport = Config.Bind("Permissions", "AllowTeleport", false, "Allow the local host to teleport their own character. Client writes are refused.");
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
                server = new Server(port.Value, token, dispatcher, error => Logger.LogError("CLI bridge connection failed: " + error.GetType().Name));
                Logger.LogInfo("CLI bridge listening on 127.0.0.1:" + port.Value + "; token stored in local config folder. Never share it.");
            }
            catch (Exception error)
            {
                Logger.LogError("CLI bridge did not start: " + error.GetType().Name +
                    (error is InvalidDataException ? "; stop the game, delete swear01.ValheimCliBridge.token from the config folder, and restart." : "; check the config folder permissions and whether the configured port is already in use."));
            }
        }
        private void Update() { dispatcher?.Pump(); }
        private void OnDestroy() { server?.Dispose(); }
        private Response Execute(Request request)
        {
            var local = Player.m_localPlayer;
            var net = ZNet.instance;
            var response = new Response
            {
                id = request.id, ok = true, state = "observed", version = "0.1.0",
                inWorld = local != null && net != null,
                host = net != null && net.IsServer(),
                teleportAllowed = net != null && net.IsServer() && allowTeleport.Value,
                world = local != null && net != null ? net.GetWorldName() : null,
                player = local != null ? local.GetPlayerName() : null,
                position = local != null ? PositionOf(local.transform.position) : null,
                teleporting = local != null && local.IsTeleporting()
            };
            if (request.operation == "players")
                response.players = (Player.GetAllPlayers() ?? Enumerable.Empty<Player>()).Where(p => p != null).Select(p => p.GetPlayerName()).ToArray();
            if (request.operation == "teleport")
            {
                if (!response.inWorld) return Response.Error(request.id, "No local player in world", "cancelled");
                if (!response.teleportAllowed) return Response.Error(request.id, "Teleport requires local host and AllowTeleport=true", "cancelled");
                if (local.IsDead() || local.IsTeleporting()) return Response.Error(request.id, "Player dead or already teleporting", "cancelled");
                var target = new Vector3((float)request.x.Value, (float)request.y.Value, (float)request.z.Value);
                if (!local.TeleportTo(target, local.transform.rotation, true)) return Response.Error(request.id, "Game refused teleport", "cancelled");
                response.state = "started";
                response.destination = PositionOf(target);
                response.teleporting = true;
                Logger.LogInfo("CLI teleport started; request " + request.id);
            }
            return response;
        }
        private static Position PositionOf(Vector3 position) => new Position { x = position.x, y = position.y, z = position.z };
    }
}
