using System;
using System.Collections.Generic;
using Godot;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Items;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Quests;
using Shadowbound.Core.Simulation;

namespace Shadowbound.Game
{
    /// <summary>
    /// The heads-up display and the on-screen controls, drawn directly with
    /// Control primitives.
    ///
    /// The project has always built its HUD in code rather than compiled UI: the
    /// layout is numbers that can be reviewed, and there is no binary asset to
    /// drift out of step. The on-screen controls are the reason this exists at
    /// all - Android has no keyboard, so the twin-stick layout is the difference
    /// between a build that launches and a game that can be played.
    ///
    /// Ability buttons are drawn and hit-tested from the SAME rectangles, so a
    /// button can never be somewhere other than where it looks.
    /// </summary>
    public partial class Hud : Control
    {
        private enum TouchRole
        {
            None,
            Move,
            Look,
            Button
        }

        private struct TouchSlot
        {
            public TouchRole Role;
            public Vector2 Origin;
            public Vector2 Last;
        }

        public event Action MenuRequested;

        // ---- layout (canvas units; the project stretches a 1920x1080 canvas) ----
        private const float Margin = 28f;
        private const float BarWidth = 560f;
        private const float BarHeight = 30f;
        private const float StickRadius = 160f;
        private const float ButtonSize = 132f;
        private const float ButtonGap = 16f;
        private const float MoveDeadZonePixels = 10f;
        public const int AbilityButtonCount = 5;

        private const float DamageNumberLifetime = 0.9f;
        private const float DamageNumberRise = 46f;
        private const int MousePointerId = -999;

        private Font _font;
        private GameSession _session;
        private PlayerInputReader _input;
        private Camera3D _camera;

        private readonly ExperienceCurve _curve = new ExperienceCurve();
        private readonly Dictionary<int, TouchSlot> _touches = new Dictionary<int, TouchSlot>(4);

        private struct DamageNumber
        {
            public Vector3 World;
            public float Amount;
            public float Age;
            public bool Critical;
        }

        private readonly List<DamageNumber> _damageNumbers = new List<DamageNumber>(16);

        private string _message = "";
        private float _messageTimer;

        /// <summary>Whether the on-screen controls respond to touches. Off while the menu is open.</summary>
        public bool InputEnabled { get; set; } = true;

        public override void _Ready()
        {
            _font = ThemeDB.FallbackFont;
            MouseFilter = MouseFilterEnum.Ignore;
            SetProcess(true);
        }

        public void Bind(GameSession session, PlayerInputReader input, Camera3D camera)
        {
            _session = session;
            _input = input;
            _camera = camera;
        }

        public void ReportDamage(Vector3 worldPosition, float amount, bool critical)
        {
            if (amount <= 0.5f)
            {
                return;
            }

            if (_damageNumbers.Count >= 24)
            {
                _damageNumbers.RemoveAt(0);
            }

            _damageNumbers.Add(new DamageNumber
            {
                World = worldPosition,
                Amount = amount,
                Age = 0f,
                Critical = critical
            });
        }

        public void ShowMessage(string message)
        {
            _message = message ?? "";
            _messageTimer = 2.5f;
        }

        public override void _Process(double deltaSeconds)
        {
            float delta = (float)deltaSeconds;

            DecayDamageNumbers(delta);

            if (_messageTimer > 0f)
            {
                _messageTimer -= delta;
            }

            PollTouches();

            QueueRedraw();
        }

        private void DecayDamageNumbers(float delta)
        {
            for (int i = _damageNumbers.Count - 1; i >= 0; i--)
            {
                DamageNumber number = _damageNumbers[i];
                number.Age += delta;

                if (number.Age >= DamageNumberLifetime)
                {
                    _damageNumbers.RemoveAt(i);
                    continue;
                }

                _damageNumbers[i] = number;
            }
        }

        // ============================= input =====================================

