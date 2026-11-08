using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using MoonCleaners;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MoonTrainer
{
    /// <summary>The in-game menu (Insert by default) plus the per-frame driver for everything else.</summary>
    public class Trainer : MonoBehaviour
    {
        public Trainer(IntPtr ptr) : base(ptr) { }

        // ---- layout ----
        const float W = 440, Pad = 16, Row = 36, Gap = 6;
        static readonly Color PanelColor = new Color(0.07f, 0.07f, 0.09f, 0.94f);
        static readonly Color Accent = new Color(1f, 0.55f, 0.2f, 1f);
        static readonly Color BtnColor = new Color(0.18f, 0.18f, 0.21f, 1f);
        static readonly Color BtnHover = new Color(0.30f, 0.30f, 0.34f, 1f);
        static readonly Color Cream = new Color(0.95f, 0.93f, 0.89f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.60f, 0.58f, 1f);

        bool _open;
        GameObject _canvas;
        RectTransform _panel, _scrollContent;
        TMP_FontAsset _fontBold, _fontRegular;
        TMP_Text _status, _moneyText, _timeText, _memberText;
        readonly List<Action> _refreshers = new List<Action>();
        float _nextRefresh, _statusUntil;
        int _memberIndex;
        List<Game.Member> _members = new List<Game.Member>();
        int _waypointRevision = -1;
        RectTransform _waypointList;
        bool _uiBlocked;

        static string _notice; static float _noticeUntil;
        public static void Notice(string text) { _notice = text; _noticeUntil = Time.unscaledTime + 5f; Plugin.Logger.LogInfo(text); }

        bool _chimed; float _chimeAt;

        void Start()
        {
            // Chime a couple of seconds after the first scene is up so the player hears that the mod attached.
            _chimeAt = Time.unscaledTime + 2f;
            Notice("Moon Trainer loaded - " + Settings.MenuKey.Value + " opens the menu");
        }

        /// <summary>Two rising tones generated on the fly (no audio files needed).</summary>
        void Chime()
        {
            try
            {
                int rate = 44100; float dur = 0.9f; int n = (int)(rate * dur);
                var data = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float>(n);
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / rate;
                    float freq = t < 0.4f ? 660f : 990f;
                    float env = t < 0.4f ? Mathf.Min(1f, t * 40f) * (1f - t / 0.4f) : Mathf.Min(1f, (t - 0.4f) * 40f) * (1f - (t - 0.4f) / 0.5f);
                    data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.35f * Mathf.Max(0f, env);
                }
                var clip = AudioClip.Create("MoonTrainerChime", n, 1, rate, false);
                clip.SetData(data, 0);
                var src = gameObject.AddComponent<AudioSource>();
                src.spatialBlend = 0f; src.volume = 0.8f; src.ignoreListenerPause = true;
                src.PlayOneShot(clip);
            }
            catch (Exception e) { Plugin.Logger.LogWarning("Chime failed: " + e.Message); }
        }

        void Update()
        {
            try
            {
                if (!_chimed && Time.unscaledTime >= _chimeAt) { _chimed = true; Chime(); }
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (Pressed(kb, Settings.MenuKey.Value)) Toggle();
                    if (!_open && Game.InGame && Pressed(kb, Settings.FlyKey.Value)) { Cheats.SetFly(!Cheats.Fly); Notice("Fly " + (Cheats.Fly ? "on" : "off")); }
                }

                Cheats.Tick();
                Chat.Tick();

                if (_open && !Game.InGame) Close();
                if (_open)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    var walker = Game.Walker;
                    if (walker != null && !_uiBlocked) { walker.UIBlocked = true; _uiBlocked = true; }
                    if (Time.unscaledTime >= _nextRefresh) Refresh();
                }
            }
            catch (Exception e) { Plugin.Logger.LogError(e); }
        }

        void OnGUI()
        {
            // Tiny notice in the corner (plain label; this game strips the IMGUI widgets but labels work).
            if (_notice != null && Time.unscaledTime < _noticeUntil)
                GUI.Label(new Rect(16, Screen.height - 40, 900, 30), "<b>" + _notice + "</b>");
        }

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
            Refresh();
        }

        void Close()
        {
            _open = false;
            if (_canvas != null && !_canvas.WasCollected && _canvas) _canvas.SetActive(false);
            var walker = Game.Walker;
            if (walker != null && _uiBlocked) walker.UIBlocked = false;
            _uiBlocked = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // ---- building ----------------------------------------------------------------------------------

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
            var canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            var scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;
            _canvas.AddComponent<GraphicRaycaster>();

            float height = 1000;
            _panel = Rect(_canvas.transform, "Panel", 0, 0, W, height);
            _panel.anchorMin = _panel.anchorMax = new Vector2(0, 1);
            _panel.pivot = new Vector2(0, 1);
            _panel.anchoredPosition = new Vector2(32, -40);
            _panel.gameObject.AddComponent<Image>().color = PanelColor;
            Rect(_panel, "Accent", 0, 0, 5, height).gameObject.AddComponent<Image>().color = Accent;

            float inner = W - Pad * 2;
            Text(_panel, "MOON TRAINER", Pad, Pad, inner, 36, 28, true, Cream, TextAlignmentOptions.MidlineLeft);
            Text(_panel, Settings.MenuKey.Value + " closes", Pad, Pad, inner, 36, 14, false, Dim, TextAlignmentOptions.MidlineRight);
            _status = Text(_panel, "", Pad, Pad + 38, inner, 24, 14, false, Accent, TextAlignmentOptions.MidlineLeft);

            // Scrollable body
            float bodyTop = Pad + 66;
            var view = Rect(_panel, "Body", Pad, bodyTop, inner, height - bodyTop - Pad);
            view.gameObject.AddComponent<RectMask2D>();
            _scrollContent = Rect(view, "Content", 0, 0, inner, 10);
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = _scrollContent; scroll.viewport = view; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40; scroll.inertia = false;

            float y = 0;
            Section(ref y, "PLAYER");
            ToggleRow(ref y, "Fly", () => Cheats.Fly, v => Cheats.SetFly(v));
            ToggleRow(ref y, "No clip (while flying)", () => Cheats.Noclip, v => { Cheats.Noclip = v; if (v) Cheats.SetFly(true); });
            StepRow(ref y, "Fly speed", () => Cheats.FlySpeed, v => Cheats.FlySpeed = Mathf.Clamp(v, 2, 500), 5, "0");
            StepRow(ref y, "Move speed x", () => Cheats.SpeedMult, v => Cheats.SpeedMult = Mathf.Clamp(v, 0.25f, 20), 0.5f, "0.0#");
            StepRow(ref y, "Jump height x", () => Cheats.JumpMult, v => Cheats.JumpMult = Mathf.Clamp(v, 0.25f, 20), 0.5f, "0.0#");
            ToggleRow(ref y, "Unlimited jetpack", () => Cheats.InfiniteJetpack, v => Cheats.InfiniteJetpack = v);
            ToggleRow(ref y, "Gravity boots", () => Cheats.GravityBoots, v => Cheats.GravityBoots = v);
            ToggleRow(ref y, "Night vision", () => Cheats.NightVision, v => Cheats.NightVision = v);
            ActionRow(ref y, "Refill jetpack + energy", () => Cheats.RefillAll());
            ActionRow(ref y, "Max all my upgrades", () => Game.GiveAllUpgrades(Game.PlayerId));

            Section(ref y, "MONEY");
            _moneyText = Text(_scrollContent, "", 0, y, inner, Row, 18, false, Cream, TextAlignmentOptions.MidlineLeft); y += Row;
            ButtonRow(ref y, new[] { "+1k", "+10k", "+100k", "+1M" }, new Func<string>[] {
                () => Money(1000), () => Money(10000), () => Money(100000), () => Money(1000000) });
            ButtonRow(ref y, new[] { "Set 0", "Set 100k", "Set 10M" }, new Func<string>[] {
                () => SetMoney(0), () => SetMoney(100000), () => SetMoney(10000000) });
            StepRow(ref y, "Earnings x", () => Cheats.PayoutMult, v => Cheats.PayoutMult = Mathf.Clamp(v, 0.25f, 100), 1f, "0.0#");
            ToggleRow(ref y, "Free upgrades & tools", () => Cheats.FreeUpgrades, v => Cheats.FreeUpgrades = v);
            ToggleRow(ref y, "Free station travel", () => Cheats.FreeTravel, v => Cheats.FreeTravel = v);

            Section(ref y, "VEHICLE");
            StepRow(ref y, "Vehicle speed x", () => Cheats.VehicleSpeedMult, v => Cheats.VehicleSpeedMult = Mathf.Clamp(v, 0.25f, 20), 0.5f, "0.0#");
            StepRow(ref y, "Brush width x", () => Cheats.BrushWidthMult, v => Cheats.BrushWidthMult = Mathf.Clamp(v, 0.25f, 20), 0.5f, "0.0#");

            Section(ref y, "WORLD");
            _timeText = Text(_scrollContent, "", 0, y, inner, Row, 16, false, Dim, TextAlignmentOptions.MidlineLeft); y += Row - 8;
            ToggleRow(ref y, "Freeze time of day", () => Cheats.FreezeTime, v => Cheats.FreezeTime = v);
            ButtonRow(ref y, new[] { "-1 h", "+1 h", "+6 h" }, new Func<string>[] { () => Cheats.ShiftTime(-1), () => Cheats.ShiftTime(1), () => Cheats.ShiftTime(6) });
            StepRow(ref y, "Machine speed x", () => Cheats.MachineSpeed, v => Cheats.MachineSpeed = Mathf.Clamp(v, 0.25f, 50), 1f, "0.0#");
            RingRow(ref y);
            ActionRow(ref y, "Finish the Moon - leave 1 section (marker set)", () => Cheats.FinishMoon(true));
            ActionRow(ref y, "Finish the Moon completely", () => Cheats.FinishMoon(false));
            ButtonRow(ref y, new[] { "Raise all flags", "Find all sites", "Both" }, new Func<string>[] {
                () => Cheats.RaiseLandmarks(MoonLandmarkKind.Flag), () => Cheats.RaiseLandmarks(MoonLandmarkKind.Site), () => Cheats.RaiseLandmarks(null) });
            ActionRow(ref y, "Save game now", () => Game.SaveNow());

            Section(ref y, "TELEPORT TO MARKER");
            _waypointList = Rect(_scrollContent, "Waypoints", 0, y, inner, 0);
            _waypointRevision = -1;   // filled by Refresh
            y += 0; _waypointListY = y;

            // the rest is laid out after the waypoint list, so it's rebuilt in Refresh when markers change
            _afterWaypoints = new List<RectTransform>();
            _afterY = y;
            BuildCrewSection();

            Plugin.Logger.LogInfo("Trainer menu built.");
        }

        float _waypointListY, _afterY;
        List<RectTransform> _afterWaypoints;

        void BuildCrewSection()
        {
            float y = _afterY;
            float inner = W - Pad * 2;
            foreach (var r in _afterWaypoints) if (r != null && !r.WasCollected && r) Destroy(r.gameObject);
            _afterWaypoints.Clear();
            int before = _scrollContent.childCount;

            Section(ref y, "GIVE TO CREWMATE");
            _members = Game.Members();
            if (_memberIndex >= _members.Count) _memberIndex = 0;
            _memberText = ButtonRaw(_scrollContent, "", 0, y, inner, Row, () => { _memberIndex = (_memberIndex + 1) % Math.Max(1, _members.Count); return "Selected " + CurrentMember().Name; }, TextAlignmentOptions.MidlineLeft);
            y += Row + Gap;
            ButtonRow(ref y, new[] { "+10k", "+100k", "+1M" }, new Func<string>[] {
                () => Give(10000), () => Give(100000), () => Give(1000000) });
            ButtonRow(ref y, new[] { "Jetpack", "Boots", "Night vis", "Belt" }, new Func<string>[] {
                () => GiveUp("gear.jetpack"), () => GiveUp("gear.boots"), () => GiveUp("gear.nightvision"), () => GiveUp("gear.belt") });
            ButtonRow(ref y, new[] { "Speed", "Jump", "Width", "Payout" }, new Func<string>[] {
                () => GiveUp("suit.stride"), () => GiveUp("suit.jump"), () => GiveUp("clean.width"), () => GiveUp("eco.payout") });
            ActionRow(ref y, "Max every upgrade", () => Game.GiveAllUpgrades(CurrentMember().Id));

            Section(ref y, "CHAT COMMANDS");
            ToggleRow(ref y, "Crew may use chat commands", () => Settings.ChatCommands.Value, v => Settings.ChatCommands.Value = v);
            ToggleRow(ref y, "...including money & upgrades", () => Settings.ChatGiveables.Value, v => Settings.ChatGiveables.Value = v);
            ToggleRow(ref y, "Reply in chat", () => Settings.ChatReply.Value, v => Settings.ChatReply.Value = v);
            Text(_scrollContent, Settings.ChatPrefix.Value + Chat.Help, 0, y, inner, 60, 12, false, Dim, TextAlignmentOptions.TopLeft).textWrappingMode = TextWrappingModes.Normal; y += 64;

            ActionRow(ref y, "Turn everything off", () => Cheats.Reset());
            y += Pad;

            for (int i = before; i < _scrollContent.childCount; i++) _afterWaypoints.Add(_scrollContent.GetChild(i).GetComponent<RectTransform>());
            _scrollContent.sizeDelta = new Vector2(inner, y);
        }

        Game.Member CurrentMember() { return _members.Count == 0 ? new Game.Member { Id = Game.PlayerId, Name = "You", Local = true } : _members[_memberIndex]; }

        string Money(double d) { var b = Game.AddMoney(Game.PlayerId, d); return "+$" + d.ToString("N0") + " (now $" + b.ToString("N0") + ")"; }
        string SetMoney(double d) { var b = Game.SetMoney(Game.PlayerId, d); return "Money set to $" + b.ToString("N0"); }
        string Give(double d) { var m = CurrentMember(); var b = Game.AddMoney(m.Id, d); return "+$" + d.ToString("N0") + " to " + m.Name + " (now $" + b.ToString("N0") + ")"; }
        string GiveUp(string id) { var m = CurrentMember(); return m.Name + ": " + Game.GiveUpgrade(m.Id, id); }

        // ---- refresh -----------------------------------------------------------------------------------

        void Refresh()
        {
            _nextRefresh = Time.unscaledTime + 0.25f;
            foreach (var r in _refreshers) r();
            if (_moneyText != null) _moneyText.text = "Wallet  <b>$" + Game.Money(Game.PlayerId).ToString("N0") + "</b>";
            if (_timeText != null) _timeText.text = "Lunar time " + Cheats.TimeText();
            if (_status != null && Time.unscaledTime >= _statusUntil) _status.text = _notice != null && Time.unscaledTime < _noticeUntil ? _notice : "";
            if (_memberText != null) { var m = CurrentMember(); _memberText.text = "Player:  <b>" + m.Name + "</b>   <size=12><color=#A09C96>(click to cycle)</color></size>"; }

            var world = Game.World;
            int rev = world == null ? -1 : world.WaypointRevision;
            if (rev != _waypointRevision) { _waypointRevision = rev; FillWaypoints(world); }
        }

        void FillWaypoints(MoonWorld world)
        {
            for (int i = _waypointList.childCount - 1; i >= 0; i--) Destroy(_waypointList.GetChild(i).gameObject);
            float inner = W - Pad * 2;
            int count = world == null || world.waypoints == null ? 0 : world.waypoints.Count;
            float y = 0;
            if (count == 0) { Text(_waypointList, "No markers. Right-click the map to place one.", 0, y, inner, Row, 14, false, Dim, TextAlignmentOptions.MidlineLeft); y += Row; }
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var mark = world.waypoints[i];
                ButtonRaw(_waypointList, "Marker " + (i + 1) + "   <size=12><color=#A09C96>#" + mark.id + "</color></size>", 0, y, inner, Row,
                    () => Cheats.TeleportToWaypoint(index), TextAlignmentOptions.MidlineLeft);
                y += Row + Gap;
            }
            _waypointList.sizeDelta = new Vector2(inner, y);
            _afterY = _waypointListY + y + Gap;
            BuildCrewSection();
        }

        // ---- widgets ---------------------------------------------------------------------------------

        /// <summary>Rings to clean around the player: -10 / -1 / value / +1 / +10, then one button that does it.</summary>
        void RingRow(ref float y)
        {
            float inner = W - Pad * 2;
            float btn = 44;
            TMP_Text text = null;
            Func<string> show = () => "Clean radius   <b>" + Cheats.CleanRings + "</b> rings";
            Action<int> bump = d => Cheats.CleanRings = Mathf.Clamp(Cheats.CleanRings + d, 1, 60);
            ButtonRaw(_scrollContent, "-10", 0, y, btn, Row, () => { bump(-10); return show(); }, TextAlignmentOptions.Center);
            ButtonRaw(_scrollContent, "-", btn + Gap, y, btn, Row, () => { bump(-1); return show(); }, TextAlignmentOptions.Center);
            text = Text(_scrollContent, "", (btn + Gap) * 2, y, inner - (btn + Gap) * 4, Row, 16, false, Cream, TextAlignmentOptions.Center);
            ButtonRaw(_scrollContent, "+", inner - btn * 2 - Gap, y, btn, Row, () => { bump(1); return show(); }, TextAlignmentOptions.Center);
            ButtonRaw(_scrollContent, "+10", inner - btn, y, btn, Row, () => { bump(10); return show(); }, TextAlignmentOptions.Center);
            _refreshers.Add(() => text.text = show());
            y += Row + Gap;
            TMP_Text go = ButtonRaw(_scrollContent, "", 0, y, inner, Row, () => Cheats.CleanAround(Cheats.CleanRings), TextAlignmentOptions.Center);
            _refreshers.Add(() => go.text = "Clean " + Cheats.CleanRings + " ring" + (Cheats.CleanRings == 1 ? "" : "s") + " around me");
            y += Row + Gap;
        }

        void Section(ref float y, string title)
        {
            y += 10;
            var t = Text(_scrollContent, title, 0, y, W - Pad * 2, 26, 15, true, Accent, TextAlignmentOptions.MidlineLeft);
            y += 28;
        }

        void ToggleRow(ref float y, string label, Func<bool> get, Action<bool> set)
        {
            float inner = W - Pad * 2;
            TMP_Text text = null;
            text = ButtonRaw(_scrollContent, "", 0, y, inner, Row, () => { set(!get()); return label + " " + (get() ? "on" : "off"); }, TextAlignmentOptions.MidlineLeft);
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

        void ActionRow(ref float y, string label, Func<string> action)
        {
            ButtonRaw(_scrollContent, label, 0, y, W - Pad * 2, Row, action, TextAlignmentOptions.Center);
            y += Row + Gap;
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

        TMP_Text Text(Transform parent, string value, float x, float y, float w, float h, float size, bool bold, Color color, TextAlignmentOptions align)
        {
            var rt = Rect(parent, "Text", x, y, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var font = bold ? _fontBold : _fontRegular;
            if (font != null) t.font = font;
            t.fontSize = size; t.color = color; t.alignment = align; t.richText = true; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            t.text = value;
            return t;
        }

        TMP_Text ButtonRaw(Transform parent, string label, float x, float y, float w, float h, Func<string> action, TextAlignmentOptions align)
        {
            var rt = Rect(parent, "Button", x, y, w, h);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = Color.white;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = BtnColor; colors.highlightedColor = BtnHover; colors.selectedColor = BtnColor; colors.pressedColor = Accent;
            colors.colorMultiplier = 1; colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() => Run(action))));
            return Text(rt, label, 12, 0, w - 24, h, 16, true, Cream, align);
        }

        void Run(Func<string> action)
        {
            try { Show(action()); }
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
