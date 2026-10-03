using UnityEngine;

namespace FumoMP;

/// <summary>
/// Thin host object for the plugin. All visible UI is uGUI and lives in MpUgui;
/// this component only exists to have a MonoBehaviour that survives scene loads,
/// drives the per-frame ticks, and keeps the entry button visibility in sync.
/// (IMGUI is not used: on real machines it paints, which produced a duplicate
/// button, and its widgets cannot be styled or laid out reliably.)
/// </summary>
public class MpMenuUI : MonoBehaviour
{
    private static MpMenuUI _instance;

    internal static readonly string[] MapNames =
    {
        "forest",
        "mountains",
        "purplefield",
        "wintermistylake",
        "ceremony",
    };

    private float _logTimer;
    private int _guiFrames;
    private static int _uguiErrors;

    private void Awake()
    {
        _instance = this;
        Plugin.Log.LogInfo("MpMenuUI awake");
    }

    private void Start()
    {
        Plugin.Log.LogInfo("MpMenuUI Start (entered PlayerLoop)");
        MpUgui.EnsureCanvas();
    }

    private void OnDestroy()
    {
        Plugin.Log.LogInfo("MpMenuUI OnDestroy");
        if (_instance == this) _instance = null;
    }

    internal static bool Exists => _instance != null;

    /// <summary>
    /// Re-create the host object after a scene load. The plugin loads before the
    /// game's first scene, so an object created there does not survive into the
    /// menu scene; this guarantees the UI host always exists.
    /// </summary>
    internal static void EnsureExists()
    {
        if (_instance != null) return;
        var go = new GameObject("FumoMP_Lobby");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<MpMenuUI>();
        Plugin.Log.LogInfo("MpMenuUI host (re)created after scene load");
    }

    private void Update()
    {
        // scene probe: useful when diagnosing which scene the game is really in
        _logTimer += Time.deltaTime;
        if (_logTimer >= 15f)
        {
            _logTimer = 0f;
            try
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                Plugin.Log.LogInfo("scene probe: name='" + sc.name + "' loaded=" + sc.isLoaded);
            }
            catch { }
        }

        MpUgui.Tick();
        MpFlow.Tick();
        UpdateEntryButton();
    }

    /// <summary>
    /// The entry button is hidden only inside an actual race scene, and while the
    /// lobby itself is open. It is deliberately NOT driven by GameManager.inRace,
    /// which can stay true after a race and would hide the button forever.
    /// </summary>
    private void UpdateEntryButton()
    {
        try
        {
            MpUgui.SetEntryVisible(!MpUgui.InRaceScenePublic && !MpUgui.LobbyVisible);
        }
        catch (System.Exception e)
        {
            if (_uguiErrors++ < 3) Plugin.Log.LogWarning("entry visibility: " + e.GetType().Name);
        }
    }

    public static void Open() => MpUgui.ShowLobby();
    public static void Close() => MpUgui.HideLobby();
}
