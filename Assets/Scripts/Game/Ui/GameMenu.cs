using System;
using System.Collections.Generic;
using Shadowbound.Core.Content;
using Shadowbound.Core.Items;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Serialization;
using Shadowbound.Core.Stats;
using Shadowbound.Core.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Shadowbound.Game.Ui
{
    /// <summary>
    /// The in-game menu: equipment and save slots.
    ///
    /// This is the piece that turns a set of working systems into something a person
    /// can actually play. Before it existed, an APK could be installed and launched,
    /// but there was no way to wear an item and no way to resume a save - the core
    /// could do both, and nothing could ask it to.
    ///
    /// Two small pieces of judgement are baked in:
    ///
    ///   * Opening the menu sets Time.timeScale to zero, which stops the simulation
    ///     without stopping rendering. Loading a save while enemies were mid-swing
    ///     would otherwise be a race.
    ///   * Rows are a fixed pool that is relabelled, not rebuilt. Rebuilding on every
    ///     open allocates and leaves garbage behind, which is the usual reason a menu
    ///     stutters on a mid-range phone.
    ///
    /// Like the HUD, input is polled and hit-tested by hand rather than routed through
    /// uGUI's event system, which needs an EventSystem object and an input module -
    /// both silent failure points on a device build.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameMenu : MonoBehaviour
    {
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;

        // Sizing is set so the whole pool fits the panel: with these numbers the
        // last row ends above the status line and the first begins below the title.
        // The previous numbers drew rows 12+ below the panel and off the screen,
        // which made anything past the first few rows unreachable on a device.
        private const int RowPool = 16;
        private const float RowHeight = 42f;
        private const float RowGap = 5f;
        private const float RowWidth = 1000f;
        private const float FirstRowY = -100f;

        private sealed class Row
        {
            public GameObject Root;
            public Image Background;
            public Text Label;
            public RectTransform Rect;
            public Action Activate;
        }

        private GameBootstrap _game;
        private HudController _hud;

        private RectTransform _canvasRect;
        private GameObject _panel;
        private GameObject _dim;
        private Text _title;
        private Text _status;

        private readonly List<Row> _rows = new List<Row>(RowPool);

        /// <summary>Rebuilt on open and after any action, so it always reflects reality.</summary>
        private readonly List<Action> _pendingActions = new List<Action>(RowPool);

        /// <summary>
        /// Which page of the menu is showing.
        ///
        /// Paged rather than one long list because the pool is finite and the menu
        /// has grown past it: equipment, carried items, twelve attributes and saves
        /// cannot all share one screen, and a list that runs off the bottom is the
        /// same as a feature that does not exist.
        /// </summary>
        private enum MenuPage
        {
            Main = 0,
            Attributes = 1,
            World = 2,
            Saves = 3
        }

        private MenuPage _page = MenuPage.Main;

        private bool _pointerWasDown;
        private float _statusTimer;

        private static readonly ExperienceCurve Curve = new ExperienceCurve();

        public bool IsOpen { get; private set; }

        public static GameMenu Create(Transform parent, GameBootstrap game, HudController hud)
        {
            var host = new GameObject("Game Menu");
            host.transform.SetParent(parent, false);

            GameMenu menu = host.AddComponent<GameMenu>();
            menu.Initialise(game, hud);
            return menu;
        }

        private void Initialise(GameBootstrap game, HudController hud)
        {
            _game = game;
            _hud = hud;

            BuildCanvas();
            BuildChrome();

            for (int i = 0; i < RowPool; i++)
            {
                BuildRow(i);
            }

            SetOpen(false);
        }

        // ------------------------------- construction -----------------------------

        private void BuildCanvas()
        {
            var canvasObject = new GameObject("Menu Canvas");
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();

            // Sorted above the HUD so an open menu is not drawn behind it.
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _canvasRect = canvasObject.GetComponent<RectTransform>();
            _canvasRect.pivot = new Vector2(0.5f, 0.5f);
        }

        private void BuildChrome()
        {
            // A full-screen dim, which also swallows taps outside the panel so a
            // stray touch cannot act on the world behind the menu.
            Image dim = CreateImage(transform, "Dim", new Color(0f, 0f, 0f, 0.72f));
            Stretch(dim.rectTransform);
            _dim = dim.gameObject;

            Image panel = CreateImage(transform, "Panel", new Color(0.09f, 0.09f, 0.11f, 0.96f));
            _panel = panel.gameObject;
            Anchor(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1080f, 940f), new Vector2(0.5f, 0.5f));

            _title = CreateLabel(_panel.transform, "Title", 46, TextAnchor.UpperCenter);
            Anchor(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(1000f, 60f), new Vector2(0.5f, 1f));

            _status = CreateLabel(_panel.transform, "Status", 30, TextAnchor.LowerCenter);
            Anchor(_status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(1000f, 50f), new Vector2(0.5f, 0f));
            _status.color = new Color(0.95f, 0.84f, 0.52f, 0f);
        }

        private void BuildRow(int index)
        {
            Image background = CreateImage(_panel.transform, "Row " + index, new Color(1f, 1f, 1f, 0.07f));

            var row = new Row
            {
                Root = background.gameObject,
                Background = background,
                Rect = background.rectTransform
            };

            Anchor(
                row.Rect,
                new Vector2(0.5f, 1f),
                new Vector2(0f, FirstRowY - (index * (RowHeight + RowGap))),
                new Vector2(RowWidth, RowHeight),
                new Vector2(0.5f, 1f));

            row.Label = CreateLabel(row.Root.transform, "Label", 30, TextAnchor.MiddleLeft);
            Anchor(row.Label.rectTransform, new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(RowWidth - 56f, RowHeight), new Vector2(0f, 0.5f));

            row.Root.SetActive(false);
            _rows.Add(row);
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

        private static Text CreateLabel(Transform parent, string name, int size, TextAnchor alignment)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);

            Text text = child.AddComponent<Text>();
            text.font = ResolveFont();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = new Color(0.92f, 0.92f, 0.95f, 0.95f);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Font _font;

        private static bool _fontResolved;

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

        // ---------------------------------- open/close -----------------------------

        private void Update()
        {
            if (WasMenuKeyPressed())
            {
                SetOpen(!IsOpen);
            }

            if (!IsOpen)
            {
                return;
            }

            if (_statusTimer > 0f)
            {
                _statusTimer -= Time.unscaledDeltaTime;

                Color colour = _status.color;
                colour.a = Mathf.Clamp01(_statusTimer);
                _status.color = colour;
            }

            PollPointer();
        }

        private static bool WasMenuKeyPressed()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return false;
            }

            return keyboard.escapeKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame;
        }

        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        public void SetOpen(bool open)
        {
            IsOpen = open;
            _panel.SetActive(open);
            _dim.SetActive(open);

            if (open)
            {
                // Freezes the simulation without freezing rendering. Loading a save
                // while an enemy is mid-swing would otherwise be a race.
                Time.timeScale = 0f;

                if (_hud != null)
                {
                    _hud.InputEnabled = false;
                }

                Rebuild();
            }
            else
            {
                Time.timeScale = 1f;

                if (_hud != null)
                {
                    _hud.InputEnabled = true;
                }

                _pointerWasDown = false;
            }
        }

        // ---------------------------------- rows ----------------------------------

        /// <summary>Relabels the row pool from current game state.</summary>
        private void Rebuild()
        {
            _pendingActions.Clear();

            _title.text = "SHADOWBOUND";

            // Always first, because a touch device has no Escape key. Without a way
            // out that does not require a keyboard, the menu would be a trap.
            AddRow("RESUME", new Color(0.95f, 0.92f, 0.78f), () => SetOpen(false));

            if (_game == null || _game.Session == null)
            {
                ShowStatus("There is no game in progress.");
                HideUnusedRows(0);
                return;
            }

            switch (_page)
            {
                case MenuPage.Attributes:
                    AddRow("<  BACK", new Color(0.72f, 0.66f, 0.44f), () => GoTo(MenuPage.Main));
                    AddAttributeRows();
                    break;

                case MenuPage.World:
                    AddRow("<  BACK", new Color(0.72f, 0.66f, 0.44f), () => GoTo(MenuPage.Main));
                    AddTravelRows();
                    break;

                case MenuPage.Saves:
                    AddRow("<  BACK", new Color(0.72f, 0.66f, 0.44f), () => GoTo(MenuPage.Main));
                    AddSaveRows();
                    break;

                default:
                    AddMainRows();
                    break;
            }

            HideUnusedRows(_pendingActions.Count);

            for (int i = 0; i < _pendingActions.Count; i++)
            {
                _rows[i].Activate = _pendingActions[i];
                _rows[i].Root.SetActive(true);
            }
        }

        private void GoTo(MenuPage page)
        {
            _page = page;
            Rebuild();
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

            // The two things a menu must never hide: where the character sheet is,
            // and unspent points. Points that exist with nothing to spend them on is
            // the bug this page exists to prevent.
            int points = _game.Session.Progression.UnspentAttributePoints;

            AddRow(
                points > 0 ? "ATTRIBUTES   (" + points + " to spend)" : "ATTRIBUTES",
                points > 0 ? new Color(0.95f, 0.88f, 0.60f) : new Color(0.82f, 0.88f, 0.94f),
                () => GoTo(MenuPage.Attributes));

            AddRow("THE WORLD", new Color(0.82f, 0.88f, 0.94f), () => GoTo(MenuPage.World));

            AddRow("SAVES", new Color(0.82f, 0.88f, 0.94f), () => GoTo(MenuPage.Saves));
        }

        private void HideUnusedRows(int used)
        {
            for (int i = used; i < _rows.Count; i++)
            {
                _rows[i].Root.SetActive(false);
            }
        }

        /// <summary>Queues a row. Every row is one of these, so the pool stays uniform.</summary>
        private void AddRow(string label, Color colour, Action activate)
        {
            if (_pendingActions.Count >= RowPool)
            {
                return;
            }

            int index = _pendingActions.Count;

            _rows[index].Label.text = label;
            _rows[index].Label.color = colour;
            _rows[index].Background.color = activate == null
                ? new Color(1f, 1f, 1f, 0.03f)
                : new Color(1f, 1f, 1f, 0.09f);

            _pendingActions.Add(activate);
        }

        private void AddHeader(string text)
        {
            AddRow(text, new Color(0.72f, 0.66f, 0.44f), null);
        }

        private void AddNote(string text)
        {
            AddRow(text, new Color(0.66f, 0.66f, 0.70f), null);
        }

        private void AddEquipmentRow(EquipSlot slot)
        {
            EquipSlot captured = slot;
            string worn = _game.Session.Equipment.GetEquipped(slot);

            if (string.IsNullOrEmpty(worn))
            {
                AddRow(EquipSlots.Name(slot) + ":  -", new Color(0.6f, 0.6f, 0.64f), null);
                return;
            }

            string name = DisplayNameOf(worn);

            AddRow(
                EquipSlots.Name(slot) + ":  " + name + Describe(worn) + "     [take off]",
                new Color(0.92f, 0.92f, 0.95f),
                () =>
                {
                    if (_game.Session.TryUnequipToInventory(captured))
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

            foreach (KeyValuePair<string, int> entry in _game.Session.Inventory.Entries)
            {
                if (added >= 5)
                {
                    AddNote("...and more in the bag.");
                    return added;
                }

                if (!_game.Session.Items.TryGet(entry.Key, out ItemDefinition definition) ||
                    definition == null ||
                    !definition.IsEquippable)
                {
                    continue;
                }

                string id = entry.Key;
                string label = "Wear  " + definition.DisplayName + Describe(id);

                if (_game.Session.Equipment.GetEquipped(definition.Slot.Value) == id)
                {
                    label = "Wearing  " + definition.DisplayName;
                }

                AddRow(
                    label,
                    new Color(0.82f, 0.88f, 0.82f),
                    () =>
                    {
                        if (_game.Session.TryEquipFromInventory(id, out EquipFailure failure))
                        {
                            ShowStatus("Equipped " + id + ".");
                        }
                        else
                        {
                            ShowStatus("Cannot equip: " + DescribeFailure(failure));
                        }

                        Rebuild();
                    });

                added++;
            }

            // Second pass: consumables. Before this existed the game handed out
            // ember draughts as quest rewards and there was nothing anywhere that
            // could drink one - the item was a reward that did nothing.
            foreach (KeyValuePair<string, int> entry in _game.Session.Inventory.Entries)
            {
                if (added >= 5)
                {
                    AddNote("...and more in the bag.");
                    return added;
                }

                if (!_game.Session.Items.TryGet(entry.Key, out ItemDefinition definition) ||
                    definition == null ||
                    !definition.IsConsumable)
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
                new Color(0.88f, 0.82f, 0.72f),
                () =>
                {
                    if (_game.Session.TryUseConsumable(id, out ConsumableFailure failure))
                    {
                        ShowStatus("Used " + name + ".");
                    }
                    else
                    {
                        ShowStatus("Cannot use it: " + DescribeUseFailure(failure));
                    }

                    Rebuild();
                });
        }

        /// <summary>What a consumable will do, so drinking one is never a guess.</summary>
        private static string DescribeEffects(ItemDefinition definition)
        {
            ItemEffect[] effects = definition.Effects;

            if (effects == null || effects.Length == 0)
            {
                return string.Empty;
            }

            var text = new System.Text.StringBuilder("  (");

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
                        text.Append(effect.Status.ToString().ToLowerInvariant());
                        text.Append(' ').Append(effect.Duration.ToString("0.#")).Append('s');
                        break;

                    case EffectKind.GrantExperience:
                        text.Append('+').Append(Mathf.RoundToInt(effect.Amount)).Append(" experience");
                        break;
                }
            }

            text.Append(')');
            return text.ToString();
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

        // -------------------------------- attributes ------------------------------

        /// <summary>
        /// One row per stat, showing exactly what a point buys.
        ///
        /// Points used to pile up on the HUD with nothing in the game able to spend
        /// them. The row therefore shows both halves of the choice - the value now
        /// and the value after - so spending is a decision rather than a gamble.
        /// </summary>
        private void AddAttributeRows()
        {
            int points = _game.Session.Progression.UnspentAttributePoints;

            if (points <= 0)
            {
                AddNote("No points to spend yet. They arrive with levels and quests.");
            }
            else
            {
                AddNote("Points to spend: " + points + ".  Each point buys the shown increase.");
            }

            for (int i = 0; i < StatIds.All.Length; i++)
            {
                AddAttributeRow(StatIds.All[i], points > 0);
            }
        }

        private void AddAttributeRow(StatId stat, bool canSpend)
        {
            float current = _game.Session.Player.Stats.Get(stat);
            float award = GameContent.AttributeAward(stat);

            string label = StatIds.Name(stat) + ":  " + FormatStat(stat, current) +
                           "  ->  " + FormatStat(stat, current + award);

            if (!canSpend)
            {
                AddRow(label, new Color(0.55f, 0.55f, 0.58f), null);
                return;
            }

            StatId captured = stat;

            AddRow(
                label,
                new Color(0.82f, 0.88f, 0.82f),
                () =>
                {
                    if (_game.Session.Progression.TrySpendAttributePoint(
                            captured,
                            GameContent.AttributeAward(captured)))
                    {
                        ShowStatus(StatIds.Name(captured) + " increased.");
                    }
                    else
                    {
                        ShowStatus("No points to spend.");
                    }

                    Rebuild();
                });
        }

        private static string FormatStat(StatId stat, float value)
        {
            return StatIds.IsFraction(stat)
                ? Mathf.RoundToInt(value * 100f) + "%"
                : value.ToString("0.#");
        }

        /// <summary>
        /// Lists the regions the player can move to.
        ///
        /// Gated regions are shown greyed out with the reason rather than hidden. A
        /// story gate the player cannot see is indistinguishable from a bug, and
        /// hiding them would also hide the fact that the world is bigger than the
        /// region they are standing in.
        /// </summary>
        private void AddTravelRows()
        {
            IReadOnlyList<RegionDefinition> regions = _game.Session.World.All;
            int added = 0;

            for (int i = 0; i < regions.Count; i++)
            {
                RegionDefinition region = regions[i];

                if (region == null || string.IsNullOrEmpty(region.Id))
                {
                    continue;
                }

                string id = region.Id;

                if (string.Equals(id, _game.Session.RegionId, StringComparison.Ordinal))
                {
                    AddRow("Here:  " + region.DisplayName, new Color(0.95f, 0.92f, 0.78f), null);
                    continue;
                }

                // The same rule the gate uses, so the menu cannot offer a place the
                // world graph would refuse.
                if (!_game.Session.CanTravelTo(id, out AccessFailure access))
                {
                    AddRow(
                        region.DisplayName + "  (" + DescribeAccess(access) + ")",
                        new Color(0.55f, 0.55f, 0.58f),
                        null);

                    continue;
                }

                if (added >= 4)
                {
                    AddNote("...and further places beyond these.");
                    break;
                }

                AddRow(
                    "Travel:  " + region.DisplayName + "  (level " + region.RecommendedLevel + ")",
                    new Color(0.82f, 0.88f, 0.94f),
                    () =>
                    {
                        if (_game.TravelTo(id, out string error))
                        {
                            ShowStatus("Arrived in " + region.DisplayName + ".");
                        }
                        else
                        {
                            ShowStatus(error);
                        }

                        Rebuild();
                    });

                added++;
            }
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

        private void AddSaveRows()
        {
            List<string> slots = new List<string>(_game.SaveManager.ListSlots());

            if (slots.Count == 0)
            {
                AddNote("No saves yet.");
            }

            for (int i = 0; i < slots.Count && i < 3; i++)
            {
                string slotId = slots[i];
                string summary = DescribeSlot(slotId);

                AddRow(
                    "Load  " + summary + "     [delete]",
                    new Color(0.86f, 0.86f, 0.92f),
                    () => LoadSlot(slotId));
            }

            string current = _game.SaveSlot;
            string currentSummary = DescribeSlot(current);

            AddRow(
                "Save  " + (currentSummary == null ? current : currentSummary),
                new Color(0.94f, 0.88f, 0.66f),
                () => SaveSlot(current));
        }

        // ---------------------------------- actions -------------------------------

        private void SaveSlot(string slotId)
        {
            _game.SaveSlot = slotId;

            if (_game.TrySave(out string error))
            {
                ShowStatus("Saved to " + slotId + ".");
            }
            else
            {
                ShowStatus("Save failed: " + error);
            }

            Rebuild();
        }

        private void LoadSlot(string slotId)
        {
            _game.SaveSlot = slotId;

            if (!_game.TryLoad(out string error))
            {
                ShowStatus("Load failed: " + error);
                return;
            }

            ShowStatus("Loaded " + slotId + ".");
            Rebuild();
        }

        private string DescribeSlot(string slotId)
        {
            if (_game == null || _game.SaveManager == null || !_game.SaveManager.Exists(slotId))
            {
                return null;
            }

            SaveResult result = _game.SaveManager.Load(slotId, out SaveGame save);

            if (!result.Success || save == null)
            {
                // A slot that exists but will not parse is worth showing, because the
                // alternative is a menu that silently hides a corrupt save.
                return slotId + "  (unreadable)";
            }

            int level = Curve.LevelForExperience(save.TotalExperience);

            return slotId + "  -  " + save.ProfileName + ", level " + level +
                   ", " + FormatPlaytime(save.PlaytimeSeconds);
        }

        private static string FormatPlaytime(float seconds)
        {
            if (seconds < 60f)
            {
                return Mathf.RoundToInt(seconds) + "s";
            }

            int minutes = Mathf.FloorToInt(seconds / 60f);

            if (minutes < 60)
            {
                return minutes + "m";
            }

            return (minutes / 60) + "h " + (minutes % 60) + "m";
        }

        private string DisplayNameOf(string itemId)
        {
            return _game.Session.Items.TryGet(itemId, out ItemDefinition definition) && definition != null
                ? definition.DisplayName
                : itemId;
        }

        /// <summary>Renders an item's stat bonuses, so the choice is not blind.</summary>
        private string Describe(string itemId)
        {
            if (!_game.Session.Items.TryGet(itemId, out ItemDefinition definition) || definition == null)
            {
                return string.Empty;
            }

            StatModifier[] modifiers = definition.Modifiers;

            if (modifiers == null || modifiers.Length == 0)
            {
                return string.Empty;
            }

            var text = new System.Text.StringBuilder("  (");

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

            text.Append(')');

            return text.ToString();
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

        /// <summary>Shows a message on the menu. Public so the game can announce a region change.</summary>
        public void ShowStatus(string message)
        {
            if (_status == null)
            {
                return;
            }

            _status.text = message;

            Color colour = _status.color;
            colour.a = 1f;
            _status.color = colour;

            _statusTimer = 4f;

            Debug.Log("Shadowbound menu: " + message);
        }

        // ---------------------------------- input ---------------------------------

        private void PollPointer()
        {
            bool pressed = TryGetPointerPosition(out Vector2 position);

            if (pressed && !_pointerWasDown)
            {
                ActivateRowAt(position);
            }

            _pointerWasDown = pressed;
        }

        private static bool TryGetPointerPosition(out Vector2 position)
        {
            Touchscreen touchscreen = Touchscreen.current;

            if (touchscreen != null)
            {
                var touches = touchscreen.touches;

                for (int i = 0; i < touches.Count; i++)
                {
                    if (!touches[i].press.isPressed)
                    {
                        continue;
                    }

                    position = touches[i].position.ReadValue();
                    return true;
                }
            }

            Mouse mouse = Mouse.current;

            if (mouse != null && mouse.leftButton.isPressed)
            {
                position = mouse.position.ReadValue();
                return true;
            }

            position = Vector2.zero;
            return false;
        }

        private void ActivateRowAt(Vector2 screenPosition)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];

                if (!row.Root.activeSelf || row.Activate == null)
                {
                    continue;
                }

                if (RectTransformUtility.RectangleContainsScreenPoint(row.Rect, screenPosition, null))
                {
                    row.Activate();
                    return;
                }
            }
        }
    }
}
