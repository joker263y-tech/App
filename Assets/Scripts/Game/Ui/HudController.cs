using System;
using System.Collections.Generic;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Simulation;
using Shadowbound.Game.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Shadowbound.Game.Ui
{
    /// <summary>
    /// The whole heads-up display, built entirely in code.
    ///
    /// Every element is created here rather than authored in a prefab: there is no
    /// .prefab or .unity binary to fall out of step with the code, nothing to lose
    /// in a merge, and the layout is expressed as numbers that can be reviewed.
    ///
    /// The on-screen controls are the reason this exists at all. Android has no
    /// keyboard, so the twin-stick layout - a movement stick on the left, a look
    /// area on the right, ability buttons along the bottom - is the difference
    /// between a build that launches and a game that can be played.
    ///
    /// Input handling here is deliberately manual. uGUI's event system needs an
    /// EventSystem object, an input module and raycast targets, all of which are
    /// silent failure points on a device build. Polling the pointers directly and
    /// hit-testing against known rectangles has no such dependencies.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudController : MonoBehaviour
    {
        private enum PointerRole
        {
            None = 0,
            Move = 1,
            Look = 2,
            Ability = 3
        }

        private struct PointerState
        {
            public PointerRole Role;
            public int AbilityIndex;
            public Vector2 Origin;
            public Vector2 Current;
        }

        // Reference resolution the layout is designed against. The scaler keeps the
        // proportions on any screen, so these are the only numbers that need tuning.
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;

        private const float StickRadius = 130f;
        private const float AbilityButtonSize = 168f;
        private const int AbilityButtonCount = 5;

        private PlayerInputDriver _driver;
        private GameSession _session;
        private Participant _playerParticipant;

        /// <summary>The curve is a pure stateless helper, kept locally for the XP bar.</summary>
        private readonly ExperienceCurve _curve = new ExperienceCurve();

        private RectTransform _canvasRect;
        private RectTransform _stickBackground;
        private RectTransform _stickHandle;
        private RectTransform _menuButton;

        /// <summary>
        /// Raised when the on-screen menu button is tapped.
        ///
        /// The HUD does not know the menu exists; the bootstrap connects the two. That
        /// keeps the two screens independent, and means a build without a menu simply
        /// never raises this.
        /// </summary>
        public event Action MenuRequested;

        private readonly List<RectTransform> _abilityButtons = new List<RectTransform>(AbilityButtonCount);
        private readonly List<Image> _abilityCooldownFills = new List<Image>(AbilityButtonCount);
        private readonly List<Text> _abilityLabels = new List<Text>(AbilityButtonCount);

        private Image _healthFill;
        private Image _staminaFill;
        private Image _experienceFill;
        private Text _statusLabel;
        private Text _objectiveLabel;
        private Text _messageLabel;

        private readonly Dictionary<int, PointerState> _pointers = new Dictionary<int, PointerState>(4);

        private float _messageTimer;

        /// <summary>Mouse is pointer id -1, so it cannot collide with a touch id.</summary>
        private const int MousePointerId = -1;

        public static HudController Create(Transform parent, PlayerInputDriver driver, GameSession session)
        {
            var host = new GameObject("HUD");
            host.transform.SetParent(parent, false);

            HudController hud = host.AddComponent<HudController>();
            hud.Initialise(driver, session);
            return hud;
        }

        private void Initialise(PlayerInputDriver driver, GameSession session)
        {
            _driver = driver;
            _session = session;

            BuildCanvas();
            BuildBars();
            BuildStick();
            BuildAbilityButtons();
            BuildMenuButton();
            BuildLabels();

            WireSessionEvents();
        }

        private void OnDestroy()
        {
            UnwireSessionEvents();
        }

        private void WireSessionEvents()
        {
            if (_session == null)
            {
                return;
            }

            _session.LevelledUp += OnLevelledUp;
            _session.LootGranted += OnLootGranted;
            _session.Encounter.Died += OnSomethingDied;
        }

        private void UnwireSessionEvents()
        {
            if (_session == null)
            {
                return;
            }

            _session.LevelledUp -= OnLevelledUp;
            _session.LootGranted -= OnLootGranted;
            _session.Encounter.Died -= OnSomethingDied;
        }

        /// <summary>Releases event subscriptions. Called by the bootstrap on teardown.</summary>
        public void Dispose()
        {
            UnwireSessionEvents();
            _pointers.Clear();
        }

        // ------------------------------- construction -----------------------------

        private void BuildCanvas()
        {
            var canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);

            // Weighted toward height: a 3D action game cares more about vertical
            // framing than about matching width on an unusual aspect ratio.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _canvasRect = canvasObject.GetComponent<RectTransform>();

            // Pinned explicitly: the stick is positioned from a screen-to-local
            // conversion, which is expressed relative to this rect's pivot.
            _canvasRect.pivot = new Vector2(0.5f, 0.5f);
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);

            Image image = child.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            return image;
        }

        private void BuildBars()
        {
            // Health, top left.
            Image healthBack = CreateImage(transform, "Health Back", new Color(0f, 0f, 0f, 0.55f));
            Anchor(healthBack.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(560f, 30f), new Vector2(0f, 1f));

            _healthFill = CreateImage(healthBack.transform, "Health Fill", new Color(0.72f, 0.16f, 0.18f, 0.95f));
            StretchToParent(_healthFill.rectTransform);
            _healthFill.type = Image.Type.Filled;
            _healthFill.fillMethod = Image.FillMethod.Horizontal;
            _healthFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _healthFill.fillAmount = 1f;

            // Stamina, just below it.
            Image staminaBack = CreateImage(transform, "Stamina Back", new Color(0f, 0f, 0f, 0.55f));
            Anchor(staminaBack.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -66f), new Vector2(440f, 18f), new Vector2(0f, 1f));

            _staminaFill = CreateImage(staminaBack.transform, "Stamina Fill", new Color(0.42f, 0.62f, 0.36f, 0.95f));
            StretchToParent(_staminaFill.rectTransform);
            _staminaFill.type = Image.Type.Filled;
            _staminaFill.fillMethod = Image.FillMethod.Horizontal;
            _staminaFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _staminaFill.fillAmount = 1f;

            // Experience, a thin strip across the top. Stretched between the left
            // and right edges, so it tracks the screen width on every device.
            Image experienceBack = CreateImage(transform, "Experience Back", new Color(0f, 0f, 0f, 0.5f));
            RectTransform experienceRect = experienceBack.rectTransform;
            experienceRect.anchorMin = new Vector2(0f, 1f);
            experienceRect.anchorMax = new Vector2(1f, 1f);
            experienceRect.pivot = new Vector2(0.5f, 1f);
            experienceRect.anchoredPosition = Vector2.zero;
            experienceRect.sizeDelta = new Vector2(0f, 8f);

            _experienceFill = CreateImage(experienceBack.transform, "Experience Fill", new Color(0.78f, 0.64f, 0.32f, 0.95f));
            StretchToParent(_experienceFill.rectTransform);
            _experienceFill.type = Image.Type.Filled;
            _experienceFill.fillMethod = Image.FillMethod.Horizontal;
            _experienceFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _experienceFill.fillAmount = 0f;
        }

        private void BuildStick()
        {
            Image background = CreateImage(transform, "Move Stick", new Color(1f, 1f, 1f, 0.09f));
            _stickBackground = background.rectTransform;

            Anchor(
                _stickBackground,
                new Vector2(0f, 0f),
                new Vector2(200f, 200f),
                new Vector2(StickRadius * 2f, StickRadius * 2f),
                new Vector2(0.5f, 0.5f));

            _stickBackground.anchoredPosition = new Vector2(200f, 200f);

            Image handle = CreateImage(_stickBackground, "Move Handle", new Color(1f, 1f, 1f, 0.28f));
            _stickHandle = handle.rectTransform;
            _stickHandle.anchorMin = new Vector2(0.5f, 0.5f);
            _stickHandle.anchorMax = new Vector2(0.5f, 0.5f);
            _stickHandle.pivot = new Vector2(0.5f, 0.5f);
            _stickHandle.sizeDelta = new Vector2(96f, 96f);
            _stickHandle.anchoredPosition = Vector2.zero;
        }

        private void BuildAbilityButtons()
        {
            // Laid out right to left along the bottom edge, so the primary attack
            // sits under the thumb that is already resting on the screen.
            for (int i = 0; i < AbilityButtonCount; i++)
            {
                int index = i;

                Image button = CreateImage(
                    transform,
                    "Ability " + i,
                    new Color(1f, 1f, 1f, 0.12f));

                RectTransform rect = button.rectTransform;
                float size = AbilityButtonSize * (i == 0 ? 1.2f : 1f);

                Anchor(
                    rect,
                    new Vector2(1f, 0f),
                    Vector2.zero,
                    new Vector2(size, size),
                    new Vector2(0.5f, 0.5f));

                rect.anchoredPosition = new Vector2(-(150f + (i * 190f)), 170f);

                Image cooldown = CreateImage(rect, "Cooldown", new Color(0f, 0f, 0f, 0.62f));
                StretchToParent(cooldown.rectTransform);
                cooldown.type = Image.Type.Filled;
                cooldown.fillMethod = Image.FillMethod.Radial360;
                cooldown.fillOrigin = (int)Image.Origin360.Top;
                cooldown.fillClockwise = true;
                cooldown.fillAmount = 0f;

                Text label = CreateLabel(rect, "Label", 34, TextAnchor.MiddleCenter);
                label.text = (i + 1).ToString();

                _abilityButtons.Add(rect);
                _abilityCooldownFills.Add(cooldown);
                _abilityLabels.Add(label);

                // The index is stored alongside the rects so a press maps to the
                // right ability without relying on list ordering elsewhere.
                _buttonIndexes.Add(index);
            }
        }

        private readonly List<int> _buttonIndexes = new List<int>(AbilityButtonCount);

        /// <summary>
        /// The menu button, top right.
        ///
        /// This is not decoration. The menu is otherwise opened with Escape or Tab,
        /// and an Android phone has neither - so without a touch target the save and
        /// equipment screens would be unreachable on the only platform this targets.
        /// </summary>
        private void BuildMenuButton()
        {
            Image button = CreateImage(transform, "Menu Button", new Color(1f, 1f, 1f, 0.14f));

            _menuButton = button.rectTransform;

            Anchor(
                _menuButton,
                new Vector2(1f, 1f),
                new Vector2(-40f, -40f),
                new Vector2(120f, 120f),
                new Vector2(0.5f, 0.5f));

            // Three bars, drawn as glyphs rather than art so there is no sprite to ship.
            Text label = CreateLabel(_menuButton, "Glyph", 52, TextAnchor.MiddleCenter);
            label.text = "=\\n=";
        }

        private void BuildLabels()
        {
            _statusLabel = CreateLabel(transform, "Status", 30, TextAnchor.UpperLeft);
            Anchor(_statusLabel.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(560f, 40f), new Vector2(0f, 1f));
            _statusLabel.rectTransform.anchoredPosition = new Vector2(28f, -104f);

            _objectiveLabel = CreateLabel(transform, "Objective", 28, TextAnchor.UpperRight);
            Anchor(_objectiveLabel.rectTransform, new Vector2(1f, 1f), Vector2.zero, new Vector2(720f, 80f), new Vector2(1f, 1f));
            _objectiveLabel.rectTransform.anchoredPosition = new Vector2(-28f, -28f);

            _messageLabel = CreateLabel(transform, "Message", 40, TextAnchor.MiddleCenter);
            Anchor(_messageLabel.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(900f, 60f), new Vector2(0.5f, 1f));
            _messageLabel.rectTransform.anchoredPosition = new Vector2(0f, -170f);
            _messageLabel.color = new Color(0.95f, 0.86f, 0.6f, 0f);
        }

        private static void Anchor(
            RectTransform rect,
            Vector2 anchor,
            Vector2 anchoredPosition,
            Vector2 size,
            Vector2 pivot)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Text CreateLabel(Transform parent, string name, int fontSize, TextAnchor alignment)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);

            Text text = child.AddComponent<Text>();
            text.font = ResolveFont();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = new Color(0.92f, 0.92f, 0.95f, 0.92f);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = string.Empty;

            return text;
        }

        private static Font _font;

        private static bool _fontResolved;

        /// <summary>
        /// Finds a font for the on-screen labels.
        ///
        /// Unity renamed its built-in font in 2022.2, so both names are tried. If
        /// neither is present the labels are simply blank rather than throwing -
        /// the bars and buttons still work, so the game stays playable.
        /// </summary>
        private static Font ResolveFont()
        {
            if (_fontResolved)
            {
                return _font;
            }

            _fontResolved = true;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (_font == null)
            {
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return _font;
        }

        // --------------------------------- update ---------------------------------

        /// <summary>
        /// Whether the on-screen controls respond to touches.
        ///
        /// Turned off while a menu is open. Without this a tap on a menu row would
        /// also swing the camera or move the Warden, because both are reading the same
        /// pointers from the same screen.
        /// </summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>
        /// Reads pointers and feeds the driver. Called by the bootstrap before the
        /// session is stepped, so on-screen input and the simulation it drives stay
        /// in the same order every frame.
        /// </summary>
        public void PollInput()
        {
            if (_driver == null)
            {
                return;
            }

            if (!InputEnabled)
            {
                // Release anything already held, so the Warden does not keep walking
                // in the direction the thumb was pointing when the menu opened.
                if (_pointers.Count > 0)
                {
                    _pointers.Clear();
                    _driver.SetMoveAxis(Vector2.zero);
                    _stickHandle.anchoredPosition = Vector2.zero;
                }

                return;
            }

            PollTouchscreen();
            PollMouseFallback();
            ApplyActivePointers();
        }

        private void PollTouchscreen()
        {
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return;
            }

            var touches = touchscreen.touches;

            for (int i = 0; i < touches.Count; i++)
            {
                TouchControl touch = touches[i];

                int id = touch.touchId.ReadValue();
                Vector2 position = touch.position.ReadValue();
                bool pressed = touch.press.isPressed;

                if (pressed)
                {
                    if (_pointers.ContainsKey(id))
                    {
                        PointerState moved = _pointers[id];
                        moved.Current = position;
                        _pointers[id] = moved;
                    }
                    else
                    {
                        BeginPointer(id, position);
                    }
                }
                else if (_pointers.ContainsKey(id))
                {
                    EndPointer(id);
                }
            }
        }

        /// <summary>
        /// Maps the mouse onto the on-screen controls, but only when device input is
        /// off. Otherwise a left click would both attack and drive the stick.
        /// </summary>
        private void PollMouseFallback()
        {
            if (_driver.DeviceInputEnabled)
            {
                if (_pointers.ContainsKey(MousePointerId))
                {
                    EndPointer(MousePointerId);
                }

                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 position = mouse.position.ReadValue();
            bool pressed = mouse.leftButton.isPressed;

            if (pressed)
            {
                if (_pointers.ContainsKey(MousePointerId))
                {
                    PointerState moved = _pointers[MousePointerId];
                    moved.Current = position;
                    _pointers[MousePointerId] = moved;
                }
                else
                {
                    BeginPointer(MousePointerId, position);
                }
            }
            else if (_pointers.ContainsKey(MousePointerId))
            {
                EndPointer(MousePointerId);
            }
        }

        private void BeginPointer(int id, Vector2 position)
        {
            var state = new PointerState
            {
                Role = PointerRole.None,
                AbilityIndex = -1,
                Origin = position,
                Current = position
            };

            // The menu button is checked first: it sits over the look area, and a press
            // that lands on it must not also swing the camera.
            if (_menuButton != null &&
                RectTransformUtility.RectangleContainsScreenPoint(_menuButton, position, null))
            {
                _pointers[id] = state;
                MenuRequested?.Invoke();
                return;
            }

            // Buttons take priority over the movement and look regions, for the same
            // reason.
            for (int i = 0; i < _abilityButtons.Count; i++)
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(_abilityButtons[i], position, null))
                {
                    continue;
                }

                state.Role = PointerRole.Ability;
                state.AbilityIndex = _buttonIndexes[i];
                _pointers[id] = state;

                _driver.RequestAbility(state.AbilityIndex);
                return;
            }

            // The left half below the top bars is movement; everything else looks.
            float halfWidth = Screen.width * 0.5f;
            bool belowBars = position.y < Screen.height - (Screen.height * 0.22f);

            if (position.x < halfWidth && belowBars && id != MousePointerId)
            {
                state.Role = PointerRole.Move;

                // The stick origin is wherever the thumb landed, clamped so the
                // handle stays on screen. Anchoring it to a fixed home position
                // makes it unusable for anyone holding the phone differently.
                state.Origin = new Vector2(
                    Mathf.Clamp(position.x, StickRadius, halfWidth - StickRadius),
                    Mathf.Clamp(position.y, StickRadius, Screen.height - StickRadius));
            }
            else
            {
                state.Role = PointerRole.Look;
            }

            _pointers[id] = state;
        }

        private void EndPointer(int id)
        {
            if (!_pointers.TryGetValue(id, out PointerState state))
            {
                return;
            }

            if (state.Role == PointerRole.Move)
            {
                _driver.SetMoveAxis(Vector2.zero);
                _stickHandle.anchoredPosition = Vector2.zero;
            }

            _pointers.Remove(id);
        }

        private void ApplyActivePointers()
        {
            if (_pointers.Count == 0)
            {
                return;
            }

            // Copied out first. A look pointer consumes its delta by moving its own
            // origin, and writing to a dictionary entry while enumerating that same
            // dictionary invalidates the enumerator.
            _pointerKeys.Clear();
            _pointerKeys.AddRange(_pointers.Keys);

            for (int i = 0; i < _pointerKeys.Count; i++)
            {
                int id = _pointerKeys[i];

                if (!_pointers.TryGetValue(id, out PointerState state))
                {
                    continue;
                }

                if (state.Role == PointerRole.Look)
                {
                    _driver.AddLookDelta(state.Current - state.Origin);

                    PointerState consumed = state;
                    consumed.Origin = state.Current;
                    _pointers[id] = consumed;
                    continue;
                }

                if (state.Role != PointerRole.Move)
                {
                    continue;
                }

                Vector2 offset = state.Current - state.Origin;

                // A small dead zone stops a resting thumb from drifting the Warden
                // across the arena while the player is only trying to hold still.
                Vector2 axis = offset.magnitude <= MoveDeadZonePixels
                    ? Vector2.zero
                    : Vector2.ClampMagnitude(offset / StickRadius, 1f);

                _driver.SetMoveAxis(axis);
                PositionStick(state.Origin, state.Current);
            }
        }

        /// <summary>Pixels the stick must move before it counts as input.</summary>
        private const float MoveDeadZonePixels = 10f;

        private readonly List<int> _pointerKeys = new List<int>(4);

        /// <summary>
        /// Draws the stick wherever the thumb landed.
        ///
        /// Both pointer positions are converted through the canvas rather than
        /// scaled by a fixed factor, so the handle stays under the finger at any
        /// reference resolution instead of drifting on an unusual aspect ratio.
        /// </summary>
        private void PositionStick(Vector2 origin, Vector2 current)
        {
            if (_stickBackground == null || _canvasRect == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, origin, null, out Vector2 originLocal))
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, current, null, out Vector2 currentLocal))
            {
                return;
            }

            // The canvas rect's pivot is its centre while the stick is anchored to
            // the bottom-left corner, so half the canvas size converts between them.
            var halfCanvas = new Vector2(
                _canvasRect.rect.width * 0.5f,
                _canvasRect.rect.height * 0.5f);

            _stickBackground.anchoredPosition = originLocal + halfCanvas;
            _stickHandle.anchoredPosition = currentLocal - originLocal;
        }

        /// <summary>Copies live game state onto the display. Safe to call every frame.</summary>
        public void Refresh()
        {
            if (_session == null)
            {
                return;
            }

            Combatant player = _session.Player;

            if (player != null && player.Vitals != null)
            {
                _healthFill.fillAmount = Normalise(player.Vitals.Health, player.Vitals.MaxHealth);
                _staminaFill.fillAmount = Normalise(player.Vitals.Stamina, player.Vitals.MaxStamina);
            }

            RefreshExperience();
            RefreshAbilities();
            RefreshStatus();
            RefreshMessage();
        }

        private static float Normalise(float value, float maximum)
        {
            if (maximum <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(value / maximum);
        }

        private void RefreshExperience()
        {
            int level = _session.Progression.Level;
            int total = _session.Progression.TotalExperience;

            int levelStart = _curve.TotalExperienceAtLevel(level);
            int needed = _curve.ExperienceForNextLevel(level);

            _experienceFill.fillAmount = needed <= 0
                ? 0f
                : Mathf.Clamp01((total - levelStart) / (float)needed);
        }

        private void RefreshAbilities()
        {
            // Resolved every frame rather than cached. Travel replaces the encounter,
            // and with it the participant that owns the player's ability controller - so
            // a cached reference would leave the cooldown display frozen on the values
            // from the region the player left. A handful of id comparisons per frame is
            // not worth being wrong for.
            _playerParticipant = _session.Encounter.Find(_session.Player.Id);

            AbilityController controller = _playerParticipant?.Abilities;
            if (controller == null)
            {
                return;
            }

            for (int i = 0; i < _abilityCooldownFills.Count; i++)
            {
                int index = _buttonIndexes[i];

                if (index >= controller.Count)
                {
                    // A button with no ability behind it is hidden rather than
                    // shown as a dead control.
                    _abilityCooldownFills[i].fillAmount = 0f;
                    _abilityLabels[i].text = string.Empty;
                    continue;
                }

                _abilityCooldownFills[i].fillAmount = Mathf.Clamp01(controller.CooldownFraction(index));

                AbilityDefinition ability = controller[index];
                if (ability != null)
                {
                    // First letter plus the ability's stamina cost: enough to learn
                    // the layout without reading a wall of text mid-fight.
                    string name = string.IsNullOrEmpty(ability.DisplayName) ? "?" : ability.DisplayName;
                    _abilityLabels[i].text = name.Substring(0, 1).ToUpperInvariant();
                }
            }
        }

        private void RefreshStatus()
        {
            int hostiles = _session.Encounter.HostilesRemaining;

            _statusLabel.text =
                "LEVEL " + _session.Progression.Level +
                "    HOSTILES " + hostiles +
                (_session.Progression.UnspentAttributePoints > 0
                    ? "    POINTS " + _session.Progression.UnspentAttributePoints
                    : string.Empty);

            string objective = CurrentObjectiveText();
            _objectiveLabel.text = objective;
        }

        /// <summary>Describes the first active quest objective, for the corner readout.</summary>
        private string CurrentObjectiveText()
        {
            IReadOnlyList<Core.Quests.QuestState> all = _session.Quests.All;

            for (int i = 0; i < all.Count; i++)
            {
                Core.Quests.QuestState state = all[i];

                if (state.Status != Core.Quests.QuestStatus.Active)
                {
                    continue;
                }

                string title = state.Definition.Title;
                Core.Quests.ObjectiveDefinition[] objectives = state.Definition.Objectives;

                if (objectives.Length == 0)
                {
                    return title;
                }

                Core.Quests.ObjectiveDefinition objective = objectives[0];
                int progress = state.ProgressOf(objective.Id);
                int required = objective.RequiredCount;

                return required > 1
                    ? title + "\n" + objective.Description + "  " + progress + " / " + required
                    : title + "\n" + objective.Description;
            }

            return _session.EncounterCleared ? "The field is quiet." : string.Empty;
        }

        private void RefreshMessage()
        {
            if (_messageTimer <= 0f)
            {
                return;
            }

            _messageTimer -= Time.deltaTime;

            Color colour = _messageLabel.color;
            colour.a = Mathf.Clamp01(_messageTimer);
            _messageLabel.color = colour;
        }

        private void ShowMessage(string message)
        {
            if (_messageLabel == null)
            {
                return;
            }

            _messageLabel.text = message;

            Color colour = _messageLabel.color;
            colour.a = 1f;
            _messageLabel.color = colour;

            _messageTimer = 2.5f;
        }

        private void OnLevelledUp(int levels)
        {
            ShowMessage(levels > 1 ? "Level +" + levels : "Level up");
        }

        private void OnLootGranted(Core.Items.ItemStack stack)
        {
            if (_session.Items.TryGet(stack.ItemId, out Core.Items.ItemDefinition definition) && definition != null)
            {
                ShowMessage(definition.DisplayName + (stack.Quantity > 1 ? " x" + stack.Quantity : string.Empty));
            }
        }

        private void OnSomethingDied(Combatant victim, Participant participant)
        {
            if (victim != null && victim.IsBoss)
            {
                ShowMessage(victim.DisplayName + " falls");
            }
        }
    }
}
