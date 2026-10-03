using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FumoMP;

/// <summary>
/// uGUI lobby + entry button, with the constraints this build forced on us:
///  1. IMGUI only hit-tests, it never paints -> everything visible is uGUI.
///  2. Il2Cpp delegate construction is missing -> no Button.onClick and no
///     TMP_InputField: clicks are hit-tested manually and typing is read from
///     Input.inputString.
///  3. Dynamic TMP fonts start empty -> CJK support is verified on the source
///     UnityEngine.Font via HasCharacter().
/// </summary>
public static class MpUgui
{
    // ------------------------------------------------------------------ theme
    private static readonly Color ColPanel   = new Color(0.055f, 0.065f, 0.100f, 0.985f);
    private static readonly Color ColCard    = new Color(0.080f, 0.095f, 0.145f, 1f);
    private static readonly Color ColInput   = new Color(0.030f, 0.038f, 0.062f, 1f);
    private static readonly Color ColBorder  = new Color(0.20f, 0.25f, 0.35f, 1f);
    private static readonly Color ColBorderOn= new Color(0.38f, 0.70f, 1.00f, 1f);
    private static readonly Color ColBorderBad=new Color(0.90f, 0.35f, 0.35f, 1f);
    private static readonly Color ColText    = new Color(0.93f, 0.95f, 1.00f, 1f);
    private static readonly Color ColDim     = new Color(0.58f, 0.64f, 0.76f, 1f);
    private static readonly Color ColAccent  = new Color(1.00f, 0.84f, 0.40f, 1f);
    private static readonly Color ColHost    = new Color(0.16f, 0.56f, 0.32f, 1f);
    private static readonly Color ColJoin    = new Color(0.18f, 0.44f, 0.78f, 1f);
    private static readonly Color ColBack    = new Color(0.42f, 0.24f, 0.26f, 1f);
    private static readonly Color ColStart   = new Color(0.60f, 0.45f, 0.12f, 1f);
    private static readonly Color ColNav     = new Color(0.19f, 0.23f, 0.32f, 1f);
    private static readonly Color ColGhost   = new Color(0.14f, 0.17f, 0.24f, 1f);

    private static readonly Color[] PingColors =
    {
        new Color(0.45f, 0.90f, 0.55f, 1f),  // < 60ms
        new Color(0.95f, 0.85f, 0.40f, 1f),  // < 150ms
        new Color(0.95f, 0.45f, 0.45f, 1f),  // >= 150ms
    };

    // ------------------------------------------------------------- structures
    private class Btn
    {
        public GameObject Go;
        public Image Bg;
        public TextMeshProUGUI Label;
        public Rect Rect;
        public Vector2 Size, Pos;
        public Action OnClick;
        public Color Base;
        public bool Enabled = true;
        public bool Pressed;
        public string Tip = "";
    }

    private class Field
    {
        public string Value = "";
        public string Placeholder = "";
        public int MaxLen = 32;
        public Rect Rect;
        public Vector2 Size, Pos;
        public Image Border;
        public Image Bg;
        public TextMeshProUGUI Label;
        public GameObject Go;
        public bool Invalid;
        public bool Enabled = true;   // false => read-only while in a session
    }

    private static readonly List<Btn> Btns = new List<Btn>();
    private static readonly List<Field> Fields = new List<Field>();

    private static GameObject _canvasGo, _lobbyGo, _entryGo;
    private static Rect _entryRect;
    private static Image _entryBg;
    private static TextMeshProUGUI _entryLabel;
    private static Image _statusPill;
    private static TextMeshProUGUI _statusText, _hintText;
    private static TextMeshProUGUI _rosterTitle, _rosterText, _localIpText;
    private static TextMeshProUGUI _mapText, _nickLabel, _mapLabel, _ipLabel, _portLabel, _title;
    private static Field _nickField, _ipField, _portField;
    private static Field _focused;
    private static int _focusIdx = -1;
        private static TMP_FontAsset _font;
    private static bool _prefsLoaded, _lobbyShown;
    private static float _nextRoster, _animPhase;
    private static int _tickErrors;

    private static readonly string[] MapNames = { "forest", "mountains", "purplefield", "wintermistylake", "ceremony" };
    private static int _mapIdx;

    /// <summary>Kept for compatibility; the UI is English-only now.</summary>
    internal static bool UseCjk = false;

