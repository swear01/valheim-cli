// Game API fixture for policy tests; never included in the plugin build.
namespace UnityEngine
{
    public struct Vector3 { public static Vector3 zero => new(); public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    public struct Quaternion { }
    public sealed class Transform { public Vector3 position, eulerAngles; public Quaternion rotation; }
    public static class Time { public static float realtimeSinceStartup, time, deltaTime = 0.02f; }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x=x; this.y=y; } public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x-b.x,a.y-b.y); }
    public static class Application { public static bool isFocused = true; }
    public enum KeyCode { F12 }
    public static class Input { public static bool Panic; public static bool GetKey(KeyCode key) => Panic; }
    public static class Screen { public static int width = 1600, height = 900; }
    public class GameObject
    {
        public readonly Dictionary<Type, object> Components = new();
        public readonly Dictionary<Type, Action<EventSystems.BaseEventData>> Handlers = new();
        public T GetComponentInParent<T>() where T:class => Components.GetValueOrDefault(typeof(T)) as T;
    }
    public class Component
    {
        public GameObject gameObject = new();
        public T GetComponent<T>() where T:class => gameObject.Components.GetValueOrDefault(typeof(T)) as T;
    }
    public class Canvas : Component { }
    public sealed class Camera { public static Camera main; public Transform transform = new(); }
    public class Texture2D
    {
        public static bool FailEncode;
        public int width, height;
        public Texture2D(int width, int height, TextureFormat format, bool mipmap) { this.width = width; this.height = height; }
        public void ReadPixels(Rect rect, int x, int y) { }
        public void Apply() { }
        public byte[] EncodeToPNG() => FailEncode ? throw new InvalidOperationException("fixture capture failure") : new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
    }
    public enum TextureFormat { RGB24 }
    public sealed class RenderTexture
    {
        public static int Releases;
        public static RenderTexture active;
        public static RenderTexture GetTemporary(int width, int height, int depth) => new();
        public static void ReleaseTemporary(RenderTexture texture) { Releases++; }
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
        public readonly List<object> Destroyed = new();
        public void StartCoroutine(System.Collections.IEnumerator routine) { Coroutines.Add(routine); }
        public void Destroy(object value) { Destroyed.Add(value); }
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
public sealed class Player : UnityEngine.Component
{
    public static Player m_localPlayer;
    public static bool NullList;
    public UnityEngine.Transform transform = new();
    public bool Dead, Teleporting, Cutscene, PlaceMode, Attacking, Dodging, AcceptTeleport = true, GuardianAllowed = true, NativeInputAllowed = true;
    public float Stamina = 50;
    public readonly Inventory Inventory = new();
    public readonly ZNetView View = new();
    public readonly PlayerController Controller = new();
    public UnityEngine.GameObject Hover = new();
    public UnityEngine.Vector2 Look;
    public int Hotbar, Interactions, PlacementCalls, Placements, m_placeRotation;
    private float m_placePressedTime = -1000;
    private bool m_blocking = false;
    public bool ToggleBlock, m_autoRun;
    public bool Blocking => m_blocking;
    public bool CanPlace;
    public Player() { gameObject.Components[typeof(ZNetView)] = View; gameObject.Components[typeof(PlayerController)] = Controller; }
    private bool TakeInput() => NativeInputAllowed;
    public bool InAttack() => Attacking;
    public bool InDodge() => Dodging;
    public bool InCutscene() => Cutscene;
    public bool InPlaceMode() => PlaceMode;
    public void SetMouseLook(UnityEngine.Vector2 value) { Look = value; }
    public bool HideHandItems(bool onlyRightHand = false, bool animation = true) => true;
    public bool StartGuardianPower() => GuardianAllowed;
    public UnityEngine.GameObject GetHoverObject() => Hover;
    private void Interact(UnityEngine.GameObject target, bool hold, bool alt) { Interactions++; }
    public void UseHotbarItem(int slot) { Hotbar = slot; }
    private void UpdatePlacement(bool takeInput, float dt) { PlacementCalls++; if (CanPlace && takeInput && m_placePressedTime == UnityEngine.Time.time) Placements++; }
    public void SetControls(UnityEngine.Vector3 movedir, bool attack, bool attackHold, bool secondaryAttack, bool secondaryAttackHold, bool block, bool blockHold, bool jump, bool crouch, bool run, bool autoRun, bool dodge = false) { m_blocking = ToggleBlock ? block ? !m_blocking : m_blocking : blockHold; }
    public string GetPlayerName() => "fixture-player";
    public bool IsDead() => Dead;
    public bool IsTeleporting() => Teleporting;
    public float GetHealth() => 25;
    public float GetMaxHealth() => 25;
    public float GetStamina() => Stamina;
    public float GetMaxStamina() => 50;
    public Inventory GetInventory() => Inventory;
    public bool TeleportTo(UnityEngine.Vector3 target, UnityEngine.Quaternion rotation, bool distant) { if (AcceptTeleport) Teleporting = true; return AcceptTeleport; }
    public static List<Player> GetAllPlayers() => NullList ? null : m_localPlayer == null ? new() : new() { null, m_localPlayer };
}
public sealed class PlayerController
{
    public static bool HasInputDelay;
    public bool InputAllowed = true;
    private bool TakeInput(bool look = false) => InputAllowed;
}
public static class Console { public static bool Visible; public static bool IsVisible() => Visible; }
public static class Menu { public static bool Visible; public static bool IsVisible() => Visible; }
public sealed class Chat { public static Chat instance; public bool Focus; public bool HasFocus() => Focus; }
public sealed class Inventory
{
    public readonly List<ItemDrop.ItemData> Items = new() { new() };
    public List<ItemDrop.ItemData> GetAllItems() => Items;
    public ItemDrop.ItemData GetItemAt(int x, int y) => Items.FirstOrDefault(i=>i.m_gridPos.x==x && i.m_gridPos.y==y);
}
public static class ItemDrop
{
    public sealed class ItemData
    {
        public Shared m_shared = new(); public int m_stack = 1, m_quality = 1; public bool m_equipped; public GridPos m_gridPos;
    }
    public sealed class Shared { public string m_name = "$item_club"; }
    public struct GridPos { public int x, y; }
}

public sealed class ZNetView { public bool Owner = true; public bool IsOwner() => Owner; }
public static class TextInput { public static bool Visible; public static bool IsVisible()=>Visible; }
public static class Minimap { public static bool Open, Typing; public static bool IsOpen()=>Open; public static bool InTextInput()=>Typing; }
public static class StoreGui { public static bool Visible; public static bool IsVisible()=>Visible; }
public static class GameCamera { public static bool FreeFly; public static bool InFreeFly()=>FreeFly; }
public static class PlayerCustomizaton { public static bool Visible; public static bool IsBarberGuiVisible()=>Visible; }
public sealed class Container { }
public sealed class InventoryGui
{
    public static InventoryGui instance = new(); public static bool Visible;
    public static bool IsVisible()=>Visible; public void Hide(){Visible=false;} public void Show(Container c, int group=1){Visible=true;}
}
public sealed class Hud
{
    public static Hud instance = new(); public static bool Selector, Radial;
    public static bool IsPieceSelectionVisible()=>Selector; public static bool InRadial()=>Radial;
    public void TogglePieceSelection(){Selector=!Selector;}
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    public sealed class Harmony { public Harmony(string id) { } public void PatchAll(System.Reflection.Assembly assembly) { } public void UnpatchSelf() { } }
    public static class AccessTools
    {
        public static System.Reflection.MethodInfo Method(Type t, string name, Type[] parameters) => t.GetMethod(name, System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic, null, parameters, null);
        public static System.Reflection.FieldInfo Field(Type t, string name) => t.GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
    }
}
namespace UnityEngine.EventSystems
{
    public class BaseEventData { }
    public sealed class PointerEventData : BaseEventData
    {
        public enum InputButton { Left, Right, Middle }
        public Vector2 position, scrollDelta, pressPosition; public InputButton button;
        public RaycastResult pointerCurrentRaycast, pointerPressRaycast;
        public GameObject pointerPress;
        public bool eligibleForClick; public int clickCount;
        public PointerEventData(EventSystem system) { }
    }
    public struct RaycastResult { public GameObject gameObject; }
    public sealed class EventSystem
    {
        public static EventSystem current;
        public readonly List<RaycastResult> Hits = new();
        public void RaycastAll(PointerEventData data,List<RaycastResult> hits){hits.AddRange(Hits);}
    }
    public interface IPointerClickHandler { } public interface IPointerDownHandler { } public interface IPointerUpHandler { }
    public interface IScrollHandler { }
    public static class ExecuteEvents
    {
        public delegate void EventFunction<T>(T handler, BaseEventData data);
        public static readonly EventFunction<IPointerClickHandler> pointerClickHandler = (_,_)=>{};
        public static readonly EventFunction<IPointerDownHandler> pointerDownHandler = (_,_)=>{};
        public static readonly EventFunction<IPointerUpHandler> pointerUpHandler = (_,_)=>{};
        public static readonly EventFunction<IScrollHandler> scrollHandler = (_,_)=>{};
        public static GameObject GetEventHandler<T>(GameObject target) => target?.Handlers.ContainsKey(typeof(T))==true ? target : null;
        public static bool Execute<T>(GameObject target, BaseEventData data,EventFunction<T> handler)
        {
            if(target!=null && target.Handlers.TryGetValue(typeof(T),out var action)){action(data);return true;} return false;
        }
        public static GameObject ExecuteHierarchy<T>(GameObject target, BaseEventData data,EventFunction<T> handler) => Execute(target,data,handler) ? target : null;
    }
}
