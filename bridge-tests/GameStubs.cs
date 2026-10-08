// Game API fixture for policy tests; never included in the plugin build.
namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    public struct Quaternion { }
    public sealed class Transform { public Vector3 position; public Quaternion rotation; }
}
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public T Value; public ConfigEntry(T value) { Value = value; } }
    public sealed class ConfigDescription { public ConfigDescription(string description, object range) { } }
    public sealed class AcceptableValueRange<T> { public AcceptableValueRange(T min, T max) { } }
    public sealed class ConfigFile { public ConfigEntry<T> Bind<T>(string group, string key, T value, object description) => new ConfigEntry<T>(value); }
}
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class BepInPlugin : Attribute { public BepInPlugin(string guid, string name, string version) { } }
    public sealed class TestLogger { public void LogInfo(object message) { } public void LogError(object message) { } }
    public class BaseUnityPlugin { public readonly Configuration.ConfigFile Config = new(); public readonly TestLogger Logger = new(); }
    public static class Paths { public static string ConfigPath => Path.GetTempPath(); }
}
public sealed class ZNet
{
    public static ZNet instance;
    public bool Host;
    public bool IsServer() => Host;
    public string GetWorldName() => "fixture-world";
}
public sealed class Player
{
    public static Player m_localPlayer;
    public UnityEngine.Transform transform = new();
    public bool Dead, Teleporting, AcceptTeleport = true;
    public string GetPlayerName() => "fixture-player";
    public bool IsDead() => Dead;
    public bool IsTeleporting() => Teleporting;
    public bool TeleportTo(UnityEngine.Vector3 target, UnityEngine.Quaternion rotation, bool distant) { if (AcceptTeleport) Teleporting = true; return AcceptTeleport; }
    public static List<Player> GetAllPlayers() => m_localPlayer == null ? new() : new() { null, m_localPlayer };
}
