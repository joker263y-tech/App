using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Shadowbound.Core.Content;
using Shadowbound.Core.Items;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Serialization;
using Shadowbound.Core.Stats;
using Shadowbound.Core.World;

namespace Shadowbound.Game
{
    /// <summary>
    /// The in-game menu: equipment, attributes, travel and saves.
    ///
    /// This is the piece that turns a set of working systems into something a
    /// person can actually play. The rules it offers live in the core; the menu
    /// only asks for them, so a change to a rule cannot make the menu and the
    /// simulation disagree.
    ///
    /// Rows are a fixed pool that is relabelled, not rebuilt. Rebuilding on every
    /// open allocates and leaves garbage behind, which is the usual reason a menu
    /// stutters on a mid-range phone. The pool is paged because equipment,
    /// carried items, twelve attributes and saves cannot all share one screen,
    /// and a list that runs off the bottom is a feature that does not exist.
    /// </summary>
    public partial class GameMenu : Control
    {
        private const int RowPool = 16;

        private sealed class Row
        {
            public Button Button;
            public Action Activate;
        }

        private enum MenuPage
        {
            Main,
            Attributes,
            World,
            Saves
        }

        public GameRoot Root;

        private readonly List<Row> _rows = new List<Row>(RowPool);
        private readonly List<Action> _pendingActions = new List<Action>(RowPool);

        private VBoxContainer _rowContainer;
        private Label _status;
        private MenuPage _page = MenuPage.Main;
        private bool _isOpen;
        private float _statusTimer;

        private static readonly ExperienceCurve Curve = new ExperienceCurve();

        public bool IsOpen => _isOpen;

        public override void _Ready()
        {
            Visible = false;
            _rowContainer = GetNode<VBoxContainer>("Panel/VBox/Rows");
            _status = GetNode<Label>("Panel/VBox/Status");

            for (int i = 0; i < RowPool; i++)
            {
                int index = i;
                var button = new Button
                {
                    Text = "",
                    FocusMode = FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(0f, 46f),
                    Alignment = HorizontalAlignment.Left
                };

                var row = new Row { Button = button };
                button.Pressed += () => row.Activate?.Invoke();

                _rowContainer.AddChild(button);
                _rows.Add(row);
            }
        }

        public void Toggle()
        {
            SetOpen(!_isOpen);
        }

        public void SetOpen(bool open)
        {
            _isOpen = open;
            Visible = open;

            if (open)
            {
                _page = MenuPage.Main;
                Rebuild();
            }
        }

        public override void _Process(double deltaSeconds)
        {
            if (_statusTimer <= 0f)
            {
                return;
            }

            _statusTimer -= (float)deltaSeconds;
            Color color = _status.Modulate;
            color.A = Mathf.Clamp(_statusTimer, 0f, 1f);
            _status.Modulate = color;
        }

        public void ShowStatus(string message)
        {
            _status.Text = message;

            Color color = _status.Modulate;
            color.A = 1f;
            _status.Modulate = color;

            _statusTimer = 4f;
            GD.Print("Shadowbound menu: " + message);
        }

        // --------------------------------- rows ----------------------------------

        private void Rebuild()
        {
            _pendingActions.Clear();

            // Always first: a phone has no Escape key, so without a way out the
            // menu would be a trap.
            AddRow("RESUME", () => SetOpen(false));

            if (Root?.Session == null)
            {
                ShowStatus("There is no game in progress.");
                ApplyRows();
                return;
            }

            switch (_page)
            {
                case MenuPage.Attributes:
                    AddRow("<  BACK", () => GoTo(MenuPage.Main));
                    AddAttributeRows();
                    break;

                case MenuPage.World:
                    AddRow("<  BACK", () => GoTo(MenuPage.Main));
                    AddTravelRows();
                    break;

                case MenuPage.Saves:
                    AddRow("<  BACK", () => GoTo(MenuPage.Main));
                    AddSaveRows();
                    break;

                default:
                    AddMainRows();
                    break;
            }

            ApplyRows();
        }

