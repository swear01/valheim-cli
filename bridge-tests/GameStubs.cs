// Game API fixture for policy tests; never included in the plugin build.
namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    public struct Quaternion { }
    public sealed class Transform { public Vector3 position, eulerAngles; public Quaternion rotation; }
    public static class Time { public static float realtimeSinceStartup; }
    public sealed class Camera { public static Camera main; public Transform transform = new(); }
    public class Texture2D
    {
        public int width, height;
        public Texture2D(int width, int height, TextureFormat format, bool mipmap) { this.width = width; this.height = height; }
        public void ReadPixels(Rect rect, int x, int y) { }
        public void Apply() { }
        public byte[] EncodeToPNG() => new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
    }
    public enum TextureFormat { RGB24 }
    public sealed class RenderTexture
    {
        public static RenderTexture active;
        public static RenderTexture GetTemporary(int width, int height, int depth) => new();
        public static void ReleaseTemporary(RenderTexture texture) { }
    }
    public static class Graphics { public static void Blit(Texture2D source, RenderTexture target) { } }
    public struct Rect { public Rect(int x, int y, int width, int height) { } }
    public sealed class WaitForEndOfFrame { }
    public static class ScreenCapture { public static Texture2D CaptureScreenshotAsTexture() => new(1600, 900, TextureFormat.RGB24, false); }
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
    public class BaseUnityPlugin
    {
        public readonly Configuration.ConfigFile Config = new(); public readonly TestLogger Logger = new();
        public readonly List<System.Collections.IEnumerator> Coroutines = new();
        public void StartCoroutine(System.Collections.IEnumerator routine) { Coroutines.Add(routine); }
        public void Destroy(object value) { }
    }
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
    public static bool NullList;
    public UnityEngine.Transform transform = new();
    public bool Dead, Teleporting, AcceptTeleport = true;
    public string GetPlayerName() => "fixture-player";
    public bool IsDead() => Dead;
    public bool IsTeleporting() => Teleporting;
    public float GetHealth() => 25;
    public float GetMaxHealth() => 25;
    public float GetStamina() => 50;
    public float GetMaxStamina() => 50;
    public Inventory GetInventory() => new();
    public bool TeleportTo(UnityEngine.Vector3 target, UnityEngine.Quaternion rotation, bool distant) { if (AcceptTeleport) Teleporting = true; return AcceptTeleport; }
    public static List<Player> GetAllPlayers() => NullList ? null : m_localPlayer == null ? new() : new() { null, m_localPlayer };
}
public static class Console { public static bool Visible; public static bool IsVisible() => Visible; }
public static class Menu { public static bool Visible; public static bool IsVisible() => Visible; }
public sealed class Chat { public static Chat instance; public bool Focus; public bool HasFocus() => Focus; }
public sealed class Inventory { public List<ItemDrop.ItemData> GetAllItems() => new() { new() }; }
public static class ItemDrop
{
    public sealed class ItemData
    {
        public Shared m_shared = new(); public int m_stack = 1, m_quality = 1; public bool m_equipped; public GridPos m_gridPos;
    }
    public sealed class Shared { public string m_name = "$item_club"; }
    public struct GridPos { public int x, y; }
}
