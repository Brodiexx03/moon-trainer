using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MoonTrainer
{
    /// <summary>The in-game menu (Insert by default), the active-cheats HUD, hotkeys, and the per-frame driver.</summary>
    public class Trainer : MonoBehaviour
    {
        public Trainer(IntPtr ptr) : base(ptr) { }

        // ---- layout ----
        const float PW = 820, PH = 620, Side = 196, Head = 60, CP = 22;
        const float CW = PW - Side - CP * 2;           // content width
        const float RowH = 42, BtnH = 36, Gap = 8;

        // ---- palette ----
        static readonly Color Panel   = Hex("0D0F14", 0.97f);
        static readonly Color Sidebar = Hex("12151C");
        static readonly Color CardBg  = Hex("181C25");
        static readonly Color Btn     = Hex("252A36");
        static readonly Color Line    = new Color(1, 1, 1, 0.06f);
        static readonly Color Accent  = Hex("5700FF");     // buttons, switches, the logo
        // 5700FF is too dark to read as small text or a thin line on this background, so those use a lighter shade
        // of the same purple. Warnings keep their amber so they don't look like ordinary highlights.
        static readonly Color AccentSoft = Hex("9D7BFF");
        const string AccentSoftHex = "#9D7BFF";
        static readonly Color Cream   = Hex("F2EEE6");
        static readonly Color Dim     = Hex("8B91A0");
        static readonly Color Faint   = Hex("5B6170");
        static readonly Color Danger  = Hex("B8392B");
        static readonly Color Good    = Hex("5BC985");
        static readonly Color Info    = Hex("5AB4E6");
        static readonly Color Off     = Hex("3A4050");
        static readonly Color KnobOff = Hex("C9CDD6");
        static readonly Color KnobOn  = Hex("FFFFFF");

        /// <summary>"FF8C33" -> colour. Worked out by hand: this runs while the class is being set up, before anything
        /// could catch a failed engine call, so it must not make one.</summary>
        static Color Hex(string h, float a = 1f)
        {
            int v = Convert.ToInt32(h, 16);
            var c = default(Color);
            c.r = ((v >> 16) & 255) / 255f; c.g = ((v >> 8) & 255) / 255f; c.b = (v & 255) / 255f; c.a = a;
            return c;
        }

        static bool Alive(UnityEngine.Object o) { return o != null && !o.WasCollected && o; }

        static readonly string[] Tabs =
        {
            "Home", "Player", "Money", "Dust & Vehicle", "World", "Flags & Sites", "Teleport",
            "Locations", "Build", "Crew", "Chat", "Backups", "Settings"
        };

        static Trainer _instance;

        bool _open, _uiBlocked;
        // what the cursor and the player's controls were doing before the menu opened (put back on close)
        CursorLockMode _prevLock = CursorLockMode.Locked; bool _prevVisible, _prevBlocked;
        int _buildFailures; bool _hudBroken, _hooksAnnounced;
        const string MenuBrokenText = "The trainer menu couldn't be built on this game version. Hotkeys and chat commands still work - details are in the console.";
        GameObject _canvas, _hudCanvas;
        Canvas _canvasComp;
        // _frame is what gets positioned and dragged; _panel sits inside it and is what fades and grows when opening
        RectTransform _frame, _panel, _header, _page, _toastRt, _hudPill;
        ScrollRect _scroll;
        CanvasGroup _toastGroup;
        TMP_FontAsset _fontBold, _fontRegular;
        TMP_Text _toastText, _hudLine, _hudNotice;
        readonly List<Action> _refreshers = new List<Action>();       // current page
        readonly List<Action> _chrome = new List<Action>();           // sidebar, header (built once)
        float _nextRefresh, _toastUntil, _nextPersist, _nextAnnounce;
        bool _rebuildPending; float _rebuildPendingAt;
        string _tab = "Home";

        // ---- effects (all optional: Settings > Menu animations switches them off, and so does any of them failing) ----
        sealed class Hot { public Button Button; public RectTransform Rt; public Image Glow; public Color GlowColor; public float T, Press; }
        sealed class Knob { public Image Track, Dot; public bool On, Init; public float T; }
        sealed class Counter { public TMP_Text Text; public Func<double, string> Fmt; public double From, To, Shown; public float P = 1f; public bool Init; }
        readonly List<Hot> _hotPage = new List<Hot>(), _hotChrome = new List<Hot>();
        readonly List<Knob> _knobs = new List<Knob>();
        readonly List<Counter> _counters = new List<Counter>();
        readonly List<TMP_Text> _tabLabels = new List<TMP_Text>();
        float[] _tabT = new float[0];
        CanvasGroup _panelGroup, _pageGroup;
        Image _dim, _logoGlow, _homeDot, _homeDotGlow;
        RectTransform _tabBar, _tabHi;
        float _openT, _openShown = -1f, _pageT = 1f, _pageShown = -1f, _tabY = -1f, _tabYShown = -1f, _toastShown = -1f;
        bool _fxShown = true, _fxBroken, _hoverBroken, _soundBroken, _anyActive;
        AudioSource _audio;
        static AudioClip _click;

        bool Fx { [HideFromIl2Cpp] get { return !_fxBroken && Settings.MenuEffects.Value; } }
        static float EaseOut(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // content cursor: widgets are placed at (_x, y) with width _w inside _page
        float _x, _w;

        // dragging
        bool _dragging; Vector2 _dragStart, _panelStart;

        // things that force a rebuild when they change
        int _waypointRevision = -1, _landmarkRevision = -1, _locationCount = -1;
        bool _updateShown;

        // data cached per rebuild
        int _memberIndex;
        List<Game.Member> _members = new List<Game.Member>();
        List<int> _flags = new List<int>(), _sites = new List<int>();
        int _flagPos, _sitePos;
        List<string> _backups = new List<string>();

        // two-click confirmations
        string _confirmKey; float _confirmUntil;

        static string _notice; static float _noticeUntil;
        public static void Notice(string text) { _notice = text; _noticeUntil = Time.unscaledTime + 5f; Plugin.Logger.LogInfo(text); }
        public static void MembersChanged() { if (_instance != null) _instance._rebuildPending = true; }

        bool _chimed; float _chimeAt;

        void Start()
        {
            _instance = this;
            _tab = Array.IndexOf(Tabs, Settings.LastTab.Value) >= 0 ? Settings.LastTab.Value : "Home";
            _chimeAt = Time.unscaledTime + 2f;   // chime once the first scene is up so the player hears the mod attached
            Notice("Moon Trainer " + Plugin.Version + " loaded - " + Settings.MenuKey.Value + " opens the menu");
        }

        /// <summary>Two rising tones generated on the fly (no audio files needed).</summary>
        [HideFromIl2Cpp]
        void Chime(float lowHz, float highHz)
        {
            try
            {
                int rate = 44100; float dur = 0.9f; int n = (int)(rate * dur);
                var data = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float>(n);
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / rate;
                    float freq = t < 0.4f ? lowHz : highHz;
                    float env = t < 0.4f ? Mathf.Min(1f, t * 40f) * (1f - t / 0.4f) : Mathf.Min(1f, (t - 0.4f) * 40f) * (1f - (t - 0.4f) / 0.5f);
                    data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.35f * Mathf.Max(0f, env);
                }
                var clip = AudioClip.Create("MoonTrainerChime", n, 1, rate, false);
                clip.SetData(data, 0);
                Speaker().PlayOneShot(clip);
            }
            catch (Exception e) { Plugin.Logger.LogWarning("Chime failed: " + e.Message); }
        }

        [HideFromIl2Cpp]
        AudioSource Speaker()
        {
            if (!Alive(_audio))
            {
                _audio = gameObject.GetComponent<AudioSource>();
                if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
                _audio.spatialBlend = 0f; _audio.volume = 0.8f; _audio.ignoreListenerPause = true;
            }
            return _audio;
        }

        /// <summary>A very short, quiet tick for menu presses.</summary>
        [HideFromIl2Cpp]
        void Click()
        {
            if (_soundBroken || !Settings.MenuSounds.Value) return;
            try
            {
                if (!Alive(_click))
                {
                    int rate = 44100; int n = (int)(rate * 0.05f);
                    var data = new Il2CppStructArray<float>(n);
                    for (int i = 0; i < n; i++)
                    {
                        float t = (float)i / rate;
                        float freq = Mathf.Lerp(1500f, 900f, t / 0.05f);
                        data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 90f) * Mathf.Min(1f, t * 800f);
                    }
                    _click = AudioClip.Create("MoonTrainerClick", n, 1, rate, false);
                    _click.SetData(data, 0);
                    _click.hideFlags = HideFlags.HideAndDontSave;
                }
                Speaker().PlayOneShot(_click, 0.22f);
            }
            catch (Exception e) { _soundBroken = true; Plugin.Logger.LogWarning("Menu click sound unavailable: " + e.Message); }
        }

        void Update()
        {
            // Each part is guarded on its own, so one failing (say, after a game update) can't stop the others.
            try
            {
                if (!_chimed && Time.unscaledTime >= _chimeAt) { _chimed = true; Chime(660f, 990f); }

                if (Updates.Available != null && !Updates.Announced && Game.InGame)
                {
                    Updates.Announced = true;
                    if (Updates.State == Updates.Idle)
                        Notice("Moon Trainer " + Updates.Available + " is available - open the menu (" + Settings.MenuKey.Value + ") and click Update now.");
                    Chime(990f, 1320f);
                }
                if (!_hooksAnnounced && Plugin.FailedHooks.Count > 0 && Game.InGame)
                {
                    _hooksAnnounced = true;
                    Notice("Some trainer features are off on this game version: " + Plugin.FailedFeatures() + ".");
                }
            }
            catch (Exception e) { LogOnce("startup", e); }

            try { UpdatePrompt.Tick(); } catch (Exception e) { LogOnce("update message", e); }
            try { HandleKeys(); } catch (Exception e) { LogOnce("hotkeys", e); }
            try { Cheats.Tick(); } catch (Exception e) { LogOnce("cheats", e); }
            try { Chat.Tick(); } catch (Exception e) { LogOnce("chat", e); }
            try
            {
                Game.TickBackups();
                if (Time.unscaledTime >= _nextAnnounce) { _nextAnnounce = Time.unscaledTime + 5f; Game.AnnounceVersion(); }
                if (Time.unscaledTime >= _nextPersist) { _nextPersist = Time.unscaledTime + 2f; Settings.SaveCheatsIfChanged(); }
            }
            catch (Exception e) { LogOnce("housekeeping", e); }
            try { UpdateHud(); } catch (Exception e) { LogOnce("hud", e); }

            try
            {
                if (_open && !Game.InGame) Close();
                if (_open)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    var walker = Game.Walker;
                    if (walker != null && !_uiBlocked) { walker.UIBlocked = true; _uiBlocked = true; }
                    Drag();
                    if (_rebuildPendingAt > 0 && Time.unscaledTime >= _rebuildPendingAt) { _rebuildPendingAt = 0; _rebuildPending = true; }
                    if (_rebuildPending) { _rebuildPending = false; Rebuild(); }
                    if (Time.unscaledTime >= _nextRefresh) Refresh();
                    UpdateToast();
                }
            }
            catch (Exception e) { LogOnce("menu", e); }

            try { Animate(); }
            catch (Exception e)
            {
                // Effects are decoration. If one can't run on this game build, drop them all and keep the menu.
                if (!_fxBroken) Plugin.Logger.LogWarning("Menu effects switched off (" + e.GetType().Name + ": " + e.Message + "). The menu itself is unaffected.");
                _fxBroken = true;
                LogOnce("effects", e);
            }
        }

        static readonly Dictionary<string, float> _logged = new Dictionary<string, float>();

        /// <summary>Logs an error, but the same one at most every 10 seconds (these run every frame).</summary>
        static void LogOnce(string stage, Exception e)
        {
            string key = stage + ":" + e.GetType().Name + ":" + e.Message;
            float now = Time.unscaledTime, last;
            if (_logged.TryGetValue(key, out last) && now - last < 10f) return;
            if (_logged.Count > 100) _logged.Clear();
            _logged[key] = now;
            Plugin.Logger.LogError("[" + stage + "] " + e);
        }

        [HideFromIl2Cpp]
        void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            // While the player is typing in chat, keys are text, not hotkeys. (The menu key still closes an open menu.)
            bool typing = Game.Typing;
            if (Pressed(kb, Settings.MenuKey.Value) && (_open || !typing)) Toggle();
            if (typing) return;
            if (Pressed(kb, Settings.HudKey.Value)) { Settings.ShowHud.Value = !Settings.ShowHud.Value; Notice("Active-cheats line " + (Settings.ShowHud.Value ? "on" : "off")); }
            if (_open || !Game.InGame) return;
            if (Pressed(kb, Settings.FlyKey.Value)) { Cheats.SetFly(!Cheats.Fly); Notice("Fly " + OnOff(Cheats.Fly)); }
            if (Pressed(kb, Settings.NoclipKey.Value)) { Cheats.Noclip = !Cheats.Noclip; if (Cheats.Noclip) Cheats.Fly = true; Notice("No clip " + OnOff(Cheats.Noclip)); }
            if (Pressed(kb, Settings.SpeedKey.Value)) { Cheats.ToggleSpeedBoost(); Notice("Move speed x" + Cheats.SpeedMult.ToString("0.#")); }
            if (Pressed(kb, Settings.JetpackKey.Value)) { Cheats.InfiniteJetpack = !Cheats.InfiniteJetpack; Notice("Unlimited jetpack " + OnOff(Cheats.InfiniteJetpack)); }
            if (Pressed(kb, Settings.SaveKey.Value)) Notice(Game.SaveNow());
        }

        static string OnOff(bool on) { return on ? "on" : "off"; }

        static bool Pressed(Keyboard kb, string keyName)
        {
            if (string.IsNullOrEmpty(keyName) || keyName == "None") return false;
            Key key;
            if (!Enum.TryParse<Key>(keyName, true, out key)) return false;
            var control = kb[key];
            return control != null && control.wasPressedThisFrame;
        }

        [HideFromIl2Cpp]
        void Toggle() { if (_open) Close(); else Open(); }

        [HideFromIl2Cpp]
        void Open()
        {
            if (!Game.InGame) { Notice("Load into a game first."); return; }
            if (!Alive(_canvas))
            {
                if (_buildFailures >= 3) { Notice(MenuBrokenText); return; }
                try { Build(); }
                catch (Exception e)
                {
                    // The game ships without the Unity features it doesn't use itself; if the menu needs one of them,
                    // give up on the window cleanly rather than leaving half of it on screen.
                    _buildFailures++;
                    Plugin.Logger.LogError("The trainer menu could not be built: " + e);
                    try { if (_canvas != null) Destroy(_canvas); } catch (Exception) { }
                    _canvas = null; _frame = null; _panel = null; _header = null; _page = null; _toastGroup = null; _toastText = null;
                    _panelGroup = null; _pageGroup = null; _dim = null; _logoGlow = null; _homeDot = null; _homeDotGlow = null; _tabBar = null; _tabHi = null;
                    Notice(MenuBrokenText);
                    return;
                }
            }
            var walker = Game.Walker;
            _prevLock = Cursor.lockState; _prevVisible = Cursor.visible;
            _prevBlocked = walker != null && walker.UIBlocked;
            _canvas.SetActive(true);
            ClampPanel();
            _open = true;
            _rebuildPending = true;
            // Fade and grow in from wherever the window is (0 after a full close; part-way if it was still closing).
            if (!Fx) _openT = 1f;
            _openShown = -1f; _pageT = 0f; _tabY = -1f;
            if (_panelGroup != null) _panelGroup.blocksRaycasts = true;
        }

        [HideFromIl2Cpp]
        void Close()
        {
            _open = false;
            _dragging = false;
            var walker = Game.Walker;
            if (Alive(_canvas))
            {
                // With effects on, the window fades out over a moment (Animate hides it when done); it stops taking
                // clicks straight away. Leaving the game hides it at once.
                if (Fx && walker != null && _panelGroup != null) _panelGroup.blocksRaycasts = false;
                else { _canvas.SetActive(false); _openT = 0f; }
            }
            // If one of the game's own screens (map, shop, pause) was open underneath and still is, leave the cursor
            // and controls the way that screen had them. Otherwise hand control back to the player.
            bool gameScreenOpen = _prevBlocked && walker != null && walker.UIBlocked;
            if (walker != null && _uiBlocked && !gameScreenOpen) walker.UIBlocked = false;
            _uiBlocked = false;
            if (walker == null) return;                 // back at the main menu: the cursor stays free
            if (gameScreenOpen) { Cursor.lockState = _prevLock; Cursor.visible = _prevVisible; }
            else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        /// <summary>Keeps enough of the window on screen to grab it (a saved position can be off-screen after a resolution change).</summary>
        [HideFromIl2Cpp]
        void ClampPanel()
        {
            if (_frame == null) return;
            // The canvas is 1080 units tall and as wide as the screen's shape makes it (see the CanvasScaler in Build).
            float ch = 1080f, cw = 1080f * Screen.width / Mathf.Max(1, Screen.height);
            var p = _frame.anchoredPosition;
            float x = Mathf.Clamp(p.x, -(PW - 200), Mathf.Max(0, cw - 200));
            float y = Mathf.Clamp(p.y, -(ch - Head), 0);
            if (x == p.x && y == p.y) return;
            _frame.anchoredPosition = new Vector2(x, y);
            Settings.MenuX.Value = x; Settings.MenuY.Value = y;
        }

        // ---- dragging the header ----------------------------------------------------------------

        [HideFromIl2Cpp]
        void Drag()
        {
            var mouse = Mouse.current;
            if (mouse == null || _header == null) return;
            Vector2 pos = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame && RectTransformUtility.RectangleContainsScreenPoint(_header, pos, null))
            {
                _dragging = true; _dragStart = pos; _panelStart = _frame.anchoredPosition;
            }
            if (!_dragging) return;
            if (mouse.leftButton.isPressed)
            {
                float scale = _canvasComp != null && _canvasComp.scaleFactor > 0 ? _canvasComp.scaleFactor : 1f;
                _frame.anchoredPosition = _panelStart + (pos - _dragStart) / scale;
            }
            else
            {
                _dragging = false;
                ClampPanel();
                Settings.MenuX.Value = _frame.anchoredPosition.x;
                Settings.MenuY.Value = _frame.anchoredPosition.y;
            }
        }

        // ---- sprites (generated once: rounded corners, pills, soft shadow) -------------------------

        static Sprite _round, _pill, _soft;
        const float RoundR = 16, PillR = 32, SoftR = 40;

        static bool _spritesFailed;

        static Sprite MakeSprite(int size, float radius, float feather)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;
            float half = size / 2f;
            var alpha = new byte[size * size];
            for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                {
                    float qx = Mathf.Abs(px + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(py + 0.5f - half) - (half - radius);
                    float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
                    float dist = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;
                    float a = feather > 0 ? Mathf.Clamp01(-dist / feather) : Mathf.Clamp01(0.5f - dist);
                    if (feather > 0) a = a * a * (3 - 2 * a);
                    alpha[py * size + px] = (byte)Mathf.RoundToInt(a * 255f);
                }
            bool written = false;
            try
            {
                var pixels = new Il2CppStructArray<Color32>(size * size);
                var c = default(Color32);
                c.r = c.g = c.b = 255;
                for (int i = 0; i < alpha.Length; i++) { c.a = alpha[i]; pixels[i] = c; }
                tex.SetPixels32(pixels);
                written = true;
            }
            catch (Exception e) { Plugin.Logger.LogInfo("Writing a texture in one go isn't available here (" + e.Message + "); doing it pixel by pixel."); }
            if (!written)
                for (int i = 0; i < alpha.Length; i++) tex.SetPixel(i % size, i / size, new Color(1, 1, 1, alpha[i] / 255f));
            tex.Apply();
            float b = Mathf.Min(half - 1, radius + feather);
            var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        /// <summary>Makes the rounded shapes once. If this game build can't, the menu falls back to square corners.</summary>
        static void EnsureSprites()
        {
            if (_spritesFailed) return;
            try
            {
                if (!Alive(_round)) _round = MakeSprite(64, RoundR, 0);
                if (!Alive(_pill)) _pill = MakeSprite(64, PillR, 0);
                if (!Alive(_soft)) _soft = MakeSprite(128, 24, SoftR);
            }
            catch (Exception e) { NoSprites(e); }
        }

        static void NoSprites(Exception e)
        {
            _spritesFailed = true;
            _round = _pill = _soft = null;
            Plugin.Logger.LogWarning("Rounded corners aren't available on this game build (" + e.GetType().Name + ": " + e.Message + "). The menu will use square corners.");
        }

        /// <summary>A rounded-rectangle image. radius is in canvas pixels; 0 = square.</summary>
        static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color, float radius = 8, bool pill = false)
        {
            var rt = Rect(parent, name, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (pill) radius = h / 2f;
            var sprite = pill ? _pill : _round;
            if (radius > 0 && !_spritesFailed && sprite != null)
            {
                try
                {
                    img.sprite = sprite;
                    img.type = Image.Type.Sliced;
                    img.pixelsPerUnitMultiplier = (pill ? PillR : RoundR) / radius;
                }
                catch (Exception e)
                {
                    NoSprites(e);
                    try { img.sprite = null; img.type = Image.Type.Simple; } catch (Exception) { }
                }
            }
            return img;
        }

        // ---- HUD (active cheats + notices) --------------------------------------------------------

        [HideFromIl2Cpp]
        void UpdateHud()
        {
            if (_hudBroken) return;
            if (!Alive(_hudCanvas))
            {
                if (!Game.InGame) return;
                try { BuildHud(); }
                catch (Exception e)
                {
                    _hudBroken = true;
                    Plugin.Logger.LogError("The on-screen notice line could not be built (notices still appear in this console): " + e);
                    try { if (_hudCanvas != null) Destroy(_hudCanvas); } catch (Exception) { }
                    _hudCanvas = null;
                    return;
                }
            }
            string line = "";
            if (Game.InGame && !_open && Settings.ShowHud.Value)
            {
                line = Cheats.ActiveSummary();
                if (Updates.Available != null)
                    line = (line.Length > 0 ? line + "     " : "") + "<color=#5AB4E6>"
                        + (Updates.State == Updates.Done ? "RESTART TO FINISH UPDATE" : "UPDATE " + Updates.Available) + "</color>";
            }
            if (_hudLine.text != line)
            {
                _hudLine.text = line;
                float w = 0;
                if (line.Length > 0)
                {
                    try { w = _hudLine.GetPreferredValues(line).x + 36; }
                    catch (Exception) { w = Mathf.Min(1700, line.Length * 9f + 36); }
                }
                _hudPill.sizeDelta = new Vector2(w, 30);
                _hudPill.gameObject.SetActive(line.Length > 0);
            }
            _hudNotice.text = (!_open && _notice != null && Time.unscaledTime < _noticeUntil) ? _notice : "";
        }

        [HideFromIl2Cpp]
        void BuildHud()
        {
            FindFonts();
            EnsureSprites();
            _hudCanvas = new GameObject("MoonTrainer HUD");
            DontDestroyOnLoad(_hudCanvas);
            var canvas = _hudCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30999;
            var scaler = _hudCanvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;

            var pill = Box(_hudCanvas.transform, "Active", 0, 0, 10, 30, new Color(0.05f, 0.06f, 0.08f, 0.82f), pill: true);
            _hudPill = pill.rectTransform;
            _hudPill.anchorMin = _hudPill.anchorMax = new Vector2(0.5f, 1); _hudPill.pivot = new Vector2(0.5f, 1);
            _hudPill.anchoredPosition = new Vector2(0, -12);
            var lineRt = Rect(_hudPill, "Text", 0, 0, 10, 30);
            lineRt.anchorMin = Vector2.zero; lineRt.anchorMax = Vector2.one; lineRt.sizeDelta = Vector2.zero; lineRt.anchoredPosition = Vector2.zero;
            _hudLine = AddText(lineRt.gameObject, 13, true, AccentSoft, TextAlignmentOptions.Center);
            _hudLine.characterSpacing = 2;
            _hudPill.gameObject.SetActive(false);

            var bottom = Rect(_hudCanvas.transform, "Notice", 0, 0, 1200, 30);
            bottom.anchorMin = bottom.anchorMax = new Vector2(0, 0); bottom.pivot = new Vector2(0, 0);
            bottom.anchoredPosition = new Vector2(24, 20);
            _hudNotice = AddText(bottom.gameObject, 17, true, Cream, TextAlignmentOptions.BottomLeft);
        }

        // ---- building the window -----------------------------------------------------------------------

        [HideFromIl2Cpp]
        void Build()
        {
            FindFonts();
            EnsureSprites();
            _chrome.Clear();
            if (EventSystem.current == null)
            {
                var es = new GameObject("MoonTrainer EventSystem");
                DontDestroyOnLoad(es);
                es.AddComponent<EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            _canvas = new GameObject("MoonTrainer canvas");
            DontDestroyOnLoad(_canvas);
            _canvasComp = _canvas.AddComponent<Canvas>();
            _canvasComp.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvasComp.sortingOrder = 31000;
            var scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;
            _canvas.AddComponent<GraphicRaycaster>();

            _hotChrome.Clear(); _tabLabels.Clear();

            // a faint dim over the game behind the window (fades with it; clicks pass through)
            try
            {
                var dimRt = Rect(_canvas.transform, "Dim", 0, 0, 10, 10);
                dimRt.anchorMin = Vector2.zero; dimRt.anchorMax = Vector2.one; dimRt.pivot = new Vector2(0.5f, 0.5f);
                dimRt.sizeDelta = Vector2.zero; dimRt.anchoredPosition = Vector2.zero;
                _dim = dimRt.gameObject.AddComponent<Image>();
                _dim.color = Color.clear; _dim.raycastTarget = false;
            }
            catch (Exception) { _dim = null; }

            _frame = Rect(_canvas.transform, "Window", 0, 0, PW, PH);
            _frame.anchoredPosition = new Vector2(Settings.MenuX.Value, Settings.MenuY.Value);
            _panel = Rect(_frame, "Panel", 0, 0, PW, PH);
            _panel.pivot = new Vector2(0.5f, 0.5f);                 // so it grows from its middle
            _panel.anchoredPosition = new Vector2(PW / 2f, -PH / 2f);
            try { _panelGroup = _panel.gameObject.AddComponent<CanvasGroup>(); } catch (Exception) { _panelGroup = null; }

            // soft shadow, border, body
            var shadow = Box(_panel, "Shadow", -30, -26, PW + 60, PH + 64, new Color(0, 0, 0, 0.55f), 0);
            if (_soft != null && !_spritesFailed)
            {
                try { shadow.sprite = _soft; shadow.type = Image.Type.Sliced; shadow.pixelsPerUnitMultiplier = 1; }
                catch (Exception) { shadow.enabled = false; }
            }
            else shadow.enabled = false;
            Box(_panel, "Border", -1, -1, PW + 2, PH + 2, new Color(1, 1, 1, 0.09f), 15);
            var body = Box(_panel, "Body", 0, 0, PW, PH, Panel, 14);
            body.raycastTarget = true;   // swallow clicks so they don't reach the game

            // sidebar
            Box(_panel, "Sidebar", 0, 0, Side, PH, Sidebar, 14);
            Box(_panel, "SidebarEdge", Side - 14, 0, 14, PH, Sidebar, 0);
            Box(_panel, "Divider", Side, 0, 1, PH, Line, 0);

            // header (drag handle) over the content side
            _header = Rect(_panel, "Header", 0, 0, PW - 56, Head);
            Box(_panel, "HeaderLine", Side, Head, PW - Side, 1, Line, 0);

            // brand in the sidebar
            _logoGlow = Glow(_panel, 1, 1, 60, 60, new Color(AccentSoft.r, AccentSoft.g, AccentSoft.b, 0.2f), 1f);
            Box(_panel, "Logo", 18, 18, 26, 26, Accent, 7);
            Text(_panel, "M", 18, 18, 26, 26, 16, true, Color.white, TextAlignmentOptions.Center);
            Text(_panel, "MOON TRAINER", 52, 14, Side - 60, 20, 15, true, Cream, TextAlignmentOptions.MidlineLeft).characterSpacing = 3;
            Text(_panel, "v" + Plugin.Version, 52, 33, Side - 60, 16, 11, false, Dim, TextAlignmentOptions.MidlineLeft);

            // tabs: one highlight and one orange bar that glide to the active tab (see Animate)
            float ty = Head + 12;
            _tabHi = Box(_panel, "TabHighlight", 10, ty, Side - 20, 34, new Color(1, 1, 1, 0.06f), 8).rectTransform;
            foreach (var tab in Tabs)
            {
                string t = tab;
                var bg = Box(_panel, "Tab " + t, 10, ty, Side - 20, 34, Color.clear, 8);
                var label = Text(_panel, t, 24, ty, Side - 44, 34, 14, true, Dim, TextAlignmentOptions.MidlineLeft);
                MakeClickable(bg, () => { SwitchTab(t); return null; }, Color.clear, new Color(1, 1, 1, 0.04f));
                _tabLabels.Add(label);
                ty += 36;
            }
            _tabT = new float[_tabLabels.Count];
            for (int i = 0; i < _tabT.Length; i++) _tabT[i] = -1f;
            _tabBar = Box(_panel, "TabBar", 10, Head + 20, 3, 18, AccentSoft, 1.5f).rectTransform;
            _tabY = -1f; _tabYShown = -1f;

            // a small dot on the Home tab that pulses while any cheat is on
            _homeDotGlow = Glow(_panel, Side - 46, Head + 16, 26, 26, new Color(AccentSoft.r, AccentSoft.g, AccentSoft.b, 0.3f), 1f);
            _homeDot = Box(_panel, "ActiveDot", Side - 36, Head + 26, 6, 6, AccentSoft, pill: true);
            _homeDot.gameObject.SetActive(false);
            if (_homeDotGlow != null) _homeDotGlow.gameObject.SetActive(false);

            // footer of the sidebar: quick "all off"
            var off = Box(_panel, "AllOff", 14, PH - 50, Side - 28, 34, Btn, 8);
            Text(off.transform, "Turn everything off", 0, 0, Side - 28, 34, 13, true, Dim, TextAlignmentOptions.Center);
            MakeClickable(off, () => Cheats.Off(), Btn, Danger);
            Lift(off, null, Color.clear, _hotChrome);

            // page title area
            var title = Text(_panel, "", Side + CP, 10, CW - 40, 26, 20, true, Cream, TextAlignmentOptions.MidlineLeft);
            var subtitle = Text(_panel, "", Side + CP, 34, CW - 40, 18, 12, false, Dim, TextAlignmentOptions.MidlineLeft);
            _chrome.Add(() => { title.text = _tab; subtitle.text = Subtitle(_tab); });

            // close button
            var close = Box(_panel, "Close", PW - 46, 14, 32, 32, Btn, 8);
            Text(close.transform, "X", 0, 0, 32, 32, 14, true, Dim, TextAlignmentOptions.Center);
            MakeClickable(close, () => { Close(); return null; }, Color.clear, Danger);
            Lift(close, null, Color.clear, _hotChrome);

            // scrolling page
            var view = Rect(_panel, "View", Side, Head + 1, PW - Side, PH - Head - 1);
            view.gameObject.AddComponent<RectMask2D>();
            _page = Rect(view, "Page", 0, 0, PW - Side, 10);
            try { _pageGroup = _page.gameObject.AddComponent<CanvasGroup>(); } catch (Exception) { _pageGroup = null; }
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _page; _scroll.viewport = view; _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 45; _scroll.inertia = false;

            // scrollbar (optional: the mouse wheel scrolls the page either way)
            Image track = null;
            try
            {
                track = Box(view, "ScrollTrack", PW - Side - 10, 8, 4, PH - Head - 17, new Color(1, 1, 1, 0.03f), pill: true);
                var handle = Box(track.transform, "Handle", 0, 0, 4, 40, new Color(1, 1, 1, 0.18f), pill: true);
                var hr = handle.rectTransform; hr.anchorMin = Vector2.zero; hr.anchorMax = Vector2.one; hr.sizeDelta = Vector2.zero; hr.anchoredPosition = Vector2.zero;
                handle.raycastTarget = true;
                var sb = track.gameObject.AddComponent<Scrollbar>();
                sb.handleRect = hr; sb.targetGraphic = handle; sb.direction = Scrollbar.Direction.BottomToTop;
                _scroll.verticalScrollbar = sb;
                _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("No scrollbar on this game build (the mouse wheel still scrolls): " + e.Message);
                try { if (track != null) Destroy(track.gameObject); } catch (Exception) { }
            }

            // toast
            var toast = Box(_panel, "Toast", Side + CP, PH - 56, CW, 38, Hex("232836", 0.98f), 10);
            _toastRt = toast.rectTransform;
            Box(toast.transform, "ToastAccent", 0, 0, 4, 38, AccentSoft, 2);
            _toastText = Text(toast.transform, "", 16, 0, CW - 28, 38, 13, true, Cream, TextAlignmentOptions.MidlineLeft);
            try
            {
                _toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
                _toastGroup.alpha = 0; _toastGroup.blocksRaycasts = false;
            }
            catch (Exception) { _toastGroup = null; toast.gameObject.SetActive(false); }   // no fading: the toast just pops in and out

            Plugin.Logger.LogInfo("Trainer menu built.");
        }

        static string Subtitle(string tab)
        {
            switch (tab)
            {
                case "Home": return "Presets and the things you use most.";
                case "Player": return "Movement, suit and gear.";
                case "Money": return "The team wallet and what things cost.";
                case "Dust & Vehicle": return "Your bag, selling, and the vehicle hopper.";
                case "World": return "Time of day, machines and cleaning.";
                case "Flags & Sites": return "Find, visit and raise landmarks.";
                case "Teleport": return "Jump to landmarks, markers or crewmates.";
                case "Locations": return "Spots you've saved on this save.";
                case "Build": return "Place buildings where you're looking.";
                case "Crew": return "Help out the people in your lobby.";
                case "Chat": return "What crewmates may do with chat commands.";
                case "Backups": return "Snapshots of this save you can roll back to.";
                case "Settings": return "How the trainer behaves.";
            }
            return "";
        }

        [HideFromIl2Cpp]
        void SwitchTab(string tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            Settings.LastTab.Value = tab;
            _rebuildPending = true;
            _pageT = 0f;                                   // the new page slides and fades in
        }

        // ---- pages ---------------------------------------------------------------------------------------

        /// <summary>Clears the page and lays out the current tab.</summary>
        [HideFromIl2Cpp]
        void Rebuild()
        {
            for (int i = _page.childCount - 1; i >= 0; i--) Destroy(_page.GetChild(i).gameObject);
            _refreshers.Clear();
            _hotPage.Clear(); _knobs.Clear(); _counters.Clear();
            _pageShown = -1f;

            var world = Game.World;
            _waypointRevision = world == null ? -1 : world.WaypointRevision;
            _landmarkRevision = world == null ? -1 : world.LandmarkRevision;
            _locationCount = Locations.All().Count;
            _updateShown = Updates.Available != null;
            _members = Game.Members();
            if (_memberIndex >= _members.Count) _memberIndex = 0;

            float y = 18;
            _x = CP; _w = CW;
            try
            {
                switch (_tab)
                {
                    case "Home": HomePage(ref y); break;
                    case "Player": PlayerPage(ref y); break;
                    case "Money": HostOnly(ref y, "the wallet"); MoneyPage(ref y); break;
                    case "Dust & Vehicle": DustPage(ref y); break;
                    case "World": HostOnly(ref y, "cleaning, machines and the save"); WorldPage(ref y); break;
                    case "Flags & Sites": HostOnly(ref y, "flags and sites"); LandmarkPage(ref y); break;
                    case "Teleport": TeleportPage(ref y); break;
                    case "Locations": LocationsPage(ref y); break;
                    case "Build": HostOnly(ref y, "buildings"); BuildPage(ref y); break;
                    case "Crew": HostOnly(ref y, "everyone's wallet and upgrades"); CrewPage(ref y); break;
                    case "Chat": ChatPage(ref y); break;
                    case "Backups": HostOnly(ref y, "the save and its backups"); BackupsPage(ref y); break;
                    case "Settings": SettingsPage(ref y); break;
                }
            }
            catch (Exception e)
            {
                // One page failing (say, the game changed something it reads) shouldn't take the whole menu with it.
                Plugin.Logger.LogError("The " + _tab + " page could not be shown: " + e);
                _x = CP; _w = CW;
                y += 12;
                try
                {
                    var t = Text(_page, "This page hit an error and can't be shown fully. The other tabs still work - details are in the console.",
                        CP, y, CW, 44, 14, true, Cream, TextAlignmentOptions.TopLeft);
                    t.textWrappingMode = TextWrappingModes.Normal;
                }
                catch (Exception) { }
                y += 52;
            }
            y += 70;   // room for the toast
            _page.sizeDelta = new Vector2(PW - Side, y);
            _page.anchoredPosition = Vector2.zero;
            Refresh();
        }

        [HideFromIl2Cpp]
        void HomePage(ref float y)
        {
            if (_updateShown)
            {
                var c = Card(ref y, null, Hex("10283A"));
                Label(ref y, "<color=#5AB4E6>UPDATE AVAILABLE</color>   v" + Updates.Available + "  <color=#8B91A0>(you have " + Plugin.Version + ")</color>", 15, true);
                if (!string.IsNullOrEmpty(Updates.Notes)) Hint(ref y, Updates.Notes);
                var status = Hint(ref y, "", 18);
                status.color = Cream;
                _refreshers.Add(() =>
                {
                    switch (Updates.State)
                    {
                        case Updates.Working: status.text = Updates.Status; break;
                        case Updates.Done: status.text = "<color=#5BC985>Installed.</color>  Restart the game to start using " + Updates.Installed + "."; break;
                        case Updates.Failed: status.text = "<color=#FF9A4D>Not installed:</color>  " + Updates.Status; break;
                        default: status.text = "Click Update now and it installs by itself. The game needs a restart afterwards."; break;
                    }
                });
                Buttons(ref y, new[] { "Update now", "Copy PowerShell command" }, new Func<string>[] { () =>
                {
                    if (Updates.State == Updates.Done) return "Already installed - restart the game to use it.";
                    if (Updates.State == Updates.Working) return "Still working on it...";
                    Updates.Reset();
                    Updates.InstallInBackground();
                    return "Updating - you can keep playing.";
                }, () =>
                {
                    GUIUtility.systemCopyBuffer = Updates.InstallCommand;
                    return "Copied. Close the game, open PowerShell, right-click to paste, press Enter.";
                } }, Info);
                EndCard(c, ref y);
            }

            if (Plugin.FailedHooks.Count > 0)
            {
                var fh = Card(ref y, null, Hex("3A2412"));
                Label(ref y, "<color=#FF8C33>SOME FEATURES ARE OFF</color>", 15, true);
                Hint(ref y, "The game has changed since this trainer was built, so these can't hook in: " + Plugin.FailedFeatures()
                    + ". Everything else works. Check for a trainer update.").color = Cream;
                EndCard(fh, ref y);
            }

            var p = Card(ref y, "Presets");
            Buttons(ref y, new[] { "Exploring", "Grinding", "Building" }, new Func<string>[] {
                () => Cheats.Preset("explore"), () => Cheats.Preset("grind"), () => Cheats.Preset("build") });
            Hint(ref y, "Exploring: fly + noclip, night vision, frozen time, flags on the map.   Grinding: 5x money, wide brush, big bag, fast machines.   Building: fly, free upgrades, fast machines.");
            EndCard(p, ref y);

            var q = Card(ref y, "Quick toggles");
            Switch(ref y, "Fly", "Space / Ctrl to go up and down", () => Cheats.Fly, v => Cheats.SetFly(v));
            Switch(ref y, "No clip", "Fly through terrain and buildings", () => Cheats.Noclip, v => { Cheats.Noclip = v; if (v) Cheats.SetFly(true); });
            Switch(ref y, "Unlimited jetpack", null, () => Cheats.InfiniteJetpack, v => Cheats.InfiniteJetpack = v);
            Switch(ref y, "Unlimited bag", null, () => Cheats.InfiniteBag, v => Cheats.InfiniteBag = v);
            Switch(ref y, "Freeze time of day", null, () => Cheats.FreezeTime, v => Cheats.FreezeTime = v);
            EndCard(q, ref y);

            var a = Card(ref y, "Active right now");
            var t = Label(ref y, "", 13, true);
            t.textWrappingMode = TextWrappingModes.Normal;
            ((RectTransform)t.transform).sizeDelta = new Vector2(_w, 40);
            y += 12;
            _refreshers.Add(() => { var s = Cheats.ActiveSummary(); t.text = s.Length == 0 ? "<color=#8B91A0>Nothing - the game is vanilla.</color>" : "<color=" + AccentSoftHex + ">" + s + "</color>"; });
            EndCard(a, ref y);
        }

        [HideFromIl2Cpp]
        void PlayerPage(ref float y)
        {
            var m = Card(ref y, "Movement");
            Switch(ref y, "Fly", "Space / Ctrl to go up and down", () => Cheats.Fly, v => Cheats.SetFly(v));
            Switch(ref y, "No clip", "Only while flying", () => Cheats.Noclip, v => { Cheats.Noclip = v; if (v) Cheats.SetFly(true); });
            Stepper(ref y, "Fly speed", () => Cheats.FlySpeed, v => Cheats.FlySpeed = Mathf.Clamp(v, 2, 500), 5, "0");
            Stepper(ref y, "Move speed", () => Cheats.SpeedMult, v => Cheats.SpeedMult = Mathf.Clamp(v, 0.25f, 20), 0.5f, "0.0#", "x");
            Stepper(ref y, "Jump height", () => Cheats.JumpMult, v => Cheats.JumpMult = Mathf.Clamp(v, 0.25f, 20), 0.5f, "0.0#", "x");
            EndCard(m, ref y);

            var s = Card(ref y, "Suit");
            Switch(ref y, "Unlimited jetpack", null, () => Cheats.InfiniteJetpack, v => Cheats.InfiniteJetpack = v);
            Switch(ref y, "Gravity boots", null, () => Cheats.GravityBoots, v => Cheats.GravityBoots = v);
            Switch(ref y, "Night vision", null, () => Cheats.NightVision, v => Cheats.NightVision = v);
            y += 4;
            Buttons(ref y, new[] { "Refill jetpack + energy", "Max all my upgrades" }, new Func<string>[] {
                () => Cheats.RefillAll(), () => Game.GiveAllUpgrades(Game.PlayerId) });
            EndCard(s, ref y);
        }

        [HideFromIl2Cpp]
        void MoneyPage(ref float y)
        {
            var w = Card(ref y, "Team wallet");
            var money = Text(_page, "", _x, y, _w, 44, 30, true, Cream, TextAlignmentOptions.MidlineLeft); y += 48;
            Count(money, () => Game.Money(Game.PlayerId), v => "$" + v.ToString("N0"));
            Buttons(ref y, new[] { "+1k", "+10k", "+100k", "+1M" }, new Func<string>[] {
                () => AddMoney(1000), () => AddMoney(10000), () => AddMoney(100000), () => AddMoney(1000000) }, Accent);
            Buttons(ref y, new[] { "Set 0", "Set 100k", "Set 10M" }, new Func<string>[] {
                () => SetMoney(0), () => SetMoney(100000), () => SetMoney(10000000) });
            EndCard(w, ref y);

            var e = Card(ref y, "Earning & spending");
            Stepper(ref y, "Earnings", () => Cheats.PayoutMult, v => Cheats.PayoutMult = Mathf.Clamp(v, 0.25f, 100), 1f, "0.0#", "x");
            Switch(ref y, "Free upgrades & tools", null, () => Cheats.FreeUpgrades, v => Cheats.FreeUpgrades = v);
            Switch(ref y, "Free station travel", null, () => Cheats.FreeTravel, v => Cheats.FreeTravel = v);
            EndCard(e, ref y);
        }

        [HideFromIl2Cpp]
        void DustPage(ref float y)
        {
            var b = Card(ref y, "Your bag");
            Switch(ref y, "Unlimited capacity", null, () => Cheats.InfiniteBag, v => Cheats.InfiniteBag = v);
            Switch(ref y, "Auto-sell what I collect", "Experimental", () => Cheats.AutoSell, v => Cheats.AutoSell = v);
            y += 4;
            Buttons(ref y, new[] { "Fill", "Empty", "Sell now" }, new Func<string>[] {
                () => Cheats.SetCarried(true), () => Cheats.SetCarried(false), () => Cheats.SellCarried(true) });
            EndCard(b, ref y);

            var v = Card(ref y, "Vehicle");
            Stepper(ref y, "Speed", () => Cheats.VehicleSpeedMult, x => Cheats.VehicleSpeedMult = Mathf.Clamp(x, 0.25f, 20), 0.5f, "0.0#", "x");
            Stepper(ref y, "Brush width", () => Cheats.BrushWidthMult, x => Cheats.BrushWidthMult = Mathf.Clamp(x, 0.25f, 20), 0.5f, "0.0#", "x");
            Switch(ref y, "Unlimited hopper", null, () => Cheats.InfiniteHopper, x => Cheats.InfiniteHopper = x);
            y += 4;
            Buttons(ref y, new[] { "Fill hopper", "Empty hopper" }, new Func<string>[] {
                () => Cheats.SetHopper(true), () => Cheats.SetHopper(false) });
            EndCard(v, ref y);
        }

        [HideFromIl2Cpp]
        void WorldPage(ref float y)
        {
            var t = Card(ref y, "Time");
            var clock = Text(_page, "", _x, y, _w, 40, 26, true, Cream, TextAlignmentOptions.MidlineLeft); y += 44;
            _refreshers.Add(() => clock.text = Cheats.TimeText() + "  <size=13><color=#8B91A0>lunar time</color></size>");
            Switch(ref y, "Freeze time of day", null, () => Cheats.FreezeTime, v => Cheats.FreezeTime = v);
            Stepper(ref y, "Time speed", () => Cheats.TimeSpeed, v => Cheats.TimeSpeed = Mathf.Clamp(Mathf.Round(v * 4) / 4, 0.25f, 50), 0.25f, "0.##", "x");
            y += 4;
            Buttons(ref y, new[] { "-1 h", "+1 h", "+6 h", "+12 h" }, new Func<string>[] {
                () => Cheats.ShiftTime(-1), () => Cheats.ShiftTime(1), () => Cheats.ShiftTime(6), () => Cheats.ShiftTime(12) });
            EndCard(t, ref y);

            var m = Card(ref y, "Machines");
            Stepper(ref y, "Machine speed", () => Cheats.MachineSpeed, v => Cheats.MachineSpeed = Mathf.Clamp(v, 0.25f, 50), 1f, "0.0#", "x");
            EndCard(m, ref y);

            var c = Card(ref y, "Cleaning");
            Stepper(ref y, "Clean radius", () => Cheats.CleanRings, v => Cheats.CleanRings = Mathf.Clamp(Mathf.RoundToInt(v), 1, 60), 1f, "0", " rings", 10f);
            y += 4;
            var go = Buttons(ref y, new[] { "" }, new Func<string>[] { () => Cheats.CleanAround(Cheats.CleanRings) }, Accent);
            _refreshers.Add(() => go[0].text = "Clean " + Cheats.CleanRings + " ring" + (Cheats.CleanRings == 1 ? "" : "s") + " around me");
            Buttons(ref y, new[] { "Finish the Moon (leave 1)" }, new Func<string>[] { () => Cheats.FinishMoon(true) });
            Confirm(ref y, "finish", "Finish the Moon completely", () => Cheats.FinishMoon(false), false);
            Confirm(ref y, "resetdust", "Reset all dust (undo cleaning)", () => Cheats.ResetDust(), true);
            EndCard(c, ref y);

            var s = Card(ref y, null);
            Buttons(ref y, new[] { "Save game now" }, new Func<string>[] { () => Game.SaveNow() });
            EndCard(s, ref y);
        }

        [HideFromIl2Cpp]
        void LandmarkPage(ref float y)
        {
            _flags = Game.UnraisedLandmarks(MoonLandmarkKind.Flag);
            _sites = Game.UnraisedLandmarks(MoonLandmarkKind.Site);
            if (_flagPos >= _flags.Count) _flagPos = 0;
            if (_sitePos >= _sites.Count) _sitePos = 0;

            var m = Card(ref y, "Map");
            Switch(ref y, "Show flags & sites on the map", null, () => Cheats.FlagsOnMap, v => Cheats.FlagsOnMap = v);
            EndCard(m, ref y);

            var f = Card(ref y, "Unraised flags (" + _flags.Count + ")");
            Browser(ref y, "Flag", _flags, () => _flagPos, v => _flagPos = v);
            EndCard(f, ref y);

            var s = Card(ref y, "Unraised sites (" + _sites.Count + ")");
            Browser(ref y, "Site", _sites, () => _sitePos, v => _sitePos = v);
            EndCard(s, ref y);

            var a = Card(ref y, "Everything at once");
            Buttons(ref y, new[] { "Raise all flags", "Find all sites", "Both" }, new Func<string>[] {
                () => Done(Cheats.RaiseLandmarks(MoonLandmarkKind.Flag)), () => Done(Cheats.RaiseLandmarks(MoonLandmarkKind.Site)), () => Done(Cheats.RaiseLandmarks(null)) });
            EndCard(a, ref y);
        }

        /// <summary>"< name >" browser over unraised landmarks, nearest first, with Go and Raise buttons.</summary>
        [HideFromIl2Cpp]
        void Browser(ref float y, string label, List<int> list, Func<int> getPos, Action<int> setPos)
        {
            var world = Game.World;
            if (list.Count == 0) { Hint(ref y, "All " + label.ToLower() + "s raised."); return; }
            Func<string> name = () =>
            {
                if (list.Count == 0 || world == null) return "-";
                var lm = world.Landmark(list[getPos()]);
                return (lm != null ? lm.name : "?") + "   <color=#8B91A0><size=12>" + (getPos() + 1) + " of " + list.Count + (getPos() == 0 ? " - nearest" : "") + "</size></color>";
            };
            float arrow = 38, act = 74;
            SmallButton(_x, y, arrow, "<", () => { setPos((getPos() - 1 + list.Count) % list.Count); return null; });
            var text = Text(_page, "", _x + arrow + 10, y, _w - arrow * 2 - act * 2 - 40, BtnH, 15, true, Cream, TextAlignmentOptions.MidlineLeft);
            SmallButton(_x + _w - act * 2 - arrow - 16, y, arrow, ">", () => { setPos((getPos() + 1) % list.Count); return null; });
            SmallButton(_x + _w - act * 2 - 8, y, act, "Go", () => Cheats.TeleportToLandmark(list[getPos()]), Accent);
            SmallButton(_x + _w - act, y, act, "Raise", () =>
            {
                var w = Game.World; if (w == null) return "Not in a game.";
                w.MarkLandmarkRaised(list[getPos()], Game.PlayerId); Game.MarkDirty();
                _rebuildPending = true;
                return label + " raised.";
            });
            _refreshers.Add(() => text.text = name());
            y += BtnH + Gap;
        }

        [HideFromIl2Cpp]
        void TeleportPage(ref float y)
        {
            var q = Card(ref y, "Landmarks");
            Buttons(ref y, new[] { "Nearest flag", "Nearest site" }, new Func<string>[] {
                () => Cheats.TeleportToNearestLandmark(MoonLandmarkKind.Flag), () => Cheats.TeleportToNearestLandmark(MoonLandmarkKind.Site) }, Accent);
            EndCard(q, ref y);

            var world = Game.World;
            int count = world == null || world.waypoints == null ? 0 : world.waypoints.Count;
            var mk = Card(ref y, "Map markers (" + count + ")");
            if (count == 0) Hint(ref y, "No markers. Right-click the map to place one.");
            for (int i = 0; i < count; i++)
            {
                int index = i;
                ListRow(ref y, "Marker " + (i + 1), "#" + world.waypoints[i].id, "Go", () => Cheats.TeleportToWaypoint(index));
            }
            EndCard(mk, ref y);

            var remotes = Game.RemotePlayers();
            var cm = Card(ref y, "Crewmates (" + remotes.Count + ")");
            if (remotes.Count == 0) Hint(ref y, "Crewmates show up here once they're on the Moon with you.");
            foreach (var rp in remotes)
            {
                string n = rp.DisplayName;
                ListRow(ref y, n, null, "Go", () => Cheats.TeleportToPlayer(n));
            }
            EndCard(cm, ref y);
        }

        [HideFromIl2Cpp]
        void LocationsPage(ref float y)
        {
            var a = Card(ref y, null);
            Buttons(ref y, new[] { "Save where I'm standing" }, new Func<string>[] { () => { _rebuildPending = true; return Locations.SaveHere(null); } }, Accent);
            Hint(ref y, "Named after the nearest landmark. In chat: " + Settings.ChatPrefix.Value + "save base   then   " + Settings.ChatPrefix.Value + "go base");
            EndCard(a, ref y);

            var spots = Locations.All();
            var l = Card(ref y, "Saved (" + spots.Count + ")");
            if (spots.Count == 0) Hint(ref y, "Nothing saved on this save yet.");
            foreach (var spot in spots)
            {
                string n = spot.Name;
                ListRow(ref y, n, null, "Go", () => Locations.Go(n), "Delete", () => { _rebuildPending = true; return Locations.Delete(n); });
            }
            EndCard(l, ref y);
        }

        [HideFromIl2Cpp]
        void BuildPage(ref float y)
        {
            var c = Card(ref y, "Place where you're looking");
            Hint(ref y, "Aim at the ground within 250 m, then click.");
            Buttons(ref y, new[] { "Dust catcher" }, new Func<string>[] { () => Cheats.PlaceCatcher() }, Accent);
            Buttons(ref y, new[] { "Robot dock Mk I", "Mk II", "Joonas" }, new Func<string>[] {
                () => Cheats.PlaceRobotDock(0, "Mk I"), () => Cheats.PlaceRobotDock(1, "Mk II"), () => Cheats.PlaceRobotDock(2, "Joonas") });
            EndCard(c, ref y);
        }

        [HideFromIl2Cpp]
        void CrewPage(ref float y)
        {
            var s = Card(ref y, "Give to");
            var pick = Buttons(ref y, new[] { "" }, new Func<string>[] { () => { _memberIndex = (_memberIndex + 1) % Math.Max(1, _members.Count); return null; } });
            _refreshers.Add(() => pick[0].text = CurrentMember().Name + "   <color=#8B91A0><size=12>click to change</size></color>");
            Buttons(ref y, new[] { "+10k", "+100k", "+1M" }, new Func<string>[] { () => Give(10000), () => Give(100000), () => Give(1000000) }, Accent);
            Buttons(ref y, new[] { "Jetpack", "Boots", "Night vis", "Belt" }, new Func<string>[] {
                () => GiveUp("gear.jetpack"), () => GiveUp("gear.boots"), () => GiveUp("gear.nightvision"), () => GiveUp("gear.belt") });
            Buttons(ref y, new[] { "Speed", "Jump", "Width", "Payout" }, new Func<string>[] {
                () => GiveUp("suit.stride"), () => GiveUp("suit.jump"), () => GiveUp("clean.width"), () => GiveUp("eco.payout") });
            Buttons(ref y, new[] { "Max every upgrade", "All flags & sites" }, new Func<string>[] {
                () => Game.GiveAllUpgrades(CurrentMember().Id), () => Done(Cheats.RaiseLandmarks(null, CurrentMember().Id)) });
            Hint(ref y, "Money goes to the shared team wallet. Upgrades, flags and sites are per player.");
            EndCard(s, ref y);

            var w = Card(ref y, "Who has the trainer");
            foreach (var m in _members)
            {
                var mm = m;
                var name = Text(_page, mm.Name + (mm.Local ? "  <color=#8B91A0><size=12>you</size></color>" : ""), _x, y, _w - 230, BtnH, 15, true, Cream, TextAlignmentOptions.MidlineLeft);
                var chip = Box(_page, "Chip", _x + _w - 220, y + 7, 120, 22, Off, pill: true);
                var chipText = Text(chip.transform, "", 0, 0, 120, 22, 11, true, Cream, TextAlignmentOptions.Center);
                float nextPoll = 0;
                _refreshers.Add(() =>
                {
                    if (Time.unscaledTime < nextPoll) return;
                    nextPoll = Time.unscaledTime + 2f;
                    string v = Game.TrainerVersionOf(mm);
                    string latest = Updates.Latest ?? Plugin.Version;
                    if (Updates.IsNewer(Plugin.Version, latest)) latest = Plugin.Version;
                    if (v == null) { chip.color = Off; chipText.text = "NOT DETECTED"; }
                    else if (Updates.IsNewer(latest, v)) { chip.color = Hex("8A5A12"); chipText.text = "v" + v + "  OLD"; }
                    else { chip.color = Hex("1F5A38"); chipText.text = "v" + v; }
                });
                if (!mm.Local) SmallButton(_x + _w - 90, y, 90, "Send link", () => { Chat.SayInstall(mm.Name); return "Install link posted in chat for " + mm.Name + "."; });
                y += BtnH + Gap;
            }
            Hint(ref y, "Not detected = no trainer, or version 1.0 (which didn't report itself).");
            EndCard(w, ref y);
        }

        [HideFromIl2Cpp]
        void ChatPage(ref float y)
        {
            var p = Card(ref y, "Permissions");
            Switch(ref y, "Crew may use chat commands", null, () => Settings.ChatCommands.Value, v => Settings.ChatCommands.Value = v);
            Switch(ref y, "Including money, upgrades, flags", null, () => Settings.ChatGiveables.Value, v => Settings.ChatGiveables.Value = v);
            Switch(ref y, "Only allowlisted players", null, () => Settings.ChatAllowlistOnly.Value, v => Settings.ChatAllowlistOnly.Value = v);
            foreach (var m in _members)
            {
                if (m.Local) continue;
                string n = m.Name; ulong id = m.SteamId;
                Switch(ref y, "Allow " + n, null, () => Settings.OnAllowlist(n, id, false), v => Settings.SetAllowlisted(n, id, v));
            }
            EndCard(p, ref y);

            var l = Card(ref y, "Limits");
            Stepper(ref y, "Cooldown", () => Settings.ChatCooldown.Value, v => Settings.ChatCooldown.Value = Mathf.Clamp(v, 0, 60), 1f, "0", " s");
            Stepper(ref y, "Money per player", () => Settings.ChatSessionMoneyCap.Value / 1000f, v => Settings.ChatSessionMoneyCap.Value = Mathf.Clamp(v, 0, 1000000) * 1000f, 1000f, "N0", "k");
            EndCard(l, ref y);

            var o = Card(ref y, "Messages");
            Switch(ref y, "Welcome people who join", null, () => Settings.ChatWelcome.Value, v => Settings.ChatWelcome.Value = v);
            Switch(ref y, "Reply in chat", null, () => Settings.ChatReply.Value, v => Settings.ChatReply.Value = v);
            EndCard(o, ref y);

            var c = Card(ref y, "Commands");
            var t = Hint(ref y, Settings.ChatPrefix.Value + Chat.Help.Replace(", ", "   " + Settings.ChatPrefix.Value), 64);
            EndCard(c, ref y);
        }

        [HideFromIl2Cpp]
        void BackupsPage(ref float y)
        {
            _backups = Game.Backups();
            var a = Card(ref y, null);
            Buttons(ref y, new[] { "Make a backup now" }, new Func<string>[] { () => { _rebuildPendingAt = Time.unscaledTime + 6f; return Game.BackupNow(); } }, Accent);
            Hint(ref y, "Restoring backs up your current state first, then reloads the shift. Host only.");
            EndCard(a, ref y);

            var l = Card(ref y, "Recent backups (" + _backups.Count + ")");
            if (_backups.Count == 0) Hint(ref y, "No backups for this save yet.");
            int shown = 0;
            foreach (var b in _backups)
            {
                if (shown++ >= 10) break;
                string manifest = b;
                Text(_page, Game.BackupLabel(b), _x, y, _w - 120, BtnH, 15, true, Cream, TextAlignmentOptions.MidlineLeft);
                ConfirmSmall(_x + _w - 100, y, 100, "restore:" + manifest, "Restore", () => Game.RestoreBackup(manifest));
                y += BtnH + Gap;
            }
            EndCard(l, ref y);
        }

        [HideFromIl2Cpp]
        void SettingsPage(ref float y)
        {
            var g = Card(ref y, "General");
            Switch(ref y, "Remember toggles between launches", null, () => Settings.RememberCheats.Value, v => Settings.RememberCheats.Value = v);
            Switch(ref y, "Show active cheats on screen", null, () => Settings.ShowHud.Value, v => Settings.ShowHud.Value = v);
            Switch(ref y, "Check for updates at startup", null, () => Settings.CheckForUpdates.Value, v => Settings.CheckForUpdates.Value = v);
            Switch(ref y, "Menu animations and glow", "Turn off if they're distracting or the game is struggling", () => Settings.MenuEffects.Value, v => Settings.MenuEffects.Value = v);
            Switch(ref y, "Menu click sound", null, () => Settings.MenuSounds.Value, v => Settings.MenuSounds.Value = v);
            EndCard(g, ref y);

            var k = Card(ref y, "Hotkeys");
            KeyRow(ref y, "Open menu", Settings.MenuKey.Value);
            KeyRow(ref y, "Fly", Settings.FlyKey.Value);
            KeyRow(ref y, "No clip", Settings.NoclipKey.Value);
            KeyRow(ref y, "Speed boost", Settings.SpeedKey.Value);
            KeyRow(ref y, "Unlimited jetpack", Settings.JetpackKey.Value);
            KeyRow(ref y, "Save game", Settings.SaveKey.Value);
            KeyRow(ref y, "Show / hide active line", Settings.HudKey.Value);
            Hint(ref y, "Change them in the game's MODS screen > Moon Trainer > Settings.");
            EndCard(k, ref y);

            var o = Card(ref y, "Other");
            Buttons(ref y, new[] { "Reset menu position" }, new Func<string>[] { () =>
            {
                Settings.MenuX.Value = 60; Settings.MenuY.Value = -60;
                _frame.anchoredPosition = new Vector2(60, -60);
                return "Menu moved back.";
            } });
            Confirm(ref y, "achievements", "Unlock all Steam achievements", () => Cheats.UnlockAchievements(), true);
            EndCard(o, ref y);
        }

        /// <summary>On a crewmate's PC, says plainly that these buttons act on things the host's game owns.</summary>
        [HideFromIl2Cpp]
        void HostOnly(ref float y, string what)
        {
            if (!Game.IsCrewmate) return;
            var c = Card(ref y, null, Hex("3A2412"));
            Hint(ref y, "<color=#FF8C33>You're not the host of this lobby.</color>  The host's game owns " + what
                + ", so these buttons may do nothing here or be undone. Ask the host to do it, or use the chat commands.").color = Cream;
            EndCard(c, ref y);
        }

        [HideFromIl2Cpp]
        Game.Member CurrentMember() { return _members.Count == 0 ? new Game.Member { Id = Game.PlayerId, Name = "You", Local = true } : _members[_memberIndex]; }

        [HideFromIl2Cpp]
        string AddMoney(double d) { var b = Game.AddMoney(Game.PlayerId, d); return "+$" + d.ToString("N0") + "  (now $" + b.ToString("N0") + ")"; }
        [HideFromIl2Cpp]
        string SetMoney(double d) { var b = Game.SetMoney(Game.PlayerId, d); return "Wallet set to $" + b.ToString("N0"); }
        [HideFromIl2Cpp]
        string Give(double d) { var m = CurrentMember(); var b = Game.AddMoney(m.Id, d); return "+$" + d.ToString("N0") + " for " + m.Name + "  (team now $" + b.ToString("N0") + ")"; }
        [HideFromIl2Cpp]
        string GiveUp(string id) { var m = CurrentMember(); return m.Name + ": " + Game.GiveUpgrade(m.Id, id); }
        [HideFromIl2Cpp]
        string Done(string msg) { _rebuildPending = true; return msg; }

        // ---- refresh -----------------------------------------------------------------------------------

        [HideFromIl2Cpp]
        void Refresh()
        {
            _nextRefresh = Time.unscaledTime + 0.2f;
            foreach (var r in _chrome) { try { r(); } catch (Exception) { } }
            foreach (var r in _refreshers) { try { r(); } catch (Exception) { } }
            _anyActive = Cheats.ActiveSummary().Length > 0;

            var world = Game.World;
            if (world != null && (world.WaypointRevision != _waypointRevision || world.LandmarkRevision != _landmarkRevision)
                && (_tab == "Teleport" || _tab == "Flags & Sites")) _rebuildPending = true;
            if ((Updates.Available != null) != _updateShown && _tab == "Home") _rebuildPending = true;
            if (_tab == "Locations" && Locations.All().Count != _locationCount) _rebuildPending = true;
        }

        [HideFromIl2Cpp]
        void Toast(string message)
        {
            if (string.IsNullOrEmpty(message) || _toastText == null) return;
            _toastText.text = message;
            _toastUntil = Time.unscaledTime + 4f;
            _noticeShownUntil = _noticeUntil;
            Plugin.Logger.LogInfo(message);
        }

        float _noticeShownUntil;

        [HideFromIl2Cpp]
        void UpdateToast()
        {
            if (_toastText == null || _toastRt == null) return;
            // Show trainer notices (e.g. from chat commands or hotkeys) in the toast while the menu is open.
            if (_notice != null && _noticeUntil != _noticeShownUntil && Time.unscaledTime < _noticeUntil)
            {
                _noticeShownUntil = _noticeUntil;
                _toastText.text = _notice;
                _toastUntil = Mathf.Max(_toastUntil, _noticeUntil - 1f);
            }
            bool show = Time.unscaledTime < _toastUntil;
            if (_toastGroup == null) { if (_toastRt.gameObject.activeSelf != show) _toastRt.gameObject.SetActive(show); return; }
            float a = Mathf.MoveTowards(_toastGroup.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            if (a == _toastShown) return;
            _toastShown = a;
            _toastGroup.alpha = a;
            _toastRt.anchoredPosition = new Vector2(Side + CP, -(PH - 56) - (Fx ? (1f - EaseOut(a)) * 14f : 0f));
        }

        // ---- widgets ---------------------------------------------------------------------------------

        struct CardInfo { public RectTransform Bg; public float Top; }

        /// <summary>Starts a card (optional small-caps title above it). Widgets go inside until EndCard.</summary>
        [HideFromIl2Cpp]
        CardInfo Card(ref float y, string title, Color? color = null)
        {
            if (title != null)
            {
                var t = Text(_page, title.ToUpper(), CP + 2, y, CW, 18, 11, true, Faint, TextAlignmentOptions.BottomLeft);
                t.characterSpacing = 4;
                y += 24;
            }
            var bg = Box(_page, "Card", CP, y, CW, 10, color ?? CardBg, 12);
            var info = new CardInfo { Bg = bg.rectTransform, Top = y };
            y += 14;
            _x = CP + 16; _w = CW - 32;
            return info;
        }

        [HideFromIl2Cpp]
        void EndCard(CardInfo c, ref float y)
        {
            y += 6;
            c.Bg.sizeDelta = new Vector2(CW, y - c.Top);
            y += 18;
            _x = CP; _w = CW;
        }

        [HideFromIl2Cpp]
        TMP_Text Label(ref float y, string text, float size, bool bold)
        {
            var t = Text(_page, text, _x, y, _w, 28, size, bold, Cream, TextAlignmentOptions.MidlineLeft);
            y += 32;
            return t;
        }

        [HideFromIl2Cpp]
        TMP_Text Hint(ref float y, string text, float h = 0)
        {
            var t = Text(_page, text, _x, y, _w, 20, 12, false, Dim, TextAlignmentOptions.TopLeft);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            if (h <= 0)
            {
                try { h = Mathf.Max(18, t.GetPreferredValues(text, _w, 0).y); }
                catch (Exception) { h = 18 + 16 * (int)(text.Length * 6.5f / Mathf.Max(1, _w)); }   // rough: ~6.5 px a letter
            }
            ((RectTransform)t.transform).sizeDelta = new Vector2(_w, h);
            y += h + 8;
            return t;
        }

        /// <summary>Label (and optional sub-label) on the left, an on/off switch on the right; the whole row is clickable.</summary>
        [HideFromIl2Cpp]
        void Switch(ref float y, string label, string sub, Func<bool> get, Action<bool> set)
        {
            var row = Box(_page, "Row", _x - 8, y, _w + 16, RowH, Color.clear, 8);
            MakeClickable(row, () => { set(!get()); return null; }, Color.clear, new Color(1, 1, 1, 0.035f));
            if (sub == null)
                Text(_page, label, _x, y, _w - 70, RowH, 15, true, Cream, TextAlignmentOptions.MidlineLeft);
            else
            {
                Text(_page, label, _x, y + 3, _w - 70, 22, 15, true, Cream, TextAlignmentOptions.MidlineLeft);
                Text(_page, sub, _x, y + 22, _w - 70, 16, 11, false, Dim, TextAlignmentOptions.MidlineLeft);
            }
            var track = Box(_page, "Track", _x + _w - 44, y + 10, 44, 22, Off, pill: true);
            var knob = Box(track.transform, "Knob", 3, 3, 16, 16, KnobOff, pill: true);
            var k = new Knob { Track = track, Dot = knob };
            _knobs.Add(k);
            _refreshers.Add(() => k.On = get());          // Animate moves the knob towards this
            y += RowH;
        }

        /// <summary>Label on the left, a "- value +" pill on the right.</summary>
        [HideFromIl2Cpp]
        void Stepper(ref float y, string label, Func<float> get, Action<float> set, float step, string fmt, string unit = "", float bigStep = 0)
        {
            Text(_page, label, _x, y, _w - 200, RowH, 15, true, Cream, TextAlignmentOptions.MidlineLeft);
            float pw = bigStep > 0 ? 220 : 160, px = _x + _w - pw, py = y + 6;
            Box(_page, "Pill", px, py, pw, 30, Btn, pill: true);
            float bx = px;
            if (bigStep > 0) { PillButton(bx, py, 36, "<<", () => set(get() - bigStep)); bx += 30; }
            PillButton(bx, py, 36, "-", () => set(get() - step));
            float rx = px + pw - 36;
            if (bigStep > 0) { PillButton(rx, py, 36, ">>", () => set(get() + bigStep)); rx -= 30; }
            PillButton(rx, py, 36, "+", () => set(get() + step));
            var value = Text(_page, "", bx + 36, py, rx - bx - 36, 30, 14, true, Cream, TextAlignmentOptions.Center);
            Count(value, () => get(), v => ((float)v).ToString(fmt) + "<color=#8B91A0>" + unit + "</color>");
            y += RowH;
        }

        [HideFromIl2Cpp]
        void PillButton(float x, float y, float w, string glyph, Action action)
        {
            var b = Box(_page, "Step", x, y, w, 30, Color.clear, pill: true);
            Text(b.transform, glyph, 0, 0, w, 30, 17, true, Dim, TextAlignmentOptions.Center);
            MakeClickable(b, () => { action(); return null; }, Color.clear, new Color(1, 1, 1, 0.08f));
        }

        /// <summary>A row of equal-width buttons. Returns their labels so callers can update them.</summary>
        [HideFromIl2Cpp]
        TMP_Text[] Buttons(ref float y, string[] labels, Func<string>[] actions, Color? color = null)
        {
            var texts = new TMP_Text[labels.Length];
            float w = (_w - Gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
                texts[i] = SmallButton(_x + i * (w + Gap), y, w, labels[i], actions[i], color);
            y += BtnH + Gap;
            return texts;
        }

        [HideFromIl2Cpp]
        TMP_Text SmallButton(float x, float y, float w, string label, Func<string> action, Color? color = null)
        {
            var c = color ?? Btn;
            bool bright = color.HasValue && (c == Accent || c == Info);
            var halo = Color.Lerp(c, Color.white, 0.25f);    // a little brighter than the button, so it shows on the dark card
            Image glow = bright ? Glow(_page, x - 10, y - 10, w + 20, BtnH + 20, new Color(halo.r, halo.g, halo.b, 0f), 3f) : null;
            var b = Box(_page, "Button", x, y, w, BtnH, c, 8);
            MakeClickable(b, action, c, Color.Lerp(c, Color.white, 0.12f));
            Lift(b, glow, halo, _hotPage);
            // light text on dark buttons, dark text on light ones
            bool lightButton = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b > 0.5f;
            return Text(b.transform, label, 10, 0, w - 20, BtnH, 14, true, lightButton ? Hex("16120E") : Cream, TextAlignmentOptions.Center);
        }

        /// <summary>Name (and faint detail) with one or two action buttons on the right.</summary>
        [HideFromIl2Cpp]
        void ListRow(ref float y, string name, string detail, string act1, Func<string> do1, string act2 = null, Func<string> do2 = null)
        {
            float bw = 80;
            float right = act2 != null ? bw * 2 + Gap : bw;
            Text(_page, name + (detail != null ? "   <color=#5B6170><size=12>" + detail + "</size></color>" : ""), _x, y, _w - right - 10, BtnH, 15, true, Cream, TextAlignmentOptions.MidlineLeft);
            SmallButton(_x + _w - right, y, bw, act1, do1, Accent);
            if (act2 != null) SmallButton(_x + _w - bw, y, bw, act2, do2, Danger);
            y += BtnH + Gap;
        }

        [HideFromIl2Cpp]
        void KeyRow(ref float y, string label, string key)
        {
            Text(_page, label, _x, y, _w - 140, 30, 14, false, Cream, TextAlignmentOptions.MidlineLeft);
            bool none = string.IsNullOrEmpty(key) || key == "None";
            var k = Box(_page, "Key", _x + _w - 110, y + 3, 110, 24, none ? Color.clear : Btn, 6);
            Text(k.transform, none ? "-" : key, 0, 0, 110, 24, 12, true, none ? Faint : Cream, TextAlignmentOptions.Center);
            y += 32;
        }

        /// <summary>A full-width button that needs a second click within 4 seconds.</summary>
        [HideFromIl2Cpp]
        void Confirm(ref float y, string key, string label, Func<string> action, bool danger)
        {
            var t = SmallButton(_x, y, _w, label, ConfirmAction(key, label, action), danger ? Danger : Btn);
            _refreshers.Add(() => t.text = Armed(key) ? "Click again to confirm" : label);
            y += BtnH + Gap;
        }

        [HideFromIl2Cpp]
        void ConfirmSmall(float x, float y, float w, string key, string label, Func<string> action)
        {
            var t = SmallButton(x, y, w, label, ConfirmAction(key, label, action), Danger);
            _refreshers.Add(() => t.text = Armed(key) ? "Sure?" : label);
        }

        [HideFromIl2Cpp]
        bool Armed(string key) { return _confirmKey == key && Time.unscaledTime < _confirmUntil; }

        [HideFromIl2Cpp]
        Func<string> ConfirmAction(string key, string label, Func<string> action)
        {
            return () =>
            {
                if (Armed(key)) { _confirmKey = null; return action(); }
                _confirmKey = key; _confirmUntil = Time.unscaledTime + 4f;
                return "Click \"" + label + "\" again to confirm.";
            };
        }

        static ColorBlock Colors(Color normal, Color hover)
        {
            var c = ColorBlock.defaultColorBlock;
            c.normalColor = normal; c.highlightedColor = hover; c.selectedColor = normal; c.pressedColor = Color.Lerp(hover, Color.white, 0.15f);
            c.disabledColor = normal; c.colorMultiplier = 1; c.fadeDuration = 0.08f;
            return c;
        }

        [HideFromIl2Cpp]
        void MakeClickable(Image image, Func<string> action, Color normal, Color hover)
        {
            image.raycastTarget = true;
            image.color = Color.white;   // the button's colour block tints it
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = Colors(normal, hover);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() => Run(action))));
        }

        [HideFromIl2Cpp]
        void Run(Func<string> action)
        {
            Click();
            try { var msg = action(); if (msg != null) Toast(msg); }
            catch (Exception e) { Toast("Failed: " + e.Message); Plugin.Logger.LogError(e); }
            _nextRefresh = 0;
            // Unity keeps a clicked button "selected", which hides its hover highlight until something else is clicked.
            try { var es = EventSystem.current; if (es != null) es.SetSelectedGameObject(null); } catch (Exception) { }
        }

        // ---- effects ------------------------------------------------------------------------------------

        /// <summary>A soft blob of colour (the shadow sprite, tinted). Null if this game build can't do it.</summary>
        static Image Glow(Transform parent, float x, float y, float w, float h, Color color, float tightness)
        {
            if (_soft == null || _spritesFailed) return null;
            try
            {
                var img = Box(parent, "Glow", x, y, w, h, color, 0);
                img.sprite = _soft; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = tightness;
                return img;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Registers a button to grow slightly under the mouse (and light its glow, if it has one).</summary>
        [HideFromIl2Cpp]
        void Lift(Image image, Image glow, Color glowColor, List<Hot> list)
        {
            try
            {
                var rt = image.rectTransform;
                var size = rt.sizeDelta; var pos = rt.anchoredPosition;
                rt.pivot = new Vector2(0.5f, 0.5f);            // grow from the middle, staying where it was
                rt.anchoredPosition = new Vector2(pos.x + size.x / 2f, pos.y - size.y / 2f);
                list.Add(new Hot { Button = image.GetComponent<Button>(), Rt = rt, Glow = glow, GlowColor = glowColor });
            }
            catch (Exception) { }
        }

        /// <summary>Shows a number that counts to its new value when it changes.</summary>
        [HideFromIl2Cpp]
        void Count(TMP_Text text, Func<double> get, Func<double, string> fmt)
        {
            var c = new Counter { Text = text, Fmt = fmt };
            _counters.Add(c);
            _refreshers.Add(() =>
            {
                double v = get();
                if (!c.Init) { c.Init = true; c.From = c.To = c.Shown = v; c.P = 1f; c.Text.text = fmt(v); }
                else if (v != c.To) { c.From = c.Shown; c.To = v; c.P = 0f; }
            });
        }

        /// <summary>Runs every frame the window is up: opening and closing, tab changes, hover, switches, numbers, glow.</summary>
        [HideFromIl2Cpp]
        void Animate()
        {
            if (!Alive(_canvas) || !_canvas.activeSelf || _panel == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool fx = Fx;

            // ---- the window: fades and grows in, shrinks and fades out ----
            _openT = fx ? Mathf.MoveTowards(_openT, _open ? 1f : 0f, dt / (_open ? 0.16f : 0.10f)) : (_open ? 1f : 0f);
            if (_openT != _openShown || fx != _fxShown)
            {
                _openShown = _openT;
                float e = EaseOut(_openT);
                if (_panelGroup != null) _panelGroup.alpha = e;
                float sc = Mathf.Lerp(0.955f, 1f, e);
                _panel.localScale = new Vector3(sc, sc, 1f);
                if (_dim != null) _dim.color = new Color(0, 0, 0, fx ? 0.26f * e : 0f);
            }
            if (!_open)
            {
                if (_openT <= 0f) _canvas.SetActive(false);
                _fxShown = fx;
                return;
            }

            // ---- the page: slides and fades in after a tab switch ----
            _pageT = fx ? Mathf.MoveTowards(_pageT, 1f, dt / 0.18f) : 1f;
            if (_pageT != _pageShown && _page != null)
            {
                _pageShown = _pageT;
                float e = EaseOut(_pageT);
                if (_pageGroup != null) _pageGroup.alpha = e;
                var pp = _page.anchoredPosition; pp.x = (1f - e) * 24f; _page.anchoredPosition = pp;
            }

            // ---- sidebar: the highlight and the orange bar glide to the active tab ----
            int index = Mathf.Max(0, Array.IndexOf(Tabs, _tab));
            float wantY = Head + 12 + index * 36;
            if (_tabY < 0f || !fx || Mathf.Abs(wantY - _tabY) < 0.4f) _tabY = wantY;
            else _tabY = Mathf.Lerp(_tabY, wantY, 1f - Mathf.Exp(-dt * 22f));
            if (_tabY != _tabYShown)
            {
                _tabYShown = _tabY;
                if (_tabHi != null) _tabHi.anchoredPosition = new Vector2(10, -_tabY);
                if (_tabBar != null) _tabBar.anchoredPosition = new Vector2(10, -(_tabY + 8));
            }
            for (int i = 0; i < _tabLabels.Count && i < _tabT.Length; i++)
            {
                float want = i == index ? 1f : 0f, cur = _tabT[i];
                float next = (cur < 0f || !fx) ? want : Mathf.MoveTowards(cur, want, dt / 0.15f);
                if (next == cur) continue;
                _tabT[i] = next;
                _tabLabels[i].color = Color.Lerp(Dim, Cream, next);
            }

            // ---- buttons: grow a touch under the mouse, dip when pressed, glow if they're orange ----
            if (!_hoverBroken)
            {
                try { Hover(_hotChrome, fx, dt); Hover(_hotPage, fx, dt); }
                catch (Exception e) { _hoverBroken = true; Plugin.Logger.LogWarning("Button hover effects unavailable: " + e.Message); }
            }

            // ---- switches: the knob glides across and the track fades between grey and orange ----
            foreach (var k in _knobs)
            {
                float want = k.On ? 1f : 0f;
                if (k.T == want) { k.Init = true; continue; }
                k.T = (!k.Init || !fx) ? want : Mathf.MoveTowards(k.T, want, dt / 0.14f);    // a freshly built page starts settled
                k.Init = true;
                float e = Smooth(k.T);
                k.Track.color = Color.Lerp(Off, Accent, e);
                k.Dot.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(3f, 25f, e), -3f);
                k.Dot.color = Color.Lerp(KnobOff, KnobOn, e);
            }

            // ---- numbers: count to their new value ----
            foreach (var c in _counters)
            {
                if (c.P >= 1f) continue;
                c.P = fx ? Mathf.MoveTowards(c.P, 1f, dt / 0.24f) : 1f;
                c.Shown = c.P >= 1f ? c.To : c.From + (c.To - c.From) * EaseOut(c.P);
                c.Text.text = c.Fmt(c.Shown);
            }

            // ---- glow: the logo breathes; the Home dot pulses while anything is switched on ----
            if (_homeDot != null && _homeDot.gameObject.activeSelf != _anyActive)
            {
                _homeDot.gameObject.SetActive(_anyActive);
                if (_homeDotGlow != null) _homeDotGlow.gameObject.SetActive(_anyActive);
            }
            if (fx)
            {
                float now = Time.unscaledTime;
                if (_logoGlow != null) _logoGlow.color = new Color(AccentSoft.r, AccentSoft.g, AccentSoft.b, 0.17f + 0.09f * Mathf.Sin(now * 1.7f));
                if (_anyActive && _homeDot != null)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(now * 3.4f);
                    _homeDot.color = new Color(AccentSoft.r, AccentSoft.g, AccentSoft.b, 0.7f + 0.3f * pulse);
                    if (_homeDotGlow != null) _homeDotGlow.color = new Color(AccentSoft.r, AccentSoft.g, AccentSoft.b, 0.12f + 0.36f * pulse);
                }
            }
            else if (_fxShown)
            {
                // just switched off: leave everything in a calm resting state
                if (_logoGlow != null) _logoGlow.color = new Color(AccentSoft.r, AccentSoft.g, AccentSoft.b, 0.17f);
                if (_homeDot != null) _homeDot.color = AccentSoft;
                if (_homeDotGlow != null) _homeDotGlow.color = new Color(AccentSoft.r, AccentSoft.g, AccentSoft.b, 0.25f);
            }
            _fxShown = fx;
        }

        [HideFromIl2Cpp]
        void Hover(List<Hot> list, bool fx, float dt)
        {
            foreach (var h in list)
            {
                bool down = fx && h.Button.IsPressed();
                bool over = fx && (down || h.Button.IsHighlighted());
                float t = Mathf.MoveTowards(h.T, over ? 1f : 0f, dt / 0.10f);
                float press = Mathf.MoveTowards(h.Press, down ? 1f : 0f, dt / 0.06f);
                if (t == h.T && press == h.Press) continue;
                h.T = t; h.Press = press;
                float sc = 1f + 0.03f * EaseOut(t) - 0.05f * press;
                h.Rt.localScale = new Vector3(sc, sc, 1f);
                if (h.Glow != null) h.Glow.color = new Color(h.GlowColor.r, h.GlowColor.g, h.GlowColor.b, 0.34f * EaseOut(t));
            }
        }

        // ---- primitives ---------------------------------------------------------------------------------

        static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        [HideFromIl2Cpp]
        TMP_Text AddText(GameObject go, float size, bool bold, Color color, TextAlignmentOptions align)
        {
            var t = go.AddComponent<TextMeshProUGUI>();
            var font = bold ? _fontBold : _fontRegular;
            if (font != null) t.font = font;
            t.fontSize = size; t.color = color; t.alignment = align; t.richText = true; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        [HideFromIl2Cpp]
        TMP_Text Text(Transform parent, string value, float x, float y, float w, float h, float size, bool bold, Color color, TextAlignmentOptions align)
        {
            var rt = Rect(parent, "Text", x, y, w, h);
            var t = AddText(rt.gameObject, size, bold, color, align);
            t.text = value;
            return t;
        }

        [HideFromIl2Cpp]
        void FindFonts()
        {
            if (_fontBold != null && _fontRegular != null) return;
            var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_FontAsset>());
            for (int i = 0; i < all.Length; i++)
            {
                var f = all[i].TryCast<TMP_FontAsset>();
                if (f == null) continue;
                if (f.name == "Outfit Bold SDF") _fontBold = f;
                else if (f.name == "Outfit SDF") _fontRegular = f;
                else if (_fontRegular == null) _fontRegular = f;
            }
            if (_fontBold == null) _fontBold = _fontRegular;
            if (_fontRegular == null) _fontRegular = _fontBold;
        }
    }
}