        public override void _UnhandledInput(InputEvent @event)
        {
            switch (@event)
            {
                case InputEventScreenTouch touch:
                    if (touch.Pressed) { BeginPointer(touch.Index, touch.Position); }
                    else { EndPointer(touch.Index); }
                    break;

                case InputEventScreenDrag drag:
                    MovePointer(drag.Index, drag.Position);
                    break;

                case InputEventMouseButton button when button.ButtonIndex == MouseButton.Left
                    && _input != null && !_input.DeviceInputEnabled:
                    if (button.Pressed) { BeginPointer(MousePointerId, button.Position); }
                    else { EndPointer(MousePointerId); }
                    break;

                case InputEventMouseMotion motion when _input != null && !_input.DeviceInputEnabled
                    && _touches.ContainsKey(MousePointerId):
                    MovePointer(MousePointerId, motion.Position);
                    break;
            }
        }

        private void BeginPointer(int id, Vector2 position)
        {
            if (!InputEnabled || _input == null)
            {
                return;
            }

            // The menu button wins over everything, then ability buttons, then the
            // screen halves.
            if (MenuButtonRect().HasPoint(position))
            {
                _touches[id] = new TouchSlot { Role = TouchRole.Button };
                MenuRequested?.Invoke();
                return;
            }

            for (int i = 0; i < AbilityButtonCount; i++)
            {
                if (!AbilityRect(i).HasPoint(position))
                {
                    continue;
                }

                _touches[id] = new TouchSlot { Role = TouchRole.Button };
                _input.RequestAbility(i);
                return;
            }

            var slot = new TouchSlot
            {
                Origin = position,
                Last = position
            };

            if (position.X <= Size.X * 0.5f)
            {
                slot.Role = TouchRole.Move;

                // The stick origin is wherever the thumb landed, so it is usable
                // however the phone is held.
                _input.SetHeldAbility(-1);
            }
            else
            {
                slot.Role = TouchRole.Look;
            }

            _touches[id] = slot;
        }

        private void MovePointer(int id, Vector2 position)
        {
            if (!_touches.TryGetValue(id, out TouchSlot slot))
            {
                return;
            }

            slot.Last = position;
            _touches[id] = slot;
        }

        private void EndPointer(int id)
        {
            if (!_touches.TryGetValue(id, out TouchSlot slot))
            {
                return;
            }

            if (slot.Role == TouchRole.Move)
            {
                _input?.SetTouchMoveAxis(Vector2.Zero);
            }

            _touches.Remove(id);
        }

        private void PollTouches()
        {
            if (_input == null)
            {
                return;
            }

            if (!InputEnabled)
            {
                if (_touches.Count > 0)
                {
                    _touches.Clear();
                    _input.SetTouchMoveAxis(Vector2.Zero);
                }

                return;
            }

            bool anyMove = false;

            foreach (KeyValuePair<int, TouchSlot> entry in _touches)
            {
                TouchSlot slot = entry.Value;

                if (slot.Role == TouchRole.Look)
                {
                    _input.AddLookDelta(slot.Last - slot.Origin);
                    slot.Origin = slot.Last;
                    _touches[entry.Key] = slot;
                }
                else if (slot.Role == TouchRole.Move)
                {
                    anyMove = true;
                    Vector2 offset = slot.Last - slot.Origin;
                    Vector2 axis = offset.Length() <= MoveDeadZonePixels
                        ? Vector2.Zero
                        : (offset / StickRadius).LimitLength(1f);
                    _input.SetTouchMoveAxis(axis);
                }
            }

            if (!anyMove)
            {
                _input.SetTouchMoveAxis(Vector2.Zero);
            }
        }

        // ============================= drawing ===================================

        public override void _Draw()
        {
            if (_session == null || _font == null)
            {
                return;
            }

            DrawVitals();
            DrawExperience();
            DrawAbilityButtons();
            DrawMoveStick();
            DrawFloatingDamage();
            DrawStatusLine();
            DrawMenuButton();
            DrawMessage();
        }