    // ---------------------------------------------------------------- helpers
    private static GameObject MakeChild(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static RectTransform Place(GameObject go, Vector2 size, Vector2 pos,
                                       Vector2? anchor = null, Vector2? pivot = null)
    {
        var rt = go.GetComponent<RectTransform>();
        var a = anchor ?? new Vector2(0.5f, 0.5f);
        rt.anchorMin = a; rt.anchorMax = a;
        rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    private static Rect CentreRect(Vector2 size, Vector2 offset)
    {
        float cx = Screen.width * 0.5f + offset.x;
        float cy = Screen.height * 0.5f + offset.y;
        return new Rect(cx - size.x * 0.5f, cy - size.y * 0.5f, size.x, size.y);
    }

    private static Image MakeImage(string name, Transform parent, Color color, Vector2 size,
                                   Vector2 pos, Vector2? anchor = null, Vector2? pivot = null)
    {
        var go = MakeChild(name, parent);
        var img = go.AddComponent<Image>();
        img.color = color;   // flat colour: no sprites, no gradients, no rounding
        Place(go, size, pos, anchor, pivot);
        return img;
    }

    internal static TextMeshProUGUI MakeText(string name, Transform parent, string text, float size,
                                             Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = MakeChild(name, parent);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.richText = true;
        t.overflowMode = TextOverflowModes.Ellipsis;
        var f = GetFont();
        if (f != null) t.font = f;
        return t;
    }

    private static void Stretch(TextMeshProUGUI t, float padX = 0f)
    {
        var rt = t.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, 0f); rt.offsetMax = new Vector2(-padX, 0f);
    }

    private static Btn MakeButton(string name, Transform parent, string label, Vector2 size, Vector2 pos,
                                  Action onClick, Color tint, float fontSize = 19f)
    {
        var img = MakeImage(name, parent, tint, size, pos);
        var txt = MakeText("Label", img.transform, label, fontSize, Color.white);
        Stretch(txt, 8f);
        var b = new Btn { Go = img.gameObject, Bg = img, Label = txt, Size = size, Pos = pos,
                          Rect = CentreRect(size, pos), OnClick = onClick, Base = tint };
        Btns.Add(b);
        return b;
    }

    private static Field MakeField(string name, Transform parent, Vector2 size, Vector2 pos,
                                   string initial, string placeholder, int maxLen)
    {
        var border = MakeImage(name + "Border", parent, ColBorder, size, pos);
        var bg = MakeImage(name, border.transform, ColInput, new Vector2(size.x - 4f, size.y - 4f), Vector2.zero);
        var label = MakeText("Text", bg.transform, initial, 19f, ColText, TextAlignmentOptions.Left);
        var lrt = label.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(12f, 0f); lrt.offsetMax = new Vector2(-10f, 0f);

        var f = new Field
        {
            Value = initial,
            Placeholder = placeholder,
            MaxLen = maxLen,
            Size = size,
            Pos = pos,
            Rect = CentreRect(size, pos),
            Border = border,
            Bg = bg,
            Label = label,
            Go = border.gameObject
        };
        Fields.Add(f);
        return f;
    }

    // ------------------------------------------------------------ font lookup
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CanShowCjk(TMP_FontAsset f)
    {
        if (f == null) return false;
        return f.HasCharacter('\u8054', false, true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TMP_FontAsset FontFromSceneText()
    {
        var all = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>(true);
        if (all == null) return null;
        TMP_FontAsset any = null;
        for (int i = 0; i < all.Length; i++)
        {
            var f = all[i] != null ? all[i].font : null;
            if (f == null) continue;
            if (any == null) any = f;
            if (CanShowCjk(f)) return f;
        }
        return any;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TMP_FontAsset FontFromDefault() => TMP_Settings.defaultFontAsset;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TMP_FontAsset FontFromOs()
    {
        string[] wanted = { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun", "Noto Sans CJK SC", "WenQuanYi Zen Hei", "Arial Unicode MS" };
        var osFont = Font.CreateDynamicFontFromOSFont(wanted, 32);
        if (osFont == null)
        {
            Plugin.Log.LogInfo("font(os): CreateDynamicFontFromOSFont returned null");
            return null;
        }

        bool srcCjk = false;
        try { srcCjk = osFont.HasCharacter('\u8054'); } catch { }
        if (!srcCjk)
        {
            Plugin.Log.LogInfo("font(os): '" + osFont.name + "' has no CJK glyphs");
            return null;
        }

        // 1) the documented factory - returns null in this build
        TMP_FontAsset asset = null;
        try { asset = TMP_FontAsset.CreateFontAsset(osFont); }
        catch (Exception e) { Plugin.Log.LogInfo("font(os): factory threw " + e.GetType().Name); }
        if (asset != null)
        {
            bool facCjk = false;
            try { facCjk = asset.HasCharacter('\u8054', false, true); } catch { }
            Plugin.Log.LogInfo("font(os): factory asset '" + asset.name + "' cjkReady=" + facCjk);
            if (facCjk) return asset;
        }

        // 2) clone a shipped TMP asset and point its source font at the OS font;
        //    in Dynamic mode TMP pulls the CJK glyphs from that font on demand
        try
        {
            var template = TMP_Settings.defaultFontAsset;
            if (template == null) { Plugin.Log.LogInfo("font(os): no template asset"); return null; }
            var mine = UnityEngine.Object.Instantiate(template);
            if (mine == null) { Plugin.Log.LogInfo("font(os): Instantiate failed"); return null; }
            mine.sourceFontFile = osFont;
            mine.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            bool ok = false;
            try { ok = mine.HasCharacter('\u8054', false, true); } catch { }
            Plugin.Log.LogInfo("font(os): cloned '" + template.name + "' source='" + osFont.name + "' cjkReady=" + ok);
            // Only use it when it can actually produce CJK glyphs, otherwise the
            // Chinese labels would render as the notorious empty boxes. On failure
            // fall through to the "repoint the default asset" attempt below.
            if (ok) return mine;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("font(os) clone failed: " + e.GetType().Name + ": " + e.Message);
        }

        // 3) last resort: repoint the game's own default asset at the OS font.
        //    This also changes the game UI font, so it is only kept when it really
        //    produces CJK glyphs (verified below).
        try
        {
            var def = TMP_Settings.defaultFontAsset;
            if (def != null)
            {
                def.sourceFontFile = osFont;
                def.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                bool ok = false;
                try { ok = def.HasCharacter('\u8054', false, true); } catch { }
                Plugin.Log.LogInfo("font(os): repointed default asset cjkReady=" + ok);
                if (ok) return def;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("font(os) repoint: " + e.GetType().Name); }
        return null;
    }

    internal static TMP_FontAsset GetFont()
    {
        if (_font != null) return _font;
        try { var sc = FontFromSceneText(); if (CanShowCjk(sc)) { _font = sc; UseCjk = true; Plugin.Log.LogInfo("uGUI font: " + sc.name + " (scene CJK)"); return _font; } }
        catch (Exception e) { Plugin.Log.LogWarning("font(scene): " + e.GetType().Name); }
        try
        {
            var os = FontFromOs();
            if (os != null)
            {
                _font = os;
                UseCjk = true;   // verified to carry CJK glyphs
                Plugin.Log.LogInfo("uGUI font: " + os.name + " (CJK verified)");
                return _font;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("font(os): " + e.GetType().Name + ": " + e.Message); }
        try { var def = FontFromDefault(); if (def != null) _font = def; }
        catch (Exception e) { Plugin.Log.LogWarning("font(default): " + e.GetType().Name); }
        UseCjk = false;
        Plugin.Log.LogInfo(_font != null
            ? ("uGUI font: " + _font.name + " (Latin only -> English UI, no boxes)")
            : "uGUI font: none (English UI)");
        return _font;
    }

    // -------------------------------------------------------------- prefs / ip
    private static void LoadPrefs()
    {
        if (_prefsLoaded) return;
        _prefsLoaded = true;
        try
        {
            string nick = PlayerPrefs.GetString("FumoMP.nick", "");
            string ip = PlayerPrefs.GetString("FumoMP.ip", "");
            int port = PlayerPrefs.GetInt("FumoMP.port", 0);
            int map = PlayerPrefs.GetInt("FumoMP.map", 0);
            int fumo = PlayerPrefs.GetInt("FumoMP.fumo", 0);
            int skin = PlayerPrefs.GetInt("FumoMP.skin", 0);
            if (!string.IsNullOrEmpty(nick) && string.IsNullOrEmpty(MpFlow.Nickname)) MpFlow.Nickname = nick;
            if (!string.IsNullOrEmpty(ip)) MpFlow.Address = ip;
            if (port > 0 && port <= 65535) MpFlow.Port = (ushort)port;
            _mapIdx = Mathf.Clamp(map, 0, MapNames.Length - 1);
            MpFlow.CharacterIdx = Mathf.Max(0, fumo);
            MpFlow.SkinIdx = Mathf.Clamp(skin, 0, 7);
            MpFlow.SelectedMap = "Assets/Scenes/" + MapNames[_mapIdx] + ".unity";
        }
        catch (Exception e) { Plugin.Log.LogWarning("prefs load: " + e.GetType().Name); }
    }

    private static void SavePrefs()
    {
        try
        {
            PlayerPrefs.SetString("FumoMP.nick", MpFlow.Nickname ?? "");
            PlayerPrefs.SetString("FumoMP.ip", MpFlow.Address ?? "");
            PlayerPrefs.SetInt("FumoMP.port", MpFlow.Port);
            PlayerPrefs.SetInt("FumoMP.map", _mapIdx);
            PlayerPrefs.SetInt("FumoMP.fumo", MpFlow.CharacterIdx);
            PlayerPrefs.SetInt("FumoMP.skin", MpFlow.SkinIdx);
            PlayerPrefs.Save();
        }
        catch (Exception e) { Plugin.Log.LogWarning("prefs save: " + e.GetType().Name); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string LocalIpCore()
    {
        var addrs = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName());
        if (addrs == null) return null;
        for (int i = 0; i < addrs.Length; i++)
        {
            var a = addrs[i];
            if (a == null) continue;
            if (a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
            string s = a.ToString();
            if (!s.StartsWith("127.")) return s;
        }
        return null;
    }

    /// <summary>
    /// What the host must tell friends: only meaningful once the server is up, so
    /// it is shown in the Hosting state rather than as permanent noise. The best
    /// candidate (a real LAN address) comes first; VPN and other adapters follow.
    /// </summary>
    private static string ShareBlock()
    {
        var sb = new System.Text.StringBuilder();
        string[] ips = null;
        try { ips = MpDiag.LocalIps(); } catch { }
        if (ips == null || ips.Length == 0)
        {
            sb.Append("Friends connect to:  <no reachable address found>");
            return sb.ToString();
        }
        sb.Append("Friends connect to:");
        int shown = Mathf.Min(ips.Length, 3);
        for (int i = 0; i < shown; i++)
        {
            var parts = ips[i].Split('|');
            string ip = parts.Length > 0 ? parts[0] : ips[i];
            string adapter = parts.Length > 1 ? parts[1] : "";
            string kind = parts.Length > 2 ? parts[2] : "";
            string tag = kind == "lan" ? "LAN" : kind == "vpn" ? "VPN" : "other";
            sb.Append("\n  ").Append(ip).Append(':').Append(MpFlow.Port)
              .Append("   (").Append(tag);
            if (!string.IsNullOrEmpty(adapter)) sb.Append(' ').Append(adapter);
            sb.Append(')');
        }
        if (ips.Length > shown) sb.Append("\n  (+").Append(ips.Length - shown).Append(" more)");
        sb.Append("\n  port ").Append(MpFlow.Port).Append(" (game) and ").Append(MpFlow.Port + 1)
          .Append(" (kart positions) - allow both in the firewall");
        return sb.ToString();
    }


    // ------------------------------------------------------------- lifecycle
    internal static void EnsureCanvas()
    {
        if (_canvasGo != null) return;
        try
        {
            Btns.Clear(); Fields.Clear();
            LoadPrefs();
            _canvasGo = new GameObject("FumoMP_Canvas");
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            _canvasGo.AddComponent<CanvasScaler>();
            _canvasGo.AddComponent<GraphicRaycaster>();
            Plugin.Log.LogInfo("uGUI canvas ready");
            BuildEntryButton();
            BuildLobby();
            BuildResults();
            BuildNetStatus();
            BuildRankBoard();
            RefreshFields();
            ApplyState();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("uGUI canvas failed: " + e);
            _canvasGo = null;
        }
    }

    private static void BuildEntryButton()
    {
        var img = MakeImage("EntryButton", _canvasGo.transform, ColJoin, new Vector2(250f, 48f),
                            new Vector2(-18f, 18f),
                            anchor: new Vector2(1f, 0f), pivot: new Vector2(1f, 0f));
        _entryGo = img.gameObject;
        _entryBg = img;
        _entryLabel = MakeText("Label", img.transform, "FumoMP Lobby", 20f, Color.white);
        Stretch(_entryLabel, 8f);
        _entryRect = new Rect(Screen.width - 18f - 250f, 18f, 250f, 48f);
        Plugin.Log.LogInfo("uGUI entry button built (bottom-right)");
    }

    /// <summary>
    /// True while a race scene is loaded. GameManager.inRace is not dependable as a
    /// visibility source (it can stay true after the race), so the scene name is
    /// used instead and the button comes back as soon as you leave the track.
    /// </summary>
    /// <summary>
    /// Keys the game must not see while our lobby is open. Without this, editing
    /// text leaks keys into the game's own menu handling - Backspace/Escape are
    /// "back" there, which closes menus and even quits the game.
    /// </summary>
    internal static bool ShouldSwallowKey(KeyCode key)
    {
        if (!LobbyVisible) return false;
        switch (key)
        {
            case KeyCode.Backspace:
            case KeyCode.Delete:
            case KeyCode.Escape:
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
            case KeyCode.Tab:
            case KeyCode.Space:
            case KeyCode.UpArrow:
            case KeyCode.DownArrow:
            case KeyCode.LeftArrow:
            case KeyCode.RightArrow:
            case KeyCode.Home:
            case KeyCode.End:
                return true;
        }
        return false;
    }

    internal static bool InRaceScenePublic => InRaceScene();

    private static bool InRaceScene()
    {
        try
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            switch (sc.name)
            {
                case "forest":
                case "mountains":
                case "purplefield":
                case "wintermistylake":
                case "ceremony":
                    return true;
            }
        }
        catch { }
        return false;
    }

    internal static void SetEntryVisible(bool visible)
    {
        try { if (_entryGo != null && _entryGo.activeSelf != visible) _entryGo.SetActive(visible); }
        catch { }
    }

    internal static void ShowLobby()
    {
        EnsureCanvas();
        try
        {
            if (_lobbyGo != null) _lobbyGo.SetActive(true);
            _lobbyShown = true;
            SetEntryVisible(false);
            LogButtonRects();
            SyncFieldsFromFlow();
            ApplyState();
            Refresh();
            MpFlow.EnsureMpScene();
        }
        catch (Exception e) { Plugin.Log.LogError("ShowLobby failed: " + e); }
    }

    internal static void HideLobby()
    {
        try
        {
            Blur();
            if (_lobbyGo != null) _lobbyGo.SetActive(false);
            _lobbyShown = false;
            SetEntryVisible(true);
            SavePrefs();
        }
        catch { }
    }

    internal static void Toggle()
    {
        if (LobbyVisible) HideLobby(); else ShowLobby();
    }

    internal static bool LobbyVisible
    {
        get { try { return _lobbyGo != null && _lobbyGo.activeSelf; } catch { return false; } }
    }

    private static void SyncFieldsFromFlow()
    {
        if (string.IsNullOrEmpty(MpFlow.Nickname))
            MpFlow.Nickname = "Player" + UnityEngine.Random.Range(100, 999);
        if (_nickField != null) _nickField.Value = MpFlow.Nickname;
        if (_ipField != null) _ipField.Value = MpFlow.Address;
        if (_portField != null) _portField.Value = MpFlow.Port.ToString();
        _mapIdx = Mathf.Clamp(Array.IndexOf(MapNames, LeafOf(MpFlow.SelectedMap)), 0, MapNames.Length - 1);
        if (_charText != null) _charText.text = CharacterLabel();
        RefreshFields();
    }

    private static string LeafOf(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath)) return MapNames[0];
        int i = scenePath.LastIndexOf('/');
        string leaf = i >= 0 ? scenePath.Substring(i + 1) : scenePath;
        return leaf.Replace(".unity", "");
    }

    // ------------------------------------------------------------------ input
    private static int _lastW, _lastH;

    /// <summary>
    /// Recompute all hit rectangles when the window/screen size changes: the game
    /// lets the player change resolution at runtime, and a rect captured at build
    /// time would then no longer match where the widget is drawn.
    /// </summary>
    private static void CheckRelayout()
    {
        if (Screen.width == _lastW && Screen.height == _lastH) return;
        _lastW = Screen.width;
        _lastH = Screen.height;
        for (int i = 0; i < Btns.Count; i++)
            Btns[i].Rect = CentreRect(Btns[i].Size, Btns[i].Pos);
        for (int i = 0; i < Fields.Count; i++)
            Fields[i].Rect = CentreRect(Fields[i].Size, Fields[i].Pos);
        _entryRect = new Rect(Screen.width - 18f - 250f, 18f, 250f, 48f);
        Plugin.Log.LogInfo("uGUI relayout for " + Screen.width + "x" + Screen.height);
    }

    internal static void Tick()
    {
        try
        {
            _animPhase += Time.unscaledDeltaTime;
            CheckRelayout();
            HandleKeyboard();
            HandleMouse();
            HandleHover();
            ApplyState();
            UpdateNetStatus();
            UpdateRankBoard();
            if (LobbyVisible) Refresh();
        }
        catch (Exception e)
        {
            if (_tickErrors++ < 3) Plugin.Log.LogWarning("uGUI tick failed: " + e.GetType().Name + ": " + e.Message);
        }
    }

    private static void HandleKeyboard()
    {
        if (Input.GetKeyDown(KeyCode.F8))
        {
            Plugin.Log.LogInfo("F8 pressed -> toggle lobby");
            Toggle();
            return;
        }
        if (!LobbyVisible) return;

        // Tab cycles input fields, Esc closes, Enter confirms/acts
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            _focusIdx = (_focusIdx + 1) % Fields.Count;
            Focus(Fields[_focusIdx]);
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_focused != null) Blur(); else HideLobby();
            return;
        }
        if (Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Return))
        {
            if (_focused != null) { Blur(); return; }
            if (MpFlow.IsHosting || MpFlow.IsClient) { MpFlow.RequestStart(); HideLobby(); }
            else if (MpFlow.Nickname != null && MpFlow.Nickname.Length > 0) { CommitAll(); MpFlow.RequestHost(); ApplyState(); }
            return;
        }

        if (_focused == null) return;
        string typed = Input.inputString;
        if (string.IsNullOrEmpty(typed)) return;

        bool dirty = false;
        for (int i = 0; i < typed.Length; i++)
        {
            char c = typed[i];
            if (c == '\b')
            {
                if (_focused.Value.Length > 0) { _focused.Value = _focused.Value.Substring(0, _focused.Value.Length - 1); dirty = true; }
            }
            else if (c == '\n' || c == '\r') { Blur(); return; }
            else if (c >= ' ' && c != 127)
            {
                if (_focused.Value.Length < _focused.MaxLen) { _focused.Value += c; dirty = true; }
            }
        }
        if (dirty) RefreshFields();
    }

    private static void Focus(Field f)
    {
        if (f == null || !f.Enabled) return;
        _focused = f;
        _focusIdx = Fields.IndexOf(f);
        RefreshFields();
    }

    private static void Blur()
    {
        if (_focused == null) return;
        CommitFocused();
        _focused = null;
        RefreshFields();
        ApplyState();
        SavePrefs();
    }

    private static void CommitFocused()
    {
        if (_focused == null) return;
        string v = (_focused.Value ?? "").Trim();
        if (_focused == _nickField)
        {
            if (v.Length == 0) v = "Player" + UnityEngine.Random.Range(100, 999);
            MpFlow.Nickname = v;
            _nickField.Value = v;
        }
        else if (_focused == _ipField)
        {
            if (v.Length == 0) v = "127.0.0.1";
            MpFlow.Address = v;
            _ipField.Value = v;
        }
        else if (_focused == _portField)
        {
            if (ushort.TryParse(v, out var p) && p > 0) MpFlow.Port = p;
            _portField.Value = MpFlow.Port.ToString();
        }
    }

    /// <summary>Commit every field (called before actions so the latest text is used).</summary>
    private static void CommitAll()
    {
        var keep = _focused;
        _focused = _nickField; CommitFocused();
        _focused = _ipField; CommitFocused();
        _focused = _portField; CommitFocused();
        _focused = keep;
        RefreshFields();
    }

    private static bool IsValidIp(string v)
    {
        try { return System.Net.IPAddress.TryParse(v, out _); } catch { return v == "localhost"; }
    }

    private static void RefreshFields()
    {
        for (int i = 0; i < Fields.Count; i++) RefreshField(Fields[i]);
    }

    private static void RefreshField(Field f)
    {
        if (f == null) return;
        bool on = (_focused == f);
        bool bad = false;
        string v = f.Value ?? "";
        if (f == _ipField && !on && v.Length > 0) bad = !IsValidIp(v);
        if (f == _portField && !on && v.Length > 0) bad = !(ushort.TryParse(v, out var p) && p > 0);
        f.Invalid = bad;
        if (!f.Enabled)
        {
            if (f.Border != null) f.Border.color = new Color(0.16f, 0.19f, 0.26f, 1f);
            if (f.Bg != null) f.Bg.color = new Color(0.045f, 0.052f, 0.072f, 1f);
            if (f.Label != null) { f.Label.text = f.Value ?? ""; f.Label.color = new Color(0.72f, 0.77f, 0.88f, 1f); }
            return;
        }

        if (f.Border != null) f.Border.color = bad ? ColBorderBad : (on ? ColBorderOn : ColBorder);
        if (f.Bg != null) f.Bg.color = on ? new Color(0.045f, 0.055f, 0.085f, 1f) : ColInput;
        if (f.Label != null)
        {
            if (string.IsNullOrEmpty(v) && !on)
            {
                f.Label.text = f.Placeholder;
                f.Label.color = ColDim;
            }
            else
            {
                bool caret = on && (((int)(_animPhase * 2f)) % 2 == 0);
                f.Label.text = caret ? v + "|" : v;
                f.Label.color = bad ? ColBorderBad : ColText;
            }
        }
    }

    private static void HandleMouse()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        var mp = Input.mousePosition;
        Vector2 p = new Vector2(mp.x, mp.y);

        if (LobbyVisible || ResultsVisible)
        {
            if (LobbyVisible)
            for (int i = Fields.Count - 1; i >= 0; i--)
            {
                var f = Fields[i];
                if (f.Go != null && f.Go.activeInHierarchy && f.Rect.Contains(p)) { Focus(f); return; }
            }
            for (int i = Btns.Count - 1; i >= 0; i--)
            {
                var b = Btns[i];
                if (b.Go == null || !b.Go.activeInHierarchy || !b.Enabled) continue;
                if (!b.Rect.Contains(p)) continue;
                b.Pressed = true;
                if (_focused != null) Blur();
                CommitAll();
                Plugin.Log.LogInfo("uGUI click: " + b.Go.name);
                var cb = b.OnClick;
                if (cb != null) cb();
                return;
            }
            if (_focused != null) Blur();
        }
        else if (_entryGo != null && _entryGo.activeInHierarchy && _entryRect.Contains(p))
        {
            Plugin.Log.LogInfo("uGUI entry button clicked -> open lobby");
            ShowLobby();
        }
    }

    private static void HandleHover()
    {
        var mp = Input.mousePosition;
        Vector2 p = new Vector2(mp.x, mp.y);
        bool lobby = LobbyVisible;
        bool down = Input.GetMouseButton(0);

        for (int i = 0; i < Btns.Count; i++)
        {
            var b = Btns[i];
            if (b.Bg == null || b.Go == null) continue;
            bool hot = lobby && b.Go.activeInHierarchy && b.Enabled && b.Rect.Contains(p);
            float k = hot ? (down ? 0.78f : 1.14f) : 1f;
            var c = b.Enabled ? b.Base : new Color(b.Base.r * 0.45f, b.Base.g * 0.45f, b.Base.b * 0.45f, 0.75f);
            b.Bg.color = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
            if (b.Label != null) b.Label.color = b.Enabled ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }
        if (_entryBg != null && _entryGo != null && !lobby && _entryGo.activeInHierarchy)
        {
            bool hot = _entryRect.Contains(p);
            float k = hot ? (down ? 0.8f : 1.15f) : 1f;
            _entryBg.color = new Color(Mathf.Clamp01(ColJoin.r * k), Mathf.Clamp01(ColJoin.g * k), Mathf.Clamp01(ColJoin.b * k), 1f);
        }
    }

    // -------------------------------------------------------------- lobby build
    private static void BuildLobby()
    {
        // compact panel: fits comfortably even at 1024x768
        float W = 560f, H = 560f;
        float half = W * 0.5f, halfH = H * 0.5f;
        float left = -half + 22f;

        var panel = MakeImage("Lobby", _canvasGo.transform, ColPanel, new Vector2(W, H), Vector2.zero);
        _lobbyGo = panel.gameObject;

        // ---- header
        var header = MakeImage("Header", panel.transform, new Color(0.105f, 0.135f, 0.205f, 1f),
                               new Vector2(W - 6f, 44f), new Vector2(0f, halfH - 25f));
        _title = MakeText("Title", header.transform, "FumoMP Lobby", 22f, ColText, TextAlignmentOptions.Left);
        Stretch(_title, 18f);
        MakeButton("CloseBtn", panel.transform, "X", new Vector2(32f, 30f), new Vector2(half - 28f, halfH - 25f), () => HideLobby(), ColGhost, 16f);
        Divider(panel.transform, W - 6f, halfH - 47f);

        // ---- fields (44px rows, labels right-aligned in one column)
        float y = halfH - 74f;

        _nickField = MakeField("NickField", panel.transform, new Vector2(300f, 36f), new Vector2(left + 250f, y),
                               MpFlow.Nickname, "your name", 16);
        _rowFumo.Add(Label(panel.transform, "NAME", left + 52f, y, 90f).gameObject);
        _rowFumo.Add(_nickField.Go);
        _rowFumo.Add(MakeButton("RandBtn", panel.transform, "R", new Vector2(40f, 36f), new Vector2(left + 435f, y),
                   () => { MpFlow.Nickname = "Player" + UnityEngine.Random.Range(100, 999); _nickField.Value = MpFlow.Nickname; RefreshFields(); }, ColNav, 17f).Go);

        y -= 44f;                                     // fumo
        _charText = MakeText("CharName", panel.transform, CharacterLabel(), 18f, ColAccent);
        Place(_charText.gameObject, new Vector2(220f, 28f), new Vector2(left + 250f, y));
        _rowFumo.Add(Label(panel.transform, "FUMO", left + 52f, y, 90f).gameObject);
        _rowFumo.Add(MakeButton("CharPrev", panel.transform, "<", new Vector2(40f, 36f), new Vector2(left + 130f, y), () => CycleCharacter(-1), ColNav, 20f).Go);
        _rowFumo.Add(_charText.gameObject);
        _rowFumo.Add(MakeButton("CharNext", panel.transform, ">", new Vector2(40f, 36f), new Vector2(left + 380f, y), () => CycleCharacter(1), ColNav, 20f).Go);

        y -= 44f;                                     // map (host's choice)
        _mapText = MakeText("MapName", panel.transform, MapLabels(_mapIdx), 18f, ColAccent);
        Place(_mapText.gameObject, new Vector2(220f, 28f), new Vector2(left + 250f, y));
        _rowMap.Add(Label(panel.transform, "MAP", left + 52f, y, 90f).gameObject);
        _rowMap.Add(MakeButton("MapPrev", panel.transform, "<", new Vector2(40f, 36f), new Vector2(left + 130f, y),
                   () => { _mapIdx = (_mapIdx + MapNames.Length - 1) % MapNames.Length; ApplyMap(); }, ColNav, 20f).Go);
        _rowMap.Add(_mapText.gameObject);
        _rowMap.Add(MakeButton("MapNext", panel.transform, ">", new Vector2(40f, 36f), new Vector2(left + 380f, y),
                   () => { _mapIdx = (_mapIdx + 1) % MapNames.Length; ApplyMap(); }, ColNav, 20f).Go);

        y -= 44f;                                     // port + host ip
        _portField = MakeField("PortField", panel.transform, new Vector2(76f, 36f), new Vector2(left + 165f, y),
                               MpFlow.Port.ToString(), "7777", 5);
        _rowPort.Add(Label(panel.transform, "PORT", left + 52f, y, 90f).gameObject);
        _rowPort.Add(_portField.Go);

        _ipField = MakeField("IpField", panel.transform, new Vector2(160f, 36f), new Vector2(left + 405f, y),
                             MpFlow.Address, "127.0.0.1", 15);
        _rowIp.Add(Label(panel.transform, "HOST IP", left + 265f, y, 84f).gameObject);
        _rowIp.Add(_ipField.Go);
        // The lap total the in-race HUD prints comes from the game mode's own field
        // (a 5 lap default) and not from the value we hand to StartGame: writing
        // _defaultLaps sticks, but the HUD still counts its own. Say so instead of
        // letting the row look like a setting that does nothing.
        _lapsHint = MakeText("LapsHint", panel.transform,
                             "laps: the game counts its own total in the HUD (" + MpFlow.Laps + " requested)",
                             12f, new Color(0.55f, 0.60f, 0.70f, 1f), TextAlignmentOptions.Left);
        Place(_lapsHint.gameObject, new Vector2(360f, 16f), new Vector2(left + 280f, y - 30f));

        _mapHint = MakeText("MapHint", panel.transform, "", 13f, ColDim, TextAlignmentOptions.Left);
        Place(_mapHint.gameObject, new Vector2(300f, 18f), new Vector2(left + 250f, y - 24f));
        _rowMap.Add(_mapHint.gameObject);
        _ipHint = null;

        y -= 46f;                                     // share block (host only)
        _localIpText = MakeText("LocalIp", panel.transform, "", 14f, ColDim, TextAlignmentOptions.TopLeft);
        Place(_localIpText.gameObject, new Vector2(W - 44f, 44f), new Vector2(0f, y - 8f));
        _rowShare.Add(_localIpText.gameObject);
        Divider(panel.transform, W - 6f, y - 30f);

        // ---- roster
        y -= 52f;
        _rosterTitleRef = MakeText("RosterHead", panel.transform, "PLAYERS" + "  0/8", 17f, ColText, TextAlignmentOptions.Left);
        Place(_rosterTitleRef.gameObject, new Vector2(280f, 22f), new Vector2(left + 140f, y));
        _rosterTitle = MakeText("RosterRole", panel.transform, "", 15f, ColAccent, TextAlignmentOptions.Right);
        Place(_rosterTitle.gameObject, new Vector2(280f, 22f), new Vector2(half - 162f, y));

        var card = MakeImage("RosterCard", panel.transform, ColCard, new Vector2(W - 44f, 116f), new Vector2(0f, y - 72f));
        _rosterText = MakeText("Roster", card.transform, "", 15f, ColDim, TextAlignmentOptions.TopLeft);
        Place(_rosterText.gameObject, new Vector2(W - 76f, 96f), new Vector2(0f, 0f));

        // ---- actions
        float by = y - 158f;
        Divider(panel.transform, W - 6f, by + 26f);
        _btnHost = MakeButton("HostBtn", panel.transform, "Host", new Vector2(150f, 42f), new Vector2(-158f, by),
                   () => OnPrimaryHost(), ColHost, 17f);
        _btnJoin = MakeButton("JoinBtn", panel.transform, "Join", new Vector2(150f, 42f), new Vector2(0f, by),
                   () => OnPrimaryJoin(), ColJoin, 17f);
        _btnBack = MakeButton("BackBtn", panel.transform, "Back", new Vector2(150f, 42f), new Vector2(158f, by),
                   () => { MpFlow.StopAll(); HideLobby(); }, ColBack, 17f);

        by -= 46f;
        _btnStart = MakeButton("StartBtn", panel.transform, "Start race (Keypad Enter)",
                   new Vector2(516f, 42f), new Vector2(0f, by),
                   () => { CommitAll(); MpFlow.RequestStart(); ApplyState(); Refresh(); }, ColStart, 16f);

        by -= 32f;
        _statusPill = MakeImage("StatusPill", panel.transform, ColCard, new Vector2(W - 44f, 30f), new Vector2(0f, by));
        _statusText = MakeText("Status", _statusPill.transform, "", 15f, ColText, TextAlignmentOptions.Left);
        Stretch(_statusText, 12f);

        by -= 26f;
        _hintText = MakeText("Hint", panel.transform,
            "Tab: field · Enter: confirm · Esc/F8: toggle", 13f, ColDim);
        Place(_hintText.gameObject, new Vector2(W - 44f, 18f), new Vector2(0f, by));

        panel.gameObject.SetActive(false);
        Plugin.Log.LogInfo("uGUI lobby built (" + Btns.Count + " buttons, " + Fields.Count + " fields)");
    }

    private static TextMeshProUGUI _rosterTitleRef;

    private static TextMeshProUGUI _charText;

    // controls whose visibility depends on the session role
    private static Btn _btnHost, _btnJoin, _btnStart, _btnBack;

    private static int _charCount = -1;

    private static int CharCount()
    {
        if (_charCount <= 0)
        {
            try { _charCount = MpDiag.CharacterCount(); } catch { _charCount = 0; }
            if (_charCount <= 0) _charCount = 8;      // fallback: cycle a small range
        }
        return _charCount;
    }

    private static string CharacterLabel()
    {
        int i = Mathf.Clamp(MpFlow.CharacterIdx, 0, CharCount() - 1);
        string n = null;
        try { n = MpDiag.CharacterName(i); } catch { }
        string skin = MpFlow.SkinIdx > 0 ? "  " + "skin" + " " + MpFlow.SkinIdx : "";
        return (string.IsNullOrEmpty(n) ? ("Fumo " + (i + 1)) : n) + skin;
    }

    private static void CycleCharacter(int delta)
    {
        int n = CharCount();
        // right-click cycles the skin instead of the character
        if (Input.GetMouseButton(1))
        {
            MpFlow.SkinIdx = (MpFlow.SkinIdx + (delta > 0 ? 1 : 7)) % 8;
        }
        else
        {
            MpFlow.CharacterIdx = ((MpFlow.CharacterIdx + delta) % n + n) % n;
        }
        if (_charText != null) _charText.text = CharacterLabel();
        // NOTE: the racer info struct is only written when hosting/joining
        // (MpFlow.Host/Join). Writing it on every click proved unsafe on real
        // clients, so cycling here just updates local state and the label.
        SavePrefs();
    }

    private static string MapLabels(int i)
    {
        if (i < 0 || i >= MapNames.Length) return "?";
        return MapNames[i];
    }

    private static void Divider(Transform parent, float width, float y)
    {
        MakeImage("Div", parent, new Color(0.16f, 0.19f, 0.26f, 1f), new Vector2(width, 1f), new Vector2(0f, y));
    }

    // ---------------------------------------------------------- net status line
    // A small always-on line while a race is running: how many machines are in the
    // session, the ping to the host, and an explicit warning if the kart channel
    // stops hearing anybody (which is exactly the case that used to look like
    // "the other player is frozen").
    private static TextMeshProUGUI _netStatus;
    private static float _lastNetStatusOk;

    private static void BuildNetStatus()
    {
        _netStatus = MakeText("NetStatus", _canvasGo.transform, "", 15f, ColDim, TextAlignmentOptions.Left);
        var rt = _netStatus.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(560f, 24f);
        rt.anchoredPosition = new Vector2(12f, -10f);
        _netStatus.gameObject.SetActive(false);
    }

    private static void UpdateNetStatus()
    {
        try
        {
            if (_netStatus == null) return;
            bool show = InRaceScene() && MpNet.Running;
            if (_netStatus.gameObject.activeSelf != show) _netStatus.gameObject.SetActive(show);
            if (!show) return;

            float ping = 0f;
            var peers = MpNet.Peers;
            bool host = MpNet.IsHost;
            if (!host)
                foreach (var p in peers) if (p.IsHost) ping = p.RttMs;

            bool quiet = false;
            try { quiet = MpDiag.NetQuiet; } catch { }
            string text = "FumoMP  " + (host ? "hosting" : "joined") + "  players " + (peers.Count + 1)
                        + "  port " + MpNet.Port + "  " + (host ? "" : (ping > 0.5f ? ((int)ping + "ms") : "ping ..."));
            _netStatus.color = quiet ? ColBorderBad : ColDim;
            _netStatus.text = quiet ? text + "   (no data from the other machines)" : text;
        }
        catch { }
    }

    // ----------------------------------------------------------- live standings
    private static TextMeshProUGUI _rankText;
    private static GameObject _rankGo;
    private static float _nextRankRefresh;

    private static void BuildRankBoard()
    {
        var bg = MakeImage("RankBoard", _canvasGo.transform, new Color(0.03f, 0.05f, 0.09f, 0.62f),
                           new Vector2(250f, 190f), Vector2.zero,
                           anchor: new Vector2(1f, 1f), pivot: new Vector2(1f, 1f));
        bg.GetComponent<RectTransform>().anchoredPosition = new Vector2(-14f, -46f);
        _rankGo = bg.gameObject;

        _rankText = MakeText("RankText", bg.transform, "", 15f, ColText, TextAlignmentOptions.TopLeft);
        var rt = _rankText.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(10f, 8f); rt.offsetMax = new Vector2(-10f, -8f);
        _rankText.enableWordWrapping = false;
        _rankGo.SetActive(false);
    }

    private static void UpdateRankBoard()
    {
        try
        {
            if (_rankGo == null) return;
            bool show = InRaceScene() && MpNet.Running && MpRank.Count > 1;
            if (_rankGo.activeSelf != show) _rankGo.SetActive(show);
            if (!show) return;
            if (Time.unscaledTime < _nextRankRefresh) return;
            _nextRankRefresh = Time.unscaledTime + 0.25f;
            _rankText.text = "<mspace=0.55em>" + MpRank.Board() + "</mspace>";
        }
        catch { }
    }

    // ------------------------------------------------------------ race results
    // The game's own end-of-race screen (CeremonyResults) builds its table from
    // the racer list and the podium rows, and it is the one piece of the race
    // flow this build never finishes: with fewer racers than podium rows it fills
    // nothing, so the player is left with a mask and a "Next" button and no
    // ranking at all. This panel is our own, so a race always ends with a readable
    // result: place, name, lap, time, state - and one button back to the menu.
    private static GameObject _resultsGo;
    private static TextMeshProUGUI _resultsTitle, _resultsBody, _resultsHint;
    private static bool _resultsShown;              // our panel is meant to be on screen

    private static void BuildResults()
    {
        float W = 620f, H = 420f;
        float halfH = H * 0.5f;

        var dim = MakeImage("ResultsDim", _canvasGo.transform, new Color(0.02f, 0.03f, 0.05f, 0.72f),
                            new Vector2(4000f, 4000f), Vector2.zero);
        var panel = MakeImage("Results", dim.transform, ColPanel, new Vector2(W, H), Vector2.zero);
        _resultsGo = dim.gameObject;

        var header = MakeImage("Header", panel.transform, new Color(0.105f, 0.135f, 0.205f, 1f),
                               new Vector2(W - 6f, 46f), new Vector2(0f, halfH - 26f));
        _resultsTitle = MakeText("Title", header.transform, "RACE RESULTS", 22f, ColText, TextAlignmentOptions.Left);
        Stretch(_resultsTitle, 18f);
        Divider(panel.transform, W - 6f, halfH - 50f);

        _resultsBody = MakeText("Body", panel.transform, "", 19f, ColText, TextAlignmentOptions.TopLeft);
        var brt = _resultsBody.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.5f, 0.5f); brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.sizeDelta = new Vector2(W - 44f, 250f);
        brt.anchoredPosition = new Vector2(0f, halfH - 62f);

        _resultsHint = MakeText("Hint", panel.transform, "", 16f, ColDim, TextAlignmentOptions.Left);
        Place(_resultsHint.gameObject, new Vector2(W - 44f, 22f), new Vector2(0f, -halfH + 52f));

        MakeButton("ResultsBackBtn", panel.transform, "BACK TO MENU", new Vector2(230f, 40f),
                   new Vector2(0f, -halfH + 24f), () => MpDiag.StepReturnToMenu(), ColBack, 19f);

        _resultsGo.SetActive(false);
    }

    internal static void ShowResults(string title, string body, string hint)
    {
        EnsureCanvas();
        try
        {
            if (_resultsGo == null) return;
            SetEntryVisible(false);
            if (_resultsTitle != null) _resultsTitle.text = title;
            if (_resultsBody != null) _resultsBody.text = body;
            if (_resultsHint != null) _resultsHint.text = hint;
            _resultsGo.SetActive(true);
            _resultsShown = true;
            Plugin.Log.LogInfo("results panel showed:\n" + body.Replace("<mspace=0.55em>", "").Replace("</mspace>", ""));
        }
        catch (Exception e) { Plugin.Log.LogWarning("ShowResults: " + e.Message); }
    }

    /// <summary>
    /// Safety net for the end of a race. The game's overlay hunt keeps running for a
    /// few seconds after the panel is up, and 0.4.1 matched our own panel by name
    /// ('.../ResultsDim/Results') and switched it off right after it appeared - which
    /// is the bug 0.4.2 fixes. If anything hides it again, this puts it back and says
    /// so, instead of letting the player watch it flash by.
    /// </summary>
    internal static void KeepResultsUp()
    {
        try
        {
            if (!_resultsShown || _resultsGo == null || _resultsGo.activeSelf) return;
            _resultsGo.SetActive(true);
            Plugin.Log.LogWarning("results panel: something switched our own panel off - put it back "
                                  + "(0.4.2 fixed the known cause; if this line shows up, send the log)");
        }
        catch (Exception e) { Plugin.Log.LogWarning("KeepResultsUp: " + e.Message); }
    }

    /// <summary>
    /// Print where our own buttons ended up. They are plain Images with our own
    /// mouse handling, so they never show up in a uGUI button scan - which makes
    /// an automated click impossible to aim without this line.
    /// </summary>
    private static void LogButtonRects()
    {
        try
        {
            if (!MpDiag.DebugLogging) return;
            var sb = new System.Text.StringBuilder("lobby buttons (xdotool click=x,y):");
            for (int i = 0; i < Btns.Count; i++)
            {
                var b = Btns[i];
                if (b.Go == null || !b.Go.activeInHierarchy) continue;
                int x = (int)b.Rect.center.x;
                int y = Screen.height - (int)b.Rect.center.y;
                sb.Append("\n   ").Append(b.Go.name).Append('=').Append(x).Append(',').Append(y);
            }
            Plugin.Log.LogInfo(sb.ToString());
        }
        catch (Exception e) { Plugin.Log.LogWarning("button rects: " + e.Message); }
    }

    internal static void HideResults()
    {
        _resultsShown = false;
        try { if (_resultsGo != null) _resultsGo.SetActive(false); } catch { }
    }

    internal static bool ResultsVisible
    {
        get { try { return _resultsGo != null && _resultsGo.activeSelf; } catch { return false; } }
    }

    private static TextMeshProUGUI Label(Transform parent, string text, float x, float y, float width = 120f)
    {
        var t = MakeText("Lbl_" + text, parent, text, 18f, ColDim, TextAlignmentOptions.Right);
        Place(t.gameObject, new Vector2(width, 26f), new Vector2(x, y));
        return t;
    }

    private static TextMeshProUGUI SectionTitle(Transform parent, string text, float x, float y)
    {
        var t = MakeText("Sec_" + text, parent, text, 16f, new Color(0.45f, 0.58f, 0.78f, 1f), TextAlignmentOptions.Left);
        Place(t.gameObject, new Vector2(300f, 22f), new Vector2(x, y));
        return t;
    }

    private static readonly List<GameObject> _rowFumo = new List<GameObject>();
    private static readonly List<GameObject> _rowMap = new List<GameObject>();
    private static readonly List<GameObject> _rowIp = new List<GameObject>();
    private static readonly List<GameObject> _rowPort = new List<GameObject>();
    private static readonly List<GameObject> _rowShare = new List<GameObject>();
    private static TextMeshProUGUI _secInfo, _secRoom, _mapHint, _lapsHint, _ipHint;

    private static void SetRowVisible(List<GameObject> row, bool v)
    {
        for (int i = 0; i < row.Count; i++) if (row[i] != null) row[i].SetActive(v);
    }

    private static void ApplyMap()
    {
        MpFlow.SelectedMap = "Assets/Scenes/" + MapNames[_mapIdx] + ".unity";
        if (_mapText != null) _mapText.text = MapLabels(_mapIdx);
        SavePrefs();
    }

    /// <summary>
    /// The lobby is a pure function of MpFlow.State: each state shows exactly the
    /// actions that are valid in it, so there is nothing to guess and no action
    /// can silently do nothing.
    /// </summary>
    private static void ApplyState()
    {
        try
        {
            var st = MpFlow.State;
            bool session = MpFlow.InSession;

            // ---- editable only outside a session
            if (_nickField != null) _nickField.Enabled = !session;
            SetBtn("RandBtn", "R", ColNav, !session);
            SetBtn("CharPrev", "<", ColNav, !session);
            SetBtn("CharNext", ">", ColNav, !session);
            SetBtn("MapPrev", "<", ColNav, !session);
            SetBtn("MapNext", ">", ColNav, !session);
            if (_portField != null) _portField.Enabled = !MpFlow.IsClient;
            bool joining = st == Session.Idle || st == Session.Failed;
            SetRowVisible(_rowIp, joining);
            if (_ipField != null) _ipField.Enabled = joining;

            if (_lapsHint != null)
                _lapsHint.text = "laps " + MpFlow.Laps + " (the in-race HUD always counts the track default)";
            if (_mapHint != null)
                _mapHint.text = st == Session.Hosting ? "you host: this track is used"
                              : MpFlow.IsClient ? "the host chose this track"
                              : "used when you host";

            // ---- actions per state
            switch (st)
            {
                case Session.Idle:
                case Session.Failed:
                    SetBtnVisible("HostBtn", true); SetBtn("HostBtn", "Host", ColHost, true);
                    SetBtnVisible("JoinBtn", true); SetBtn("JoinBtn", "Join", ColJoin, true);
                    SetBtnVisible("StartBtn", true); SetBtn("StartBtn", "Start race", ColStart, false);
                    SetBtnVisible("BackBtn", true); SetBtn("BackBtn", "Close", ColBack, true);
                    break;

                case Session.StartingHost:
                case Session.StartingClient:
                case Session.Connecting:
                    SetBtnVisible("HostBtn", true); SetBtn("HostBtn", "Cancel", ColBack, true);
                    SetBtnVisible("JoinBtn", false);
                    SetBtnVisible("StartBtn", true); SetBtn("StartBtn", "Start race", ColStart, false);
                    SetBtnVisible("BackBtn", true); SetBtn("BackBtn", "Close", ColBack, true);
                    break;

                case Session.Hosting:
                    SetBtnVisible("HostBtn", true); SetBtn("HostBtn", "Stop hosting", ColBack, true);
                    SetBtnVisible("JoinBtn", false);
                    SetBtnVisible("StartBtn", true);
                    SetBtn("StartBtn", "Start race (Keypad Enter)", ColStart, true);
                    SetBtnVisible("BackBtn", true); SetBtn("BackBtn", "Close", ColBack, true);
                    break;

                case Session.Connected:
                    SetBtnVisible("HostBtn", false);
                    SetBtnVisible("JoinBtn", true); SetBtn("JoinBtn", "Disconnect", ColBack, true);
                    SetBtnVisible("StartBtn", true);
                    SetBtn("StartBtn", "waiting for the host…", ColStart, false);
                    SetBtnVisible("BackBtn", true); SetBtn("BackBtn", "Close", ColBack, true);
                    break;
            }

            SetBtn("CloseBtn", "X", ColGhost, true);

            // ---- status line + colour
            string text; Color col;
            switch (st)
            {
                case Session.StartingHost:
                    text = "starting host…"; col = new Color(0.34f, 0.30f, 0.10f, 1f); break;
                                case Session.Hosting:
                    if (MpFlow.RaceStartPending)
                    {
                        text = "Starting the race...";
                        col = new Color(0.34f, 0.30f, 0.10f, 1f);
                    }
                    else
                    {
                        text = "Hosting \u00b7 port " + MpFlow.Port + "   players " + MpFlow.PlayerCount + "/8   " +
                               "share the address above, then Start race";
                        col = new Color(0.10f, 0.26f, 0.16f, 1f);
                    }
                    break;
                case Session.StartingClient:
                case Session.Connecting:
                    text = "connecting to " + MpFlow.Address + ":" + MpFlow.Port + " …";
                    col = new Color(0.34f, 0.30f, 0.10f, 1f); break;
                case Session.Connected:
                    text = "connected to " + MpFlow.Address + "   " +
                           "waiting for the host to start";
                    col = new Color(0.10f, 0.20f, 0.34f, 1f); break;
                case Session.Failed:
                    text = "failed: " + MpFlow.LastError; col = new Color(0.36f, 0.12f, 0.12f, 1f); break;
                default:
                    text = "pick a name/map, then Host or Join";
                    col = ColCard; break;
            }
            if (_statusText != null) _statusText.text = text;
            if (_statusPill != null) _statusPill.color = col;
            // the share block belongs to the Hosting state only: before that there
            // is nothing to dial, and afterwards it would just be noise
            bool hosting = st == Session.Hosting;
            SetRowVisible(_rowShare, hosting);
            if (_localIpText != null)
            {
                _localIpText.color = ColAccent;
                string share = hosting ? ShareBlock() : "";
                _localIpText.text = share;
                if (hosting && !_shareLogged)
                {
                    _shareLogged = true;
                    Plugin.Log.LogInfo("share block:\n" + share);
                }
                if (!hosting) _shareLogged = false;
            }
        }
        catch (Exception e)
        {
            if (_tickErrors++ < 3) Plugin.Log.LogWarning("ApplyState: " + e.GetType().Name);
        }
    }

    private static bool _shareLogged;

    private static void OnPrimaryHost()
    {
        CommitAll();
        ApplyMap();
        var st = MpFlow.State;
        if (st == Session.Hosting || st == Session.StartingHost
            || st == Session.StartingClient || st == Session.Connecting || st == Session.Connected)
            MpFlow.StopAll();
        else
            MpFlow.RequestHost();
        ApplyState();
        Refresh();
    }

    private static void OnPrimaryJoin()
    {
        CommitAll();
        var st = MpFlow.State;
        if (st == Session.Connected || st == Session.Connecting || st == Session.StartingClient)
            MpFlow.StopAll();
        else
            MpFlow.RequestJoin();
        ApplyState();
        Refresh();
    }

    /// <summary>Hide/show a button entirely (states use this instead of empty labels).</summary>
    private static void SetBtnVisible(string name, bool visible)
    {
        for (int i = 0; i < Btns.Count; i++)
        {
            var b = Btns[i];
            if (b.Go == null || b.Go.name != name) continue;
            if (b.Go.activeSelf != visible) b.Go.SetActive(visible);
            return;
        }
    }

    private static void SetBtn(string name, string label, Color baseCol, bool enabled)
    {
        for (int i = 0; i < Btns.Count; i++)
        {
            var b = Btns[i];
            if (b.Go == null || b.Go.name != name) continue;
            if (label != null && b.Label != null && b.Label.text != label) b.Label.text = label;
            b.Base = baseCol;
            b.Enabled = enabled;
            return;
        }
    }

    // ------------------------------------------------------------------ refresh
    private static string BuildStatus()
    {
        if (MpFlow.IsHosting)
            return "● Hosting · port " + MpFlow.Port + "   " +
                   "waiting for players, then Start";
        if (MpFlow.IsClient)
            return "● Connected " + MpFlow.Address + ":" + MpFlow.Port;
        if (!string.IsNullOrEmpty(MpFlow.Status)) return MpFlow.Status;
        return "Pick a name/map, then Host or Join";
    }

    internal static void Refresh()
    {
        try
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextRoster) return;
            _nextRoster = now + 0.4f;

            int count = 0;
            string roster = null;
            try { roster = MpDiag.PlayerRosterRich(out count); }
            catch (Exception e) { Plugin.Log.LogWarning("roster: " + e.GetType().Name); }

            if (_rosterText != null)
                _rosterText.text = string.IsNullOrEmpty(roster)
                    ? "no players yet"
                    : roster;
            if (_rosterTitleRef != null)
                _rosterTitleRef.text = "PLAYERS" + "  " + count + "/8";
            if (_rosterTitle != null)
                _rosterTitle.text = MpFlow.State == Session.Hosting ? "you are the host"
                                  : MpFlow.State == Session.Connected ? "connected" : "";
        }
        catch (Exception e) { Plugin.Log.LogWarning("Refresh: " + e.Message); }
    }

    internal static Color PingColor(double rtt)
    {
        if (rtt < 60) return PingColors[0];
        if (rtt < 150) return PingColors[1];
        return PingColors[2];
    }
}