        private void ApplyRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i < _pendingActions.Count)
                {
                    _rows[i].Activate = _pendingActions[i];
                    _rows[i].Button.Visible = true;
                }
                else
                {
                    _rows[i].Activate = null;
                    _rows[i].Button.Visible = false;
                }
            }
        }

        private void GoTo(MenuPage page)
        {
            _page = page;
            Rebuild();
        }

        private void AddRow(string label, Action activate)
        {
            AddRow(label, activate, new Color(0.92f, 0.92f, 0.95f));
        }

        private void AddRow(string label, Action activate, Color color)
        {
            if (_pendingActions.Count >= RowPool)
            {
                return;
            }

            int index = _pendingActions.Count;
            Button button = _rows[index].Button;

            button.Text = label;
            button.Disabled = activate == null;
            button.AddThemeColorOverride("font_color", color);
            button.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.6f, 0.64f));

            _pendingActions.Add(activate);
        }

        private void AddHeader(string text)
        {
            AddRow(text, null, new Color(0.72f, 0.66f, 0.44f));
        }

        private void AddNote(string text)
        {
            AddRow(text, null, new Color(0.66f, 0.66f, 0.70f));
        }

        private void AddMainRows()
        {
            AddHeader("EQUIPPED");

            for (int i = 0; i < EquipSlots.All.Length; i++)
            {
                AddEquipmentRow(EquipSlots.All[i]);
            }

            AddHeader("CARRIED");

            int carried = AddCarriedRows();
            if (carried == 0)
            {
                AddNote("Nothing usable in the bag.");
            }

            int points = Root.Session.Progression.UnspentAttributePoints;

            AddRow(
                points > 0 ? "ATTRIBUTES   (" + points + " to spend)" : "ATTRIBUTES",
                () => GoTo(MenuPage.Attributes),
                points > 0 ? new Color(0.95f, 0.88f, 0.60f) : new Color(0.82f, 0.88f, 0.94f));

            AddRow("THE WORLD", () => GoTo(MenuPage.World), new Color(0.82f, 0.88f, 0.94f));
            AddRow("SAVES", () => GoTo(MenuPage.Saves), new Color(0.82f, 0.88f, 0.94f));
        }

        private void AddEquipmentRow(EquipSlot slot)
        {
            EquipSlot captured = slot;
            string worn = Root.Session.Equipment.GetEquipped(slot);

            if (string.IsNullOrEmpty(worn))
            {
                AddRow(EquipSlots.Name(slot) + ":  -", null, new Color(0.6f, 0.6f, 0.64f));
                return;
            }

            string name = DisplayNameOf(worn);

            AddRow(
                EquipSlots.Name(slot) + ":  " + name + Describe(worn) + "     [take off]",
                () =>
                {
                    if (Root.Session.TryUnequipToInventory(captured))
                    {
                        ShowStatus("Removed " + name + ".");
                    }
                    else
                    {
                        ShowStatus("No room in the bag for " + name + ".");
                    }

                    Rebuild();
                });
        }

        private int AddCarriedRows()
        {
            int added = 0;

            foreach (KeyValuePair<string, int> entry in Root.Session.Inventory.Entries)
            {
                if (added >= 5)
                {
                    AddNote("...and more in the bag.");
                    return added;
                }

                if (!Root.Session.Items.TryGet(entry.Key, out ItemDefinition definition) ||
                    definition == null || !definition.IsEquippable)
                {
                    continue;
                }

                string id = entry.Key;
                string label = "Wear  " + definition.DisplayName + Describe(id);

                if (Root.Session.Equipment.GetEquipped(definition.Slot.Value) == id)
                {
                    label = "Wearing  " + definition.DisplayName;
                }

                AddRow(label, () =>
                {
                    if (Root.Session.TryEquipFromInventory(id, out EquipFailure failure))
                    {
                        ShowStatus("Equipped " + DisplayNameOf(id) + ".");
                    }
                    else
                    {
                        ShowStatus("Cannot equip: " + DescribeFailure(failure));
                    }

                    Rebuild();
                }, new Color(0.82f, 0.88f, 0.82f));

                added++;
            }

            // Second pass: consumables. These were handed out as quest rewards with
            // nothing anywhere able to drink one.
            foreach (KeyValuePair<string, int> entry in Root.Session.Inventory.Entries)
            {
                if (added >= 5)
                {
                    AddNote("...and more in the bag.");
                    return added;
                }

                if (!Root.Session.Items.TryGet(entry.Key, out ItemDefinition definition) ||
                    definition == null || !definition.IsConsumable)
                {
                    continue;
                }

                AddUseRow(entry.Key, entry.Value, definition);
                added++;
            }

            return added;
        }

        private void AddUseRow(string id, int quantity, ItemDefinition definition)
        {
            string name = definition.DisplayName;

            AddRow(
                "Use  " + name + "  x" + quantity + DescribeEffects(definition),
                () =>
                {
                    if (Root.Session.TryUseConsumable(id, out ConsumableFailure failure))
                    {
                        ShowStatus("Used " + name + ".");
                    }
                    else
                    {
                        ShowStatus("Cannot use it: " + DescribeUseFailure(failure));
                    }

                    Rebuild();
                },
                new Color(0.88f, 0.82f, 0.72f));
        }

        // -------------------------------- attributes ------------------------------

        private void AddAttributeRows()
        {
            int points = Root.Session.Progression.UnspentAttributePoints;

            AddNote(points <= 0
                ? "No points to spend yet. They arrive with levels and quests."
                : "Points to spend: " + points + ".  Each point buys the shown increase.");

            for (int i = 0; i < StatIds.All.Length; i++)
            {
                AddAttributeRow(StatIds.All[i], points > 0);
            }
        }

        private void AddAttributeRow(StatId stat, bool canSpend)
        {
            float current = Root.Session.Player.Stats.Get(stat);
            float award = GameContent.AttributeAward(stat);

            string label = StatIds.Name(stat) + ":  " + FormatStat(stat, current) +
                           "  ->  " + FormatStat(stat, current + award);

            if (!canSpend)
            {
                AddRow(label, null, new Color(0.55f, 0.55f, 0.58f));
                return;
            }

            StatId captured = stat;

            AddRow(label, () =>
            {
                if (Root.Session.Progression.TrySpendAttributePoint(captured, GameContent.AttributeAward(captured)))
                {
                    ShowStatus(StatIds.Name(captured) + " increased.");
                }
                else
                {
                    ShowStatus("No points to spend.");
                }

                Rebuild();
            }, new Color(0.82f, 0.88f, 0.82f));
        }

        private void AddTravelRows()
        {
            IReadOnlyList<RegionDefinition> regions = Root.Session.World.All;
            int added = 0;

            for (int i = 0; i < regions.Count; i++)
            {
                RegionDefinition region = regions[i];

                if (region == null || string.IsNullOrEmpty(region.Id))
                {
                    continue;
                }

                string id = region.Id;

                if (string.Equals(id, Root.Session.RegionId, StringComparison.Ordinal))
                {
                    AddRow("Here:  " + region.DisplayName, null, new Color(0.95f, 0.92f, 0.78f));
                    continue;
                }

                if (!Root.Session.CanTravelTo(id, out AccessFailure access))
                {
                    AddRow(region.DisplayName + "  (" + DescribeAccess(access) + ")", null, new Color(0.55f, 0.55f, 0.58f));
                    continue;
                }

                if (added >= 4)
                {
                    AddNote("...and further places beyond these.");
                    break;
                }

                AddRow(
                    "Travel:  " + region.DisplayName + "  (level " + region.RecommendedLevel + ")",
                    () =>
                    {
                        ShowStatus(Root.TravelTo(id, out string error) ? "Arrived in " + region.DisplayName + "." : error);
                        Rebuild();
                    },
                    new Color(0.82f, 0.88f, 0.94f));

                added++;
            }
        }

        private void AddSaveRows()
        {
            List<string> slots = new List<string>(Root.SaveManager.ListSlots());

            if (slots.Count == 0)
            {
                AddNote("No saves yet.");
            }

            for (int i = 0; i < slots.Count && i < 3; i++)
            {
                string slotId = slots[i];
                string summary = DescribeSlot(slotId);

                AddRow("Load  " + summary, () =>
                {
                    Root.SaveSlot = slotId;
                    ShowStatus(Root.TryLoad(out string error) ? "Loaded " + slotId + "." : "Load failed: " + error);
                    Rebuild();
                });
            }

            string current = Root.SaveSlot;
            string currentSummary = DescribeSlot(current);

            AddRow("Save  " + (currentSummary ?? current), () =>
            {
                Root.SaveSlot = current;
                ShowStatus(Root.TrySave(out string error) ? "Saved to " + current + "." : "Save failed: " + error);
                Rebuild();
            }, new Color(0.94f, 0.88f, 0.66f));
        }

        private string DescribeSlot(string slotId)
        {
            if (Root?.SaveManager == null || !Root.SaveManager.Exists(slotId))
            {
                return slotId;
            }

            SaveResult result = Root.SaveManager.Load(slotId, out SaveGame save);

            if (!result.Success || save == null)
            {
                return slotId + "  (unreadable)";
            }

            int level = Curve.LevelForExperience(save.TotalExperience);
            return slotId + "  -  " + save.ProfileName + ", level " + level + ", " + FormatPlaytime(save.PlaytimeSeconds);
        }

        // --------------------------------- helpers -------------------------------

        private string DisplayNameOf(string itemId)
        {
            return Root.Session.Items.TryGet(itemId, out ItemDefinition definition) && definition != null
                ? definition.DisplayName
                : itemId;
        }

        private string Describe(string itemId)
        {
            if (!Root.Session.Items.TryGet(itemId, out ItemDefinition definition) || definition == null)
            {
                return "";
            }

            StatModifier[] modifiers = definition.Modifiers;

            if (modifiers == null || modifiers.Length == 0)
            {
                return "";
            }

            var text = new StringBuilder("  (");

            for (int i = 0; i < modifiers.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                StatModifier modifier = modifiers[i];

                switch (modifier.Op)
                {
                    case ModifierOp.Flat:
                        text.Append('+').Append(Mathf.RoundToInt(modifier.Value));
                        break;
                    case ModifierOp.PercentAdditive:
                        text.Append('+').Append(Mathf.RoundToInt(modifier.Value)).Append('%');
                        break;
                    case ModifierOp.PercentMultiplicative:
                        text.Append('x').Append(modifier.Value.ToString("0.##"));
                        break;
                }

                text.Append(' ').Append(StatIds.Name(modifier.Stat));
            }

            return text.Append(')').ToString();
        }

        private static string DescribeEffects(ItemDefinition definition)
        {
            ItemEffect[] effects = definition.Effects;

            if (effects == null || effects.Length == 0)
            {
                return "";
            }

            var text = new StringBuilder("  (");

            for (int i = 0; i < effects.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                ItemEffect effect = effects[i];

                switch (effect.Kind)
                {
                    case EffectKind.RestoreHealth:
                        text.Append('+').Append(Mathf.RoundToInt(effect.Amount)).Append(" health");
                        break;
                    case EffectKind.RestoreStamina:
                        text.Append('+').Append(Mathf.RoundToInt(effect.Amount)).Append(" stamina");
                        break;
                    case EffectKind.ApplyStatus:
                        text.Append(effect.Status).Append(' ').Append(effect.Duration.ToString("0.#")).Append('s');
                        break;
                    case EffectKind.GrantExperience:
                        text.Append('+').Append(Mathf.RoundToInt(effect.Amount)).Append(" experience");
                        break;
                }
            }

            return text.Append(')').ToString();
        }

        private static string FormatStat(StatId stat, float value)
        {
            return StatIds.IsFraction(stat)
                ? Mathf.RoundToInt(value * 100f) + "%"
                : value.ToString("0.#");
        }

        private static string FormatPlaytime(float seconds)
        {
            if (seconds < 60f)
            {
                return Mathf.RoundToInt(seconds) + "s";
            }

            int minutes = Mathf.FloorToInt(seconds / 60f);
            return minutes < 60 ? minutes + "m" : (minutes / 60) + "h " + (minutes % 60) + "m";
        }

        private static string DescribeAccess(AccessFailure failure)
        {
            switch (failure)
            {
                case AccessFailure.UnknownRegion: return "unknown";
                case AccessFailure.ChapterIncomplete: return "closed for now";
                case AccessFailure.NotConnected: return "no way there from here";
                default: return "not yet";
            }
        }

        private static string DescribeFailure(EquipFailure failure)
        {
            switch (failure)
            {
                case EquipFailure.UnknownItem: return "you are not carrying it";
                case EquipFailure.NotEquippable: return "it is not equipment";
                case EquipFailure.WrongSlot: return "wrong slot";
                case EquipFailure.LevelTooLow: return "your level is too low";
                case EquipFailure.SlotLocked: return "that slot is locked";
                default: return "not possible";
            }
        }

        private static string DescribeUseFailure(ConsumableFailure failure)
        {
            switch (failure)
            {
                case ConsumableFailure.UnknownItem: return "you are not carrying it";
                case ConsumableFailure.NotConsumable: return "it cannot be used";
                case ConsumableFailure.NotHeld: return "the last one is gone";
                default: return "not possible";
            }
        }
    }
}