        private void DrawVitals()
        {
            Combatant player = _session.Player;
            if (player?.Vitals == null)
            {
                return;
            }

            float healthFraction = Mathf.Clamp(player.Vitals.HealthFraction, 0f, 1f);
            float staminaFraction = Mathf.Clamp(player.Vitals.StaminaFraction, 0f, 1f);

            float healthY = Margin;
            float staminaY = Margin + BarHeight + 6f;

            DrawBar(Margin, healthY, BarWidth, BarHeight, new Color(0.05f, 0.04f, 0.04f, 0.75f),
                new Color(0.62f, 0.16f, 0.16f, 0.95f), healthFraction,
                Mathf.RoundToInt(player.Vitals.Health) + " / " + Mathf.RoundToInt(player.Vitals.MaxHealth));

            DrawBar(Margin, staminaY, BarWidth * 0.8f, BarHeight, new Color(0.04f, 0.05f, 0.06f, 0.75f),
                new Color(0.30f, 0.52f, 0.58f, 0.95f), staminaFraction,
                Mathf.RoundToInt(player.Vitals.Stamina) + " / " + Mathf.RoundToInt(player.Vitals.MaxStamina));
        }

        private void DrawBar(float x, float y, float width, float height, Color back, Color fill, float fraction, string label)
        {
            DrawRect(new Rect2(x, y, width, height), back);
            DrawRect(new Rect2(x, y, width * fraction, height), fill);
            DrawString(_font, new Vector2(x + 8f, y + height - 8f), label, HorizontalAlignment.Left, -1, 18, new Color(1f, 1f, 1f, 0.92f));
        }

        private void DrawExperience()
        {
            int level = _session.Progression.Level;
            int total = _session.Progression.TotalExperience;
            int levelStart = _curve.TotalExperienceAtLevel(level);
            int needed = _curve.ExperienceForNextLevel(level);

            float fraction = needed <= 0 ? 0f : Mathf.Clamp((total - levelStart) / (float)needed, 0f, 1f);

            DrawRect(new Rect2(0f, 0f, Size.X, 8f), new Color(0f, 0f, 0f, 0.5f));
            DrawRect(new Rect2(0f, 0f, Size.X * fraction, 8f), new Color(0.78f, 0.64f, 0.32f, 0.95f));
        }

        private Rect2 AbilityRect(int index)
        {
            float size = ButtonSize * (index == 0 ? 1.25f : 1f);
            float offset = 0f;

            for (int i = 0; i < index; i++)
            {
                offset += (ButtonSize * (i == 0 ? 1.25f : 1f)) + ButtonGap;
            }

            float x = Size.X - Margin - size - offset;
            float y = Size.Y - Margin - size;
            return new Rect2(x, y, size, size);
        }

        private void DrawAbilityButtons()
        {
            Participant participant = _session.Encounter.Find(_session.Player.Id);
            if (participant == null)
            {
                return;
            }

            for (int i = 0; i < AbilityButtonCount; i++)
            {
                Rect2 rect = AbilityRect(i);
                float cooldown = Mathf.Clamp(participant.Abilities.CooldownFraction(i), 0f, 1f);
                bool ready = participant.Abilities.IsReady(i) && !participant.Abilities.IsBusy;

                Color back = ready
                    ? new Color(0.22f, 0.20f, 0.26f, 0.8f)
                    : new Color(0.10f, 0.09f, 0.12f, 0.8f);

                DrawRect(rect, back);

                if (cooldown > 0f)
                {
                    DrawRect(new Rect2(rect.Position.X, rect.Position.Y + rect.Size.Y * (1f - cooldown), rect.Size.X, rect.Size.Y * cooldown),
                        new Color(0f, 0f, 0f, 0.55f));
                }

                AbilityDefinition ability = participant.Abilities[i];
                string label = ability != null && !string.IsNullOrEmpty(ability.DisplayName)
                    ? ability.DisplayName
                    : (i + 1).ToString();

                Color textColor = ready ? Colors.White : new Color(0.6f, 0.6f, 0.6f);
                DrawString(_font, new Vector2(rect.Position.X + 8f, rect.Position.Y + rect.Size.Y * 0.5f),
                    label, HorizontalAlignment.Left, rect.Size.X - 12f, 16, textColor);
            }
        }

        private Rect2 MenuButtonRect()
        {
            float size = 96f;
            return new Rect2(Size.X - Margin - size, Margin, size, size);
        }

        private void DrawMenuButton()
        {
            Rect2 rect = MenuButtonRect();
            DrawRect(rect, new Color(0.16f, 0.15f, 0.20f, 0.7f));
            DrawString(_font, new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * 0.5f + 8f),
                "MENU", HorizontalAlignment.Center, rect.Size.X, 20, new Color(0.9f, 0.9f, 0.94f, 0.92f));
        }

