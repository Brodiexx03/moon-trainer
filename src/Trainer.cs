using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
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
        const float W = 460, Pad = 16, Row = 34, Gap = 6, TitleH = 64;
        static readonly Color PanelColor = new Color(0.07f, 0.07f, 0.09f, 0.95f);
        static readonly Color Accent = new Color(1f, 0.55f, 0.2f, 1f);
        static readonly Color BtnColor = new Color(0.18f, 0.18f, 0.21f, 1f);
        static readonly Color BtnHover = new Color(0.30f, 0.30f, 0.34f, 1f);
        static readonly Color HeaderColor = new Color(0.12f, 0.12f, 0.14f, 1f);
        static readonly Color Cream = new Color(0.95f, 0.93f, 0.89f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.60f, 0.58f, 1f);
        static readonly Color Danger = new Color(0.55f, 0.16f, 0.14f, 1f);

        static Trainer _instance;

        bool _open, _uiBlocked;
        GameObject _canvas, _hudCanvas;
        Canvas _canvasComp;
        RectTransform _panel, _title, _scrollContent;
        TMP_FontAsset _fontBold, _fontRegular;
        TMP_Text _status, _hudLine, _hudNotice;
        readonly List<Action> _refreshers = new List<Action>();
        float _nextRefresh, _statusUntil, _nextPersist, _nextAnnounce;
        List<string> _backups = new List<string>();
        int _locationCount = -1;
        bool _rebuildPending; float _rebuildPendingAt;

        // dragging
        bool _dragging; Vector2 _dragStart, _panelStart;

        // things that force a rebuild when they change
        int _waypointRevision = -1, _landmarkRevision = -1;
        bool _updateShown;

        // crew selection
        int _memberIndex;
        List<Game.Member> _members = new List<Game.Member>();

        // landmark browser
        List<int> _flags = new List<int>(), _sites = new List<int>();
        int _flagPos, _sitePos;

        // two-click confirmations
        string _confirmKey; float _confirmUntil;

        static string _notice; static float _noticeUntil;
        public static void Notice(string text) { _notice = text; _noticeUntil = Time.unscaledTime + 5f; Plugin.Logger.LogInfo(text); }
        public static void MembersChanged() { if (_instance != null) _instance._rebuildPending = true; }

        bool _chimed; float _chimeAt;

        void Start()
        {
            _instance = this;
            _chimeAt = Time.unscaledTime + 2f;   // chime once the first scene is up so the player hears the mod attached
            Notice("Moon Trainer " + Plugin.Version + " loaded - " + Settings.MenuKey.Value + " opens the menu");
        }

        /// <summary>Two rising tones generated on the fly (no audio files needed).</summary>
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
                var src = gameObject.GetComponent<AudioSource>();
                if (src == null) src = gameObject.AddComponent<AudioSource>();
                src.spatialBlend = 0f; src.volume = 0.8f; src.ignoreListenerPause = true;
                src.PlayOneShot(clip);
            }
            catch (Exception e) { Plugin.Logger.LogWarning("Chime failed: " + e.Message); }
        }

        void Update()
        {
            try
            {
                if (!_chimed && Time.unscaledTime >= _chimeAt) { _chimed = true; Chime(660f, 990f); }

                // An update was found in the background: say so once, in game, with a different chime.
                if (Updates.Available != null && !Updates.Announced && Game.InGame)
                {
                    Updates.Announced = true;
                    Notice("Moon Trainer " + Updates.Available + " is available - open the menu (" + Settings.MenuKey.Value + ") to update.");
                    Chime(990f, 1320f);
                }

                HandleKeys();
                Cheats.Tick();
                Chat.Tick();
                Game.TickBackups();
                if (Time.unscaledTime >= _nextAnnounce) { _nextAnnounce = Time.unscaledTime + 5f; Game.AnnounceVersion(); }
                UpdateHud();

                if (Time.unscaledTime >= _nextPersist) { _nextPersist = Time.unscaledTime + 2f; Settings.SaveCheatsIfChanged(); }

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
                }
            }
            catch (Exception e) { Plugin.Logger.LogError(e); }
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (Pressed(kb, Settings.MenuKey.Value)) Toggle();
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

        void Toggle() { if (_open) Close(); else Open(); }

        void Open()
        {
            if (!Game.InGame) { Notice("Load into a game first."); return; }
            if (_canvas == null || _canvas.WasCollected || !_canvas) Build();
            _canvas.SetActive(true);
            _open = true;
            _rebuildPending = true;
        }

        void Close()
        {
            _open = false;
            _dragging = false;
            if (_canvas != null && !_canvas.WasCollected && _canvas) _canvas.SetActive(false);
            var walker = Game.Walker;
            if (walker != null && _uiBlocked) walker.UIBlocked = false;
            _uiBlocked = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // ---- dragging the title bar ---------------------------------------------------------------

        void Drag()
        {
            var mouse = Mouse.current;
            if (mouse == null || _title == null) return;
            Vector2 pos = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame && RectTransformUtility.RectangleContainsScreenPoint(_title, pos, null))
            {
                _dragging = true; _dragStart = pos; _panelStart = _panel.anchoredPosition;
            }
            if (_dragging)
            {
                if (mouse.leftButton.isPressed)
                {
                    float scale = _canvasComp != null && _canvasComp.scaleFactor > 0 ? _canvasComp.scaleFactor : 1f;
                    _panel.anchoredPosition = _panelStart + (pos - _dragStart) / scale;
                }
                else
                {
                    _dragging = false;
                    Settings.MenuX.Value = _panel.anchoredPosition.x;
                    Settings.MenuY.Value = _panel.anchoredPosition.y;
                }
            }
        }

        // ---- HUD (active cheats + notices) --------------------------------------------------------

        void UpdateHud()
        {
            if (_hudCanvas == null || _hudCanvas.WasCollected || !_hudCanvas)
            {
                if (!Game.InGame) return;
                BuildHud();
            }
            bool inGame = Game.InGame;
            string line = "";
            if (inGame && !_open && Settings.ShowHud.Value)
            {
                line = Cheats.ActiveSummary();
                if (Updates.Available != null) line = (line.Length > 0 ? line + "      " : "") + "<color=#7FD4FF>UPDATE " + Updates.Available + " AVAILABLE</color>";
            }
            _hudLine.text = line;
            _hudNotice.text = (_notice != null && Time.unscaledTime < _noticeUntil) ? _notice : "";
        }

        void BuildHud()
        {
            FindFonts();
            _hudCanvas = new GameObject("MoonTrainer HUD");
            DontDestroyOnLoad(_hudCanvas);
            var canvas = _hudCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30999;
            var scaler = _hudCanvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;

            var top = Rect(_hudCanvas.transform, "Active", 0, 10, 1400, 26);
            top.anchorMin = top.anchorMax = new Vector2(0.5f, 1); top.pivot = new Vector2(0.5f, 1);
            top.anchoredPosition = new Vector2(0, -10);
            _hudLine = AddText(top.gameObject, 15, true, Accent, TextAlignmentOptions.Center);

            var bottom = Rect(_hudCanvas.transform, "Notice", 0, 0, 1200, 30);
            bottom.anchorMin = bottom.anchorMax = new Vector2(0, 0); bottom.pivot = new Vector2(0, 0);
            bottom.anchoredPosition = new Vector2(20, 18);
            _hudNotice = AddText(bottom.gameObject, 18, true, Cream, TextAlignmentOptions.BottomLeft);
        }

        // ---- building the menu ------------------------------------------------------------------------

        void Build()
        {
            FindFonts();
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

            float height = 1000;
            _panel = Rect(_canvas.transform, "Panel", 0, 0, W, height);
            _panel.anchoredPosition = new Vector2(Settings.MenuX.Value, Settings.MenuY.Value);
            _panel.gameObject.AddComponent<Image>().color = PanelColor;
            Rect(_panel, "Accent", 0, 0, 5, height).gameObject.AddComponent<Image>().color = Accent;

            float inner = W - Pad * 2;
            _title = Rect(_panel, "Title", 0, 0, W, TitleH);
            Text(_panel, "MOON TRAINER", Pad, Pad - 4, inner, 34, 26, true, Cream, TextAlignmentOptions.MidlineLeft);
            Text(_panel, "v" + Plugin.Version + "  ·  drag to move  ·  " + Settings.MenuKey.Value + " closes", Pad, Pad - 4, inner, 34, 12, false, Dim, TextAlignmentOptions.MidlineRight);
            _status = Text(_panel, "", Pad, Pad + 32, inner, 22, 14, false, Accent, TextAlignmentOptions.MidlineLeft);

            var view = Rect(_panel, "Body", Pad, TitleH + 8, inner, height - TitleH - 8 - Pad);
            view.gameObject.AddComponent<RectMask2D>();
            _scrollContent = Rect(view, "Content", 0, 0, inner, 10);
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = _scrollContent; scroll.viewport = view; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40; scroll.inertia = false;

            Plugin.Logger.LogInfo("Trainer menu built.");
        }

        /// <summary>Clears the menu body and lays every section out again (collapsed sections show just their header).</summary>
        void Rebuild()
        {
            for (int i = _scrollContent.childCount - 1; i >= 0; i--) Destroy(_scrollContent.GetChild(i).gameObject);
            _refreshers.Clear();

            var world = Game.World;
            _waypointRevision = world == null ? -1 : world.WaypointRevision;
            _landmarkRevision = world == null ? -1 : world.LandmarkRevision;
            _updateShown = Updates.Available != null;
            _members = Game.Members();
            if (_memberIndex >= _members.Count) _memberIndex = 0;
            _backups = Game.Backups();
            _locationCount = Locations.All().Count;
            _flags = Game.UnraisedLandmarks(MoonLandmarkKind.Flag);
            _sites = Game.UnraisedLandmarks(MoonLandmarkKind.Site);
            if (_flagPos >= _flags.Count) _flagPos = 0;
            if (_sitePos >= _sites.Count) _sitePos = 0;

            float y = 0;
            if (_updateShown) UpdateSection(ref y);
            if (Section(ref y, "PRESETS")) PresetsSection(ref y);
            if (Section(ref y, "PLAYER")) PlayerSection(ref y);
            if (Section(ref y, "MONEY")) MoneySection(ref y);
            if (Section(ref y, "DUST & VEHICLE")) DustSection(ref y);
            if (Section(ref y, "WORLD")) WorldSection(ref y);
            if (Section(ref y, "FLAGS & SITES")) LandmarkSection(ref y);
            if (Section(ref y, "TELEPORT")) TeleportSection(ref y);
            if (Section(ref y, "SAVED LOCATIONS")) LocationsSection(ref y);
            if (Section(ref y, "BUILD")) BuildSection(ref y);
            if (Section(ref y, "CREW")) CrewSection(ref y);
            if (Section(ref y, "CHAT COMMANDS")) ChatSection(ref y);
            if (Section(ref y, "BACKUPS")) BackupsSection(ref y);
            if (Section(ref y, "SETTINGS")) SettingsSection(ref y);
            y += Pad;
            _scrollContent.sizeDelta = new Vector2(W - Pad * 2, y);
            Refresh();
        }

        void UpdateSection(ref float y)
        {
            float inner = W - Pad * 2;
            var box = Rect(_scrollContent, "Update", 0, y, inner, 92);
            box.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.22f, 0.30f, 1f);
            Text(box, "UPDATE AVAILABLE  ·  v" + Updates.Available + "  <size=12><color=#A0C8DA>(you have " + Plugin.Version + ")</color></size>", 12, 6, inner - 24, 24, 16, true, Cream, TextAlignmentOptions.MidlineLeft);
            var notes = Text(box, string.IsNullOrEmpty(Updates.Notes) ? "Close the game, then paste the install command into PowerShell." : Updates.Notes, 12, 30, inner - 24, 20, 12, false, new Color(0.75f, 0.86f, 0.92f), TextAlignmentOptions.MidlineLeft);
            ButtonRaw(box, "Copy update command", 12, 54, inner - 24, 30, () =>
            {
                GUIUtility.systemCopyBuffer = Updates.InstallCommand;
                return "Copied. Close the game, open PowerShell, paste (right-click) and press Enter.";
            }, TextAlignmentOptions.Center);
            y += 92 + Gap * 2;
        }

        void PresetsSection(ref float y)
        {
            ButtonRow(ref y, new[] { "Exploring", "Grinding", "Building", "All off" }, new Func<string>[] {
                () => Cheats.Preset("explore"), () => Cheats.Preset("grind"), () => Cheats.Preset("build"), () => Cheats.Off() });
            Hint(ref y, "Exploring: fly + noclip, night vision, frozen time, flags on map.  Grinding: 5x money, wide brush, big bag.  Building: fly, free upgrades, fast machines.");
        }

        void PlayerSection(ref float y)
        {
            ToggleRow(ref y, "Fly", () => Cheats.Fly, v => Cheats.SetFly(v));
            ToggleRow(ref y, "No clip (while flying)", () => Cheats.Noclip, v => { Cheats.Noclip = v; if (v) Cheats.SetFly(true); });
            StepRow(ref y, "Fly speed", () => Cheats.FlySpeed, v => Cheats.FlySpeed = Mathf.Clamp(v, 2, 500), 5, "0");
            StepRow(ref y, "Move speed x", () => Cheats.SpeedMult, v => Cheats.SpeedMult = Mathf.Clamp(v, 0.25f, 20), 0.5f, "0.0#");
            StepRow(ref y, "Jump height x", () => Cheats.JumpMult, v => Cheats.JumpMult = Mathf.Clamp(v, 0.25f, 20), 0.5f, "0.0#");
            ToggleRow(ref y, "Unlimited jetpack", () => Cheats.InfiniteJetpack, v => Cheats.InfiniteJetpack = v);
            ToggleRow(ref y, "Gravity boots", () => Cheats.GravityBoots, v => Cheats.GravityBoots = v);
            ToggleRow(ref y, "Night vision", () => Cheats.NightVision, v => Cheats.NightVision = v);
            ButtonRow(ref y, new[] { "Refill jetpack + energy", "Max all my upgrades" }, new Func<string>[] {
                () => Cheats.RefillAll(), () => Game.GiveAllUpgrades(Game.PlayerId) });
        }

        void MoneySection(ref float y)
        {
            float inner = W - Pad * 2;
            var money = Text(_scrollContent, "", 0, y, inner, Row, 18, false, Cream, TextAlignmentOptions.MidlineLeft); y += Row;
            _refreshers.Add(() => money.text = "Team wallet  <b>$" + Game.Money(Game.PlayerId).ToString("N0") + "</b>");
            ButtonRow(ref y, new[] { "+1k", "+10k", "+100k", "+1M" }, new Func<string>[] {
                () => AddMoney(1000), () => AddMoney(10000), () => AddMoney(100000), () => AddMoney(1000000) });
            ButtonRow(ref y, new[] { "Set 0", "Set 100k", "Set 10M" }, new Func<string>[] {
                () => SetMoney(0), () => SetMoney(100000), () => SetMoney(10000000) });
            StepRow(ref y, "Earnings x", () => Cheats.PayoutMult, v => Cheats.PayoutMult = Mathf.Clamp(v, 0.25f, 100), 1f, "0.0#");
            ToggleRow(ref y, "Free upgrades & tools", () => Cheats.FreeUpgrades, v => Cheats.FreeUpgrades = v);
            ToggleRow(ref y, "Free station travel", () => Cheats.FreeTravel, v => Cheats.FreeTravel = v);
        }

        void DustSection(ref float y)
        {
            ToggleRow(ref y, "Unlimited bag capacity", () => Cheats.InfiniteBag, v => Cheats.InfiniteBag = v);
            ToggleRow(ref y, "Auto-sell what I collect (experimental)", () => Cheats.AutoSell, v => Cheats.AutoSell = v);
            ButtonRow(ref y, new[] { "Fill my bag", "Empty my bag", "Sell bag now" }, new Func<string>[] {
                () => Cheats.SetCarried(true), () => Cheats.SetCarried(false), () => Cheats.SellCarried(true) });
            StepRow(ref y, "Vehicle speed x", () => Cheats.VehicleSpeedMult, v => Cheats.VehicleSpeedMult = Mathf.Clamp(v, 0.25f, 20), 0.5f, "0.0#");
            StepRow(ref y, "Brush width x", () => Cheats.BrushWidthMult, v => Cheats.BrushWidthMult = Mathf.Clamp(v, 0.25f, 20), 0.5f, "0.0#");
            ToggleRow(ref y, "Unlimited vehicle hopper", () => Cheats.InfiniteHopper, v => Cheats.InfiniteHopper = v);
            ButtonRow(ref y, new[] { "Fill hopper", "Empty hopper" }, new Func<string>[] {
                () => Cheats.SetHopper(true), () => Cheats.SetHopper(false) });
        }

        void WorldSection(ref float y)
        {
            float inner = W - Pad * 2;
            var time = Text(_scrollContent, "", 0, y, inner, Row - 6, 15, false, Dim, TextAlignmentOptions.MidlineLeft); y += Row - 6;
            _refreshers.Add(() => time.text = "Lunar time " + Cheats.TimeText());
            ToggleRow(ref y, "Freeze time of day", () => Cheats.FreezeTime, v => Cheats.FreezeTime = v);
            StepRow(ref y, "Time speed x", () => Cheats.TimeSpeed, v => Cheats.TimeSpeed = Mathf.Clamp(Mathf.Round(v * 4) / 4, 0.25f, 50), Cheats.TimeSpeed >= 5 ? 5f : 0.25f, "0.##");
            ButtonRow(ref y, new[] { "-1 h", "+1 h", "+6 h", "+12 h" }, new Func<string>[] {
                () => Cheats.ShiftTime(-1), () => Cheats.ShiftTime(1), () => Cheats.ShiftTime(6), () => Cheats.ShiftTime(12) });
            StepRow(ref y, "Machine speed x", () => Cheats.MachineSpeed, v => Cheats.MachineSpeed = Mathf.Clamp(v, 0.25f, 50), 1f, "0.0#");
            RingRow(ref y);
            ActionRow(ref y, "Finish the Moon - leave 1 section (marker set)", () => Cheats.FinishMoon(true));
            ConfirmRow(ref y, "finish", "Finish the Moon completely", () => Cheats.FinishMoon(false));
            ConfirmRow(ref y, "resetdust", "Reset all dust (undo cleaning)", () => Cheats.ResetDust(), true);
            ActionRow(ref y, "Save game now", () => Game.SaveNow());
        }

        void LandmarkSection(ref float y)
        {
            ToggleRow(ref y, "Show flags & sites on the map", () => Cheats.FlagsOnMap, v => Cheats.FlagsOnMap = v);
            LandmarkBrowser(ref y, "Flag", _flags, () => _flagPos, v => _flagPos = v, MoonLandmarkKind.Flag);
            LandmarkBrowser(ref y, "Site", _sites, () => _sitePos, v => _sitePos = v, MoonLandmarkKind.Site);
            ButtonRow(ref y, new[] { "Raise all flags", "Find all sites", "Both" }, new Func<string>[] {
                () => Done(Cheats.RaiseLandmarks(MoonLandmarkKind.Flag)), () => Done(Cheats.RaiseLandmarks(MoonLandmarkKind.Site)), () => Done(Cheats.RaiseLandmarks(null)) });
        }

        /// <summary>"◀ name ▶" browser over unraised landmarks, nearest first, with Go and Raise buttons.</summary>
        void LandmarkBrowser(ref float y, string label, List<int> list, Func<int> getPos, Action<int> setPos, MoonLandmarkKind kind)
        {
            float inner = W - Pad * 2, btn = 40, act = 64;
            var world = Game.World;
            if (list.Count == 0)
            {
                Text(_scrollContent, "All " + label.ToLower() + "s raised.", 0, y, inner, Row, 14, false, Dim, TextAlignmentOptions.MidlineLeft);
                y += Row + Gap; return;
            }
            Func<string> name = () =>
            {
                if (list.Count == 0 || world == null) return "-";
                var lm = world.Landmark(list[getPos()]);
                return label + " " + (getPos() + 1) + "/" + list.Count + ":  <b>" + (lm != null ? lm.name : "?") + "</b>";
            };
            ButtonRaw(_scrollContent, "<", 0, y, btn, Row, () => { setPos((getPos() - 1 + list.Count) % list.Count); return name(); }, TextAlignmentOptions.Center);
            var text = Text(_scrollContent, "", btn + Gap, y, inner - btn * 2 - act * 2 - Gap * 4, Row, 14, false, Cream, TextAlignmentOptions.MidlineLeft);
            ButtonRaw(_scrollContent, ">", inner - act * 2 - btn - Gap * 2, y, btn, Row, () => { setPos((getPos() + 1) % list.Count); return name(); }, TextAlignmentOptions.Center);
            ButtonRaw(_scrollContent, "Go", inner - act * 2 - Gap, y, act, Row, () => Cheats.TeleportToLandmark(list[getPos()]), TextAlignmentOptions.Center);
            ButtonRaw(_scrollContent, "Raise", inner - act, y, act, Row, () =>
            {
                var w = Game.World; if (w == null) return "Not in a game.";
                w.MarkLandmarkRaised(list[getPos()], Game.PlayerId); Game.MarkDirty();
                _rebuildPending = true;
                return label + " raised.";
            }, TextAlignmentOptions.Center);
            _refreshers.Add(() => text.text = name());
            y += Row + Gap;
        }

        void TeleportSection(ref float y)
        {
            float inner = W - Pad * 2;
            ButtonRow(ref y, new[] { "Nearest flag", "Nearest site" }, new Func<string>[] {
                () => Cheats.TeleportToNearestLandmark(MoonLandmarkKind.Flag), () => Cheats.TeleportToNearestLandmark(MoonLandmarkKind.Site) });

            SubHeader(ref y, "Map markers");
            var world = Game.World;
            int count = world == null || world.waypoints == null ? 0 : world.waypoints.Count;
            if (count == 0) Hint(ref y, "No markers. Right-click the map to place one.");
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var mark = world.waypoints[i];
                ButtonRaw(_scrollContent, "Marker " + (i + 1) + "   <size=12><color=#A09C96>#" + mark.id + "</color></size>", 0, y, inner, Row,
                    () => Cheats.TeleportToWaypoint(index), TextAlignmentOptions.MidlineLeft);
                y += Row + Gap;
            }

            SubHeader(ref y, "Crewmates");
            var remotes = Game.RemotePlayers();
            if (remotes.Count == 0) Hint(ref y, "No crewmates in range. Players show up here once they're on the Moon with you.");
            foreach (var rp in remotes)
            {
                string n = rp.DisplayName;
                ButtonRaw(_scrollContent, "Go to " + n, 0, y, inner, Row, () => Cheats.TeleportToPlayer(n), TextAlignmentOptions.MidlineLeft);
                y += Row + Gap;
            }
        }

        void BuildSection(ref float y)
        {
            Hint(ref y, "Look at the ground where you want it (within 250 m), then click.");
            ActionRow(ref y, "Place dust catcher", () => Cheats.PlaceCatcher());
            ButtonRow(ref y, new[] { "Robot dock Mk I", "Mk II", "Joonas" }, new Func<string>[] {
                () => Cheats.PlaceRobotDock(0, "Mk I"), () => Cheats.PlaceRobotDock(1, "Mk II"), () => Cheats.PlaceRobotDock(2, "Joonas") });
        }

        void CrewSection(ref float y)
        {
            float inner = W - Pad * 2;
            TMP_Text who = ButtonRaw(_scrollContent, "", 0, y, inner, Row, () => { _memberIndex = (_memberIndex + 1) % Math.Max(1, _members.Count); return "Selected " + CurrentMember().Name; }, TextAlignmentOptions.MidlineLeft);
            _refreshers.Add(() => who.text = "Player:  <b>" + CurrentMember().Name + "</b>   <size=12><color=#A09C96>(click to cycle)</color></size>");
            y += Row + Gap;
            ButtonRow(ref y, new[] { "+10k", "+100k", "+1M" }, new Func<string>[] {
                () => Give(10000), () => Give(100000), () => Give(1000000) });
            ButtonRow(ref y, new[] { "Jetpack", "Boots", "Night vis", "Belt" }, new Func<string>[] {
                () => GiveUp("gear.jetpack"), () => GiveUp("gear.boots"), () => GiveUp("gear.nightvision"), () => GiveUp("gear.belt") });
            ButtonRow(ref y, new[] { "Speed", "Jump", "Width", "Payout" }, new Func<string>[] {
                () => GiveUp("suit.stride"), () => GiveUp("suit.jump"), () => GiveUp("clean.width"), () => GiveUp("eco.payout") });
            ButtonRow(ref y, new[] { "Max every upgrade", "Raise all flags & sites" }, new Func<string>[] {
                () => Game.GiveAllUpgrades(CurrentMember().Id), () => Done(Cheats.RaiseLandmarks(null, CurrentMember().Id)) });
            Hint(ref y, "Money goes to the shared team wallet. Upgrades, flags and sites are per player.");
            SubHeader(ref y, "Who has the trainer");
            foreach (var m in _members)
            {
                var mm = m;
                var t = Text(_scrollContent, "", 0, y, inner - 110 - Gap, Row - 6, 14, false, Cream, TextAlignmentOptions.MidlineLeft);
                _refreshers.Add(() => t.text = VersionLine(mm));
                if (!m.Local)
                    ButtonRaw(_scrollContent, "Send link", inner - 110, y, 110, Row - 6, () =>
                    {
                        Chat.SayInstall(mm.Name);
                        return "Link posted in chat for " + mm.Name + ".";
                    }, TextAlignmentOptions.Center);
                y += Row - 6 + Gap;
            }
        }

        void ChatSection(ref float y)
        {
            ToggleRow(ref y, "Crew may use chat commands", () => Settings.ChatCommands.Value, v => Settings.ChatCommands.Value = v);
            ToggleRow(ref y, "...including money, upgrades, flags", () => Settings.ChatGiveables.Value, v => Settings.ChatGiveables.Value = v);
            ToggleRow(ref y, "Only allowlisted players", () => Settings.ChatAllowlistOnly.Value, v => Settings.ChatAllowlistOnly.Value = v);
            foreach (var m in _members)
            {
                if (m.Local) continue;
                string n = m.Name;
                ToggleRow(ref y, "   Allow " + n, () => Settings.OnAllowlist(n), v => Settings.SetAllowlisted(n, v));
            }
            StepRow(ref y, "Cooldown (seconds)", () => Settings.ChatCooldown.Value, v => Settings.ChatCooldown.Value = Mathf.Clamp(v, 0, 60), 1f, "0");
            StepRow(ref y, "Money limit per player ($k)", () => Settings.ChatSessionMoneyCap.Value / 1000f, v => Settings.ChatSessionMoneyCap.Value = Mathf.Clamp(v, 0, 1000000) * 1000f, 1000f, "N0");
            ToggleRow(ref y, "Welcome message when someone joins", () => Settings.ChatWelcome.Value, v => Settings.ChatWelcome.Value = v);
            ToggleRow(ref y, "Reply in chat", () => Settings.ChatReply.Value, v => Settings.ChatReply.Value = v);
            Hint(ref y, Settings.ChatPrefix.Value + Chat.Help, 52);
        }

        void LocationsSection(ref float y)
        {
            float inner = W - Pad * 2, small = 70;
            ActionRow(ref y, "Save where I'm standing", () => Locations.SaveHere(null));
            var spots = Locations.All();
            if (spots.Count == 0) Hint(ref y, "Nothing saved yet. Tip: type " + Settings.ChatPrefix.Value + "save base in chat to save a spot with your own name, and " + Settings.ChatPrefix.Value + "go base to come back.");
            foreach (var spot in spots)
            {
                string n = spot.Name;
                ButtonRaw(_scrollContent, n, 0, y, inner - small - Gap, Row, () => Locations.Go(n), TextAlignmentOptions.MidlineLeft);
                ButtonRaw(_scrollContent, "Delete", inner - small, y, small, Row, () => { _rebuildPending = true; return Locations.Delete(n); }, TextAlignmentOptions.Center, Danger);
                y += Row + Gap;
            }
        }

        void BackupsSection(ref float y)
        {
            float inner = W - Pad * 2, small = 84;
            ActionRow(ref y, "Make a backup now", () => { _rebuildPendingAt = Time.unscaledTime + 6f; return Game.BackupNow(); });
            Hint(ref y, "The game keeps its own rolling backups too. Restoring backs up your current state first, then reloads the shift.", 30);
            if (_backups.Count == 0) Hint(ref y, "No backups for this save yet.", 22);
            int shown = 0;
            foreach (var b in _backups)
            {
                if (shown++ >= 8) break;
                string manifest = b;
                Text(_scrollContent, Game.BackupLabel(b), 0, y, inner - small - Gap, Row, 15, false, Cream, TextAlignmentOptions.MidlineLeft);
                ConfirmButton(inner - small, y, small, "restore:" + manifest, "Restore", () => Game.RestoreBackup(manifest));
                y += Row + Gap;
            }
        }

        void SettingsSection(ref float y)
        {
            ToggleRow(ref y, "Remember toggles between launches", () => Settings.RememberCheats.Value, v => Settings.RememberCheats.Value = v);
            ToggleRow(ref y, "Show active cheats on screen", () => Settings.ShowHud.Value, v => Settings.ShowHud.Value = v);
            ToggleRow(ref y, "Check for updates at startup", () => Settings.CheckForUpdates.Value, v => Settings.CheckForUpdates.Value = v);
            Hint(ref y, "Hotkeys (change in MODS > Moon Trainer > Settings):  fly " + Settings.FlyKey.Value + "  ·  noclip " + Settings.NoclipKey.Value
                + "  ·  speed " + Settings.SpeedKey.Value + "  ·  jetpack " + Settings.JetpackKey.Value + "  ·  save " + Settings.SaveKey.Value + "  ·  HUD " + Settings.HudKey.Value, 40);
            ConfirmRow(ref y, "achievements", "Unlock all Steam achievements", () => Cheats.UnlockAchievements(), true);
            ActionRow(ref y, "Reset menu position", () =>
            {
                Settings.MenuX.Value = 32; Settings.MenuY.Value = -40;
                _panel.anchoredPosition = new Vector2(32, -40);
                return "Menu moved back.";
            });
        }

        static string VersionLine(Game.Member m)
        {
            string latest = Updates.Latest ?? Plugin.Version;
            if (Updates.IsNewer(Plugin.Version, latest)) latest = Plugin.Version;
            string v = Game.TrainerVersionOf(m);
            string name = m.Name + (m.Local ? " (you)" : "");
            if (v == null) return name + "   <color=#A09C96>not detected - no trainer, or 1.0</color>";
            if (Updates.IsNewer(latest, v)) return name + "   <color=#FFB347>v" + v + " - out of date</color>";
            return name + "   <color=#7FD48A>v" + v + "</color>";
        }

        Game.Member CurrentMember() { return _members.Count == 0 ? new Game.Member { Id = Game.PlayerId, Name = "You", Local = true } : _members[_memberIndex]; }

        string AddMoney(double d) { var b = Game.AddMoney(Game.PlayerId, d); return "+$" + d.ToString("N0") + " (now $" + b.ToString("N0") + ")"; }
        string SetMoney(double d) { var b = Game.SetMoney(Game.PlayerId, d); return "Money set to $" + b.ToString("N0"); }
        string Give(double d) { var m = CurrentMember(); var b = Game.AddMoney(m.Id, d); return "+$" + d.ToString("N0") + " for " + m.Name + " (team now $" + b.ToString("N0") + ")"; }
        string GiveUp(string id) { var m = CurrentMember(); return m.Name + ": " + Game.GiveUpgrade(m.Id, id); }
        string Done(string msg) { _rebuildPending = true; return msg; }

        // ---- refresh -----------------------------------------------------------------------------------

        void Refresh()
        {
            _nextRefresh = Time.unscaledTime + 0.25f;
            foreach (var r in _refreshers) { try { r(); } catch (Exception) { } }
            if (_status != null && Time.unscaledTime >= _statusUntil) _status.text = _notice != null && Time.unscaledTime < _noticeUntil ? _notice : "";

            var world = Game.World;
            if (world != null && (world.WaypointRevision != _waypointRevision || world.LandmarkRevision != _landmarkRevision)) _rebuildPending = true;
            if ((Updates.Available != null) != _updateShown) _rebuildPending = true;
            if (Locations.All().Count != _locationCount) _rebuildPending = true;
        }

        // ---- widgets ---------------------------------------------------------------------------------

        /// <summary>Clickable section header; returns true when the section is expanded.</summary>
        bool Section(ref float y, string title)
        {
            float inner = W - Pad * 2;
            bool collapsed = Settings.IsCollapsed(title);
            y += 6;
            var t = ButtonRaw(_scrollContent, (collapsed ? "+  " : "-  ") + title, 0, y, inner, 30, () =>
            {
                Settings.SetCollapsed(title, !Settings.IsCollapsed(title));
                _rebuildPending = true;
                return null;
            }, TextAlignmentOptions.MidlineLeft, HeaderColor);
            t.color = Accent; t.fontSize = 15;
            y += 30 + Gap;
            return !collapsed;
        }

        void SubHeader(ref float y, string title)
        {
            Text(_scrollContent, title.ToUpper(), 0, y, W - Pad * 2, 22, 12, true, Dim, TextAlignmentOptions.BottomLeft);
            y += 24;
        }

        void Hint(ref float y, string text, float h = 34)
        {
            var t = Text(_scrollContent, text, 0, y, W - Pad * 2, h, 12, false, Dim, TextAlignmentOptions.TopLeft);
            t.textWrappingMode = TextWrappingModes.Normal;
            y += h + 2;
        }

        void ToggleRow(ref float y, string label, Func<bool> get, Action<bool> set)
        {
            float inner = W - Pad * 2;
            TMP_Text text = null;
            text = ButtonRaw(_scrollContent, "", 0, y, inner, Row, () => { set(!get()); return label.Trim() + " " + (get() ? "on" : "off"); }, TextAlignmentOptions.MidlineLeft);
            _refreshers.Add(() => text.text = label + "   " + (get() ? "<color=#FF8C33><b>ON</b></color>" : "<color=#A09C96>OFF</color>"));
            y += Row + Gap;
        }

        void StepRow(ref float y, string label, Func<float> get, Action<float> set, float step, string fmt)
        {
            float inner = W - Pad * 2;
            float btn = 44;
            var text = Text(_scrollContent, "", 0, y, inner - btn * 2 - Gap * 2, Row, 16, false, Cream, TextAlignmentOptions.MidlineLeft);
            ButtonRaw(_scrollContent, "-", inner - btn * 2 - Gap, y, btn, Row, () => { set(get() - step); return label + " " + get().ToString(fmt); }, TextAlignmentOptions.Center);
            ButtonRaw(_scrollContent, "+", inner - btn, y, btn, Row, () => { set(get() + step); return label + " " + get().ToString(fmt); }, TextAlignmentOptions.Center);
            _refreshers.Add(() => text.text = label + "   <b>" + get().ToString(fmt) + "</b>");
            y += Row + Gap;
        }

        /// <summary>Rings to clean around the player: -10 / -1 / value / +1 / +10, then one button that does it.</summary>
        void RingRow(ref float y)
        {
            float inner = W - Pad * 2;
            float btn = 44;
            Func<string> show = () => "Clean radius   <b>" + Cheats.CleanRings + "</b> rings";
            Action<int> bump = d => Cheats.CleanRings = Mathf.Clamp(Cheats.CleanRings + d, 1, 60);
            ButtonRaw(_scrollContent, "-10", 0, y, btn, Row, () => { bump(-10); return show(); }, TextAlignmentOptions.Center);
            ButtonRaw(_scrollContent, "-", btn + Gap, y, btn, Row, () => { bump(-1); return show(); }, TextAlignmentOptions.Center);
            var text = Text(_scrollContent, "", (btn + Gap) * 2, y, inner - (btn + Gap) * 4, Row, 16, false, Cream, TextAlignmentOptions.Center);
            ButtonRaw(_scrollContent, "+", inner - btn * 2 - Gap, y, btn, Row, () => { bump(1); return show(); }, TextAlignmentOptions.Center);
            ButtonRaw(_scrollContent, "+10", inner - btn, y, btn, Row, () => { bump(10); return show(); }, TextAlignmentOptions.Center);
            _refreshers.Add(() => text.text = show());
            y += Row + Gap;
            TMP_Text go = ButtonRaw(_scrollContent, "", 0, y, inner, Row, () => Cheats.CleanAround(Cheats.CleanRings), TextAlignmentOptions.Center);
            _refreshers.Add(() => go.text = "Clean " + Cheats.CleanRings + " ring" + (Cheats.CleanRings == 1 ? "" : "s") + " around me");
            y += Row + Gap;
        }

        void ActionRow(ref float y, string label, Func<string> action)
        {
            ButtonRaw(_scrollContent, label, 0, y, W - Pad * 2, Row, action, TextAlignmentOptions.Center);
            y += Row + Gap;
        }

        /// <summary>A button that needs a second click within 4 seconds before it does anything.</summary>
        void ConfirmRow(ref float y, string key, string label, Func<string> action, bool danger = false)
        {
            TMP_Text text = null;
            text = ButtonRaw(_scrollContent, label, 0, y, W - Pad * 2, Row, () =>
            {
                if (_confirmKey == key && Time.unscaledTime < _confirmUntil)
                {
                    _confirmKey = null;
                    return action();
                }
                _confirmKey = key; _confirmUntil = Time.unscaledTime + 4f;
                return "Click \"" + label + "\" again to confirm.";
            }, TextAlignmentOptions.Center, danger ? Danger : (Color?)null);
            _refreshers.Add(() => text.text = (_confirmKey == key && Time.unscaledTime < _confirmUntil) ? "<color=#FFD27F>Click again to confirm</color>" : label);
            y += Row + Gap;
        }

        void ConfirmButton(float x, float y, float w, string key, string label, Func<string> action)
        {
            TMP_Text text = null;
            text = ButtonRaw(_scrollContent, label, x, y, w, Row, () =>
            {
                if (_confirmKey == key && Time.unscaledTime < _confirmUntil) { _confirmKey = null; return action(); }
                _confirmKey = key; _confirmUntil = Time.unscaledTime + 4f;
                return "Click " + label + " again to confirm.";
            }, TextAlignmentOptions.Center, Danger);
            _refreshers.Add(() => text.text = (_confirmKey == key && Time.unscaledTime < _confirmUntil) ? "<color=#FFD27F>Sure?</color>" : label);
        }

        void ButtonRow(ref float y, string[] labels, Func<string>[] actions)
        {
            float inner = W - Pad * 2;
            float w = (inner - Gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
                ButtonRaw(_scrollContent, labels[i], i * (w + Gap), y, w, Row, actions[i], TextAlignmentOptions.Center);
            y += Row + Gap;
        }

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

        TMP_Text AddText(GameObject go, float size, bool bold, Color color, TextAlignmentOptions align)
        {
            var t = go.AddComponent<TextMeshProUGUI>();
            var font = bold ? _fontBold : _fontRegular;
            if (font != null) t.font = font;
            t.fontSize = size; t.color = color; t.alignment = align; t.richText = true; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        TMP_Text Text(Transform parent, string value, float x, float y, float w, float h, float size, bool bold, Color color, TextAlignmentOptions align)
        {
            var rt = Rect(parent, "Text", x, y, w, h);
            var t = AddText(rt.gameObject, size, bold, color, align);
            t.text = value;
            return t;
        }

        TMP_Text ButtonRaw(Transform parent, string label, float x, float y, float w, float h, Func<string> action, TextAlignmentOptions align, Color? baseColor = null)
        {
            var rt = Rect(parent, "Button", x, y, w, h);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = Color.white;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            var normal = baseColor ?? BtnColor;
            colors.normalColor = normal; colors.highlightedColor = Color.Lerp(normal, Color.white, 0.12f); colors.selectedColor = normal; colors.pressedColor = Accent;
            colors.colorMultiplier = 1; colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() => Run(action))));
            return Text(rt, label, 12, 0, w - 24, h, 15, true, Cream, align);
        }

        void Run(Func<string> action)
        {
            try { var msg = action(); if (msg != null) Show(msg); }
            catch (Exception e) { Show("Failed: " + e.Message); Plugin.Logger.LogError(e); }
            _nextRefresh = 0;
        }

        void Show(string message)
        {
            if (_status != null) _status.text = message;
            _statusUntil = Time.unscaledTime + 4f;
            Plugin.Logger.LogInfo(message);
        }

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