        private void DrawMoveStick()
        {
            Vector2 baseCentre = new Vector2(Margin + StickRadius, Size.Y - Margin - StickRadius);
            var knob = baseCentre;

            foreach (KeyValuePair<int, TouchSlot> entry in _touches)
            {
                if (entry.Value.Role != TouchRole.Move)
                {
                    continue;
                }

                baseCentre = entry.Value.Origin;
                knob = entry.Value.Last;
            }

            DrawRect(new Rect2(baseCentre.X - StickRadius, baseCentre.Y - StickRadius, StickRadius * 2f, StickRadius * 2f),
                new Color(0.14f, 0.14f, 0.18f, 0.35f));

            Vector2 delta = knob - baseCentre;
            if (delta.Length() > StickRadius)
            {
                knob = baseCentre + delta.Normalized() * StickRadius;
            }

            float knobRadius = StickRadius * 0.35f;
            DrawRect(new Rect2(knob.X - knobRadius, knob.Y - knobRadius, knobRadius * 2f, knobRadius * 2f),
                new Color(0.85f, 0.85f, 0.9f, 0.55f));
        }

        private void DrawFloatingDamage()
        {
            for (int i = 0; i < _damageNumbers.Count; i++)
            {
                DamageNumber number = _damageNumbers[i];

                if (_camera == null || _camera.IsPositionBehind(number.World))
                {
                    continue;
                }

                Vector2 screen = _camera.UnprojectPosition(number.World);
                float alpha = Mathf.Clamp(1f - (number.Age / DamageNumberLifetime), 0f, 1f);
                float rise = DamageNumberRise * (number.Age / DamageNumberLifetime);

                Color color = number.Critical
                    ? new Color(1f, 0.85f, 0.3f, alpha)
                    : new Color(1f, 0.35f, 0.28f, alpha);

                DrawString(_font, new Vector2(screen.X, screen.Y - rise), Mathf.RoundToInt(number.Amount).ToString(),
                    HorizontalAlignment.Left, -1, number.Critical ? 26 : 20, color);
            }
        }

        private void DrawStatusLine()
        {
            int hostiles = _session.Encounter.HostilesRemaining;
            string line = _session.RegionId + "    LEVEL " + _session.Progression.Level + "    HOSTILES " + hostiles;

            if (_session.Progression.UnspentAttributePoints > 0)
            {
                line += "    POINTS " + _session.Progression.UnspentAttributePoints;
            }

            DrawString(_font, new Vector2(Margin, Margin + (BarHeight + 6f) * 2f + 26f), line,
                HorizontalAlignment.Left, -1, 18, new Color(0.8f, 0.8f, 0.85f, 0.9f));

            DrawString(_font, new Vector2(Size.X - Margin, Margin + 110f), CurrentObjectiveText(),
                HorizontalAlignment.Right, 700f, 17, new Color(0.88f, 0.86f, 0.78f, 0.9f));
        }

        private string CurrentObjectiveText()
        {
            IReadOnlyList<QuestState> all = _session.Quests.All;

            for (int i = 0; i < all.Count; i++)
            {
                QuestState state = all[i];
                if (state.Status != QuestStatus.Active)
                {
                    continue;
                }

                string title = state.Definition.Title;
                ObjectiveDefinition[] objectives = state.Definition.Objectives;

                if (objectives.Length == 0)
                {
                    return title;
                }

                ObjectiveDefinition objective = objectives[0];
                int progress = state.ProgressOf(objective.Id);

                return objective.RequiredCount > 1
                    ? title + "\n" + objective.Description + "  " + progress + " / " + objective.RequiredCount
                    : title + "\n" + objective.Description;
            }

            return _session.EncounterCleared ? "The field is quiet." : "";
        }

        private void DrawMessage()
        {
            if (_messageTimer <= 0f || string.IsNullOrEmpty(_message))
            {
                return;
            }

            float alpha = Mathf.Clamp(_messageTimer, 0f, 1f);
            DrawString(_font, new Vector2(Size.X * 0.5f, 150f), _message,
                HorizontalAlignment.Center, Size.X, 28, new Color(0.95f, 0.86f, 0.6f, alpha));
        }
    }
}
