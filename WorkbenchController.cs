using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using ImGuiNET;
using SharpDX;
using static ExileCore.PoEMemory.MemoryObjects.ServerInventory;
using Point = SharpDX.Point;
using Vector2 = SharpDX.Vector2;
using Vector4 = System.Numerics.Vector4;
using UiHelpers = SquirrelUi;

namespace SquirrelStash
{
    /// <summary>
    /// Owns the filter-test workbench window, the hover-info overlay and the autocomplete
    /// suggestion provider. Behavior moved verbatim from SquirrelStashCore.
    /// </summary>
    internal class WorkbenchController
    {
        internal static readonly Dictionary<string, Func<ItemData, string>> ItemPropertyGetters =
            new Dictionary<string, Func<ItemData, string>>(StringComparer.OrdinalIgnoreCase)
            {
                {"classname", d => d.ClassName},
                {"basename", d => d.BaseName},
                {"path", d => d.Path},
                {"name", d => d.Name},
                {"visceraslot", d => d.VisceraSlot},
                {"rarity", d => d.Rarity.ToString()},
            };

        private readonly SquirrelStashCore _core;

        internal bool _workbenchOpen;
        internal string _workbenchQuery = "";
        internal string _workbenchError;
        internal readonly Dictionary<string, int> _workbenchInvValues = new Dictionary<string, int>();
        internal readonly Dictionary<string, int> _workbenchStashValues = new Dictionary<string, int>();
        internal int _workbenchInvTotal;
        internal int _workbenchStashTotal;
        internal bool _workbenchInvVisible;
        internal bool _workbenchStashVisible;
        internal string _workbenchProp;
        internal EditorOpts _workbenchOpts;
        internal bool _wbSaveOpen;
        internal string _wbSaveName = "";
        internal string _wbSaveCategory = "Custom";
        internal List<CustomFilter> _workbenchFilters;

        public WorkbenchController(SquirrelStashCore core)
        {
            _core = core;
        }

        internal void DrawWorkbench()
        {
            UiHelpers.PushSettingsStyle();
            try
            {
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(700, 620), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("SquirrelStash Filter Test", ref _workbenchOpen))
            {
                ImGui.End();
                return;
            }

            UiHelpers.HouseTitle("Filter test");
            var changed = Editor.Code("stashieWorkbench", ref _workbenchQuery, GetWorkbenchOpts());
            if (changed)
                RefreshWorkbenchMatches();

            if (_workbenchError != null)
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), _workbenchError);
            else
                ImGui.TextColored(new Vector4(0f, 1f, 0f, 1f), "Query is valid.");

            ImGui.TextDisabled("Each line is a rule. Items matching any line are shown.");
            ImGui.Separator();

            var propLabel = _workbenchProp ?? "Items";

            if (_workbenchInvVisible && _workbenchStashVisible)
            {
                ImGui.Text($"{propLabel} matched:  inventory {_workbenchInvTotal}   stash {_workbenchStashTotal}");
                ImGui.BeginChild("##wbInvPane", new System.Numerics.Vector2(0.48f * ImGui.GetContentRegionAvail().X, 170), ImGuiChildFlags.Borders);
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), "Inventory");
                DrawWorkbenchMatchValues(_workbenchInvValues);
                ImGui.EndChild();
                ImGui.SameLine();
                ImGui.BeginChild("##wbStashPane", new System.Numerics.Vector2(0.48f * ImGui.GetContentRegionAvail().X, 170), ImGuiChildFlags.Borders);
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), "Stash");
                DrawWorkbenchMatchValues(_workbenchStashValues);
                ImGui.EndChild();
            }
            else if (_workbenchInvVisible)
            {
                ImGui.Text($"{propLabel} matched in inventory: {_workbenchInvTotal}");
                ImGui.BeginChild("##wbInvPane", new System.Numerics.Vector2(0, 150), ImGuiChildFlags.Borders);
                DrawWorkbenchMatchValues(_workbenchInvValues);
                ImGui.EndChild();
            }
            else if (_workbenchStashVisible)
            {
                ImGui.Text($"{propLabel} matched in stash: {_workbenchStashTotal}");
                ImGui.BeginChild("##wbStashPane", new System.Numerics.Vector2(0, 150), ImGuiChildFlags.Borders);
                DrawWorkbenchMatchValues(_workbenchStashValues);
                ImGui.EndChild();
            }
            else if (_workbenchError == null)
            {
                ImGui.TextDisabled("No matches. Open your inventory or the stash to scan.");
            }

            ImGui.Spacing();
            var wbActions = UiHelpers.ButtonRow(
                ("Save as new filter", "wbSaveNew", 1),
                ("Close", "wbClose", 0));
            if (wbActions == 0)
            {
                _wbSaveName = "";
                _wbSaveCategory = _core.Store._selectedFilter?.SubmenuName ?? "Custom";
                _wbSaveOpen = true;
                ImGui.OpenPopup("Save as new filter");
            }
            else if (wbActions == 1) _workbenchOpen = false;

            if (ImGui.BeginPopupModal("Save as new filter", ref _wbSaveOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                UiHelpers.HouseTitle("Save as new filter");
                ImGui.InputText("Name", ref _wbSaveName, 128);
                ImGui.InputText("Category", ref _wbSaveCategory, 128);
                ImGui.Spacing();
                var canSave = !string.IsNullOrWhiteSpace(_wbSaveName) &&
                              _workbenchError == null &&
                              !string.IsNullOrWhiteSpace(_workbenchQuery);
                int wbSaveChoice = UiHelpers.ButtonRow(("Save", "wbSave", 1), ("Cancel", "wbCancel", 0));
                if (wbSaveChoice == 0 && canSave)
                {
                    var lines = FilterProfileStore.SplitQueryLines(_workbenchQuery);

                    var filterName = _wbSaveName.Trim();
                    var filterCategory = _wbSaveCategory.Trim();

                    var newFilter = _core.Store.CreateFilter(filterName, filterCategory, lines[0], null);
                    if (_core.Settings.CustomFilterOptions.TryGetValue(newFilter.Name, out var templateNode))
                    {
                        for (var i = 1; i < lines.Count; i++)
                            _core.Store.CreateFilter(filterName, filterCategory, lines[i], templateNode);
                    }
                    _core.Store._selectedFilter = newFilter;
                    _core.Store.SaveCustomFilters();
                    _core.Store.GenerateMenu();
                    _wbSaveOpen = false;
                    ImGui.CloseCurrentPopup();
                }
                else if (wbSaveChoice == 1)
                {
                    _wbSaveOpen = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            ImGui.End();
            }
            finally
            {
                UiHelpers.PopSettingsStyle();
            }
        }

        internal void RefreshWorkbenchMatches()
        {
            _workbenchError = null;
            _workbenchFilters = null;
            _workbenchProp = null;
            _workbenchInvValues.Clear();
            _workbenchInvTotal = 0;
            _workbenchStashValues.Clear();
            _workbenchStashTotal = 0;

            var q = _workbenchQuery ?? "";
            if (string.IsNullOrWhiteSpace(q)) return;

            if (q.Contains(':'))
            {
                _workbenchError = "Query cannot contain ':' - it's used as the name/submenu separator.";
                return;
            }

            // Partial queries are expected while typing; validate quietly and report through
            // _workbenchError / the editor's inline underline instead of the global debug log.
            List<CustomFilter> filters;
            List<string> errors;
            FilterParser.SuppressDebugMessages = true;
            try { filters = FilterParser.Parse(new[] { "__:" + q + ":" }, out errors); }
            finally { FilterParser.SuppressDebugMessages = false; }
            if (errors.Count > 0)
            {
                _workbenchError = errors[0];
                return;
            }
            if (filters.Count == 0)
            {
                _workbenchError = "Query produced no filter.";
                return;
            }

            _workbenchFilters = filters;
            _workbenchProp = GetWorkbenchDisplayProperty(q);

            _workbenchInvVisible = IsPlayerInventoryVisible();
            _workbenchStashVisible = IsStashVisible();

            if (!_workbenchInvVisible && !_workbenchStashVisible)
            {
                _workbenchError = "Inventory not available - open your inventory or the stash to scan items.";
                return;
            }

            if (_workbenchInvVisible)
                AccumulateWorkbenchMatches(GetPlayerMatchItems(), _workbenchInvValues, ref _workbenchInvTotal, filters);

            if (_workbenchStashVisible)
                AccumulateWorkbenchMatches(GetStashMatchItems(), _workbenchStashValues, ref _workbenchStashTotal, filters);
        }

        private void AccumulateWorkbenchMatches(IEnumerable<ItemData> items, Dictionary<string, int> values,
            ref int total, List<CustomFilter> filters)
        {
            foreach (var itemData in items)
            {
                if (filters.Any(f => f.CompareItem(itemData)))
                {
                    total++;
                    var value = GetWorkbenchDisplayValue(itemData, _workbenchProp);
                    if (values.TryGetValue(value, out var count)) values[value] = count + 1;
                    else values[value] = 1;
                }
            }
        }

        private void DrawWorkbenchMatchValues(Dictionary<string, int> values)
        {
            foreach (var kv in values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var line = kv.Key;
                if (kv.Value > 1) line += "  (" + kv.Value + ")";
                ImGui.TextWrapped(line);
            }
        }

        private bool IsPlayerInventoryVisible()
        {
            try
            {
                var inventory = _core.GameController.Game.IngameState.IngameUi.InventoryPanel[InventoryIndex.PlayerInventory];
                return inventory?.InventoryUIElement != null && inventory.InventoryUIElement.IsVisibleLocal;
            }
            catch
            {
                return false;
            }
        }

        private bool IsStashVisible()
        {
            try
            {
                return _core.GameController.Game.IngameState.IngameUi.StashElement?.IsVisibleLocal == true;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<ItemData> GetPlayerMatchItems()
        {
            var slots = _core.Drop.GetPlayerInventory()?.InventorySlotItems;
            if (slots == null) yield break;

            foreach (var s in slots)
            {
                if (s == null || s.Item == null || s.Address == 0) continue;
                var bit = _core.GameController.Files.BaseItemTypes.Translate(s.Item.Path);
                if (bit == null) continue;
                yield return new ItemData(s.Item, bit, s.GetClientRect().Center);
            }
        }

        private IEnumerable<ItemData> GetStashMatchItems()
        {
            var slots = _core.GameController.Game.IngameState.IngameUi.StashElement?.VisibleStash?.VisibleInventoryItems;
            if (slots == null) yield break;

            foreach (var s in slots)
            {
                if (s == null || s.Item == null || s.Address == 0) continue;
                var bit = _core.GameController.Files.BaseItemTypes.Translate(s.Item.Path);
                if (bit == null) continue;
                yield return new ItemData(s.Item, bit, s.GetClientRect().Center);
            }
        }

        private string GetWorkbenchDisplayProperty(string query)
        {
            if (string.IsNullOrEmpty(query)) return null;
            if (query.IndexOf("classname", StringComparison.OrdinalIgnoreCase) >= 0) return "ClassName";
            if (query.IndexOf("basename", StringComparison.OrdinalIgnoreCase) >= 0) return "BaseName";
            if (query.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0) return "Path";
            if (query.IndexOf("visceraslot", StringComparison.OrdinalIgnoreCase) >= 0) return "VisceraSlot";
            if (query.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0) return "Name";
            return null;
        }

        private string GetWorkbenchDisplayValue(ItemData itemData, string prop) =>
            prop != null && ItemPropertyGetters.TryGetValue(prop, out var getter) ? getter(itemData) : itemData.Name;

        internal void DrawHoverInfoOverlay()
        {
            var cell = GetHoveredInventoryCell();
            if (cell.X < 0) return;

            var slot = GetSlotAtCell(cell);
            if (slot == null) return;

            var bit = _core.GameController.Files.BaseItemTypes.Translate(slot.Item.Path);
            if (bit == null) return;

            var itemData = new ItemData(slot, bit, slot.GetClientRect().Center);

            var itemRect = slot.GetClientRect();
            var width = 280f;
            var posX = itemRect.Right + 8;
            if (posX + width > ImGui.GetMainViewport().WorkSize.X)
                posX = itemRect.Left - width - 8;

            ImGui.SetNextWindowPos(new System.Numerics.Vector2(posX, itemRect.Top), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(width, 0), ImGuiCond.Always);
            UiHelpers.PushSettingsStyle();
            try
            {
            if (ImGui.Begin("##hoverInfo", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                                           ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing |
                                           ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoInputs))
            {
                UiHelpers.HouseTitle((itemData.Name ?? "").Replace("%", "%%"));

                DrawInfoRow("BaseName", itemData.BaseName);
                DrawInfoRow("ClassName", itemData.ClassName);
                DrawInfoRow("Path", itemData.Path);

                ImGui.Separator();
                if (_workbenchFilters == null)
                {
                    ImGui.TextDisabled("Enter a valid query to test a match.");
                }
                else
                {
                    var match = _workbenchFilters.FirstOrDefault(f => f.CompareItem(itemData));
                    if (match != null)
                    {
                        ImGui.TextColored(new Vector4(0f, 1f, 0f, 1f), "Match");
                        ImGui.TextWrapped((match.Commands ?? "").Replace("%", "%%"));
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "No match");
                    }
                }
            }
            ImGui.End();
            }
            finally
            {
                UiHelpers.PopSettingsStyle();
            }
        }

        private void DrawInfoRow(string label, string value)
        {
            ImGui.TextColored(UiHelpers.Muted, label + ":");
            ImGui.SameLine();
            ImGui.TextWrapped((value ?? "").Replace("%", "%%"));
        }

        private Point GetHoveredInventoryCell()
        {
            try
            {
                var inventory = _core.GameController.Game.IngameState.IngameUi.InventoryPanel[InventoryIndex.PlayerInventory];
                var el = inventory?.InventoryUIElement;
                if (el == null || !el.IsVisibleLocal) return new Point(-1, -1);

                var rect = el.GetClientRect();
                if (rect.Width <= 0 || rect.Height <= 0) return new Point(-1, -1);

                var mouse = ImGui.GetIO().MousePos;
                var x = (int)((mouse.X - rect.X) / (rect.Width / SquirrelStashCore.InventoryColumns));
                var y = (int)((mouse.Y - rect.Y) / (rect.Height / SquirrelStashCore.InventoryRows));
                if (x < 0 || x >= SquirrelStashCore.InventoryColumns || y < 0 || y >= SquirrelStashCore.InventoryRows) return new Point(-1, -1);

                return new Point(x, y);
            }
            catch
            {
                return new Point(-1, -1);
            }
        }

        private InventSlotItem GetSlotAtCell(Point cell)
        {
            var inventory = _core.Drop.GetPlayerInventory();
            if (inventory?.InventorySlotItems == null) return null;

            foreach (var slot in inventory.InventorySlotItems)
            {
                if (slot == null || slot.Item == null || slot.Item.Address == 0) continue;
                if (cell.X >= slot.PosX && cell.X < slot.PosX + slot.SizeX &&
                    cell.Y >= slot.PosY && cell.Y < slot.PosY + slot.SizeY)
                    return slot;
            }

            return null;
        }

        private EditorOpts GetWorkbenchOpts()
        {
            if (_workbenchOpts == null)
            {
                _workbenchOpts = new EditorOpts
                {
                    Tokenize = SquirrelStashSyntax.Tokenize,
                    Complete = SquirrelStashSyntax.Complete,
                    LineNumbers = true,
                    Size = new System.Numerics.Vector2(0, 240)
                };
            }

            _workbenchOpts.Errors = _workbenchError == null
                ? null
                : new Dictionary<int, string> { [_core.Store.ErrorLineIndex(_workbenchError)] = _workbenchError };

            return _workbenchOpts;
        }

        internal IEnumerable<string> GetSuggestionValues(string prop)
        {
            try
            {
                var inventory = _core.Drop.GetPlayerInventory();
                if (inventory?.InventorySlotItems == null) return Enumerable.Empty<string>();

                var values = new List<string>();
                foreach (var invItem in inventory.InventorySlotItems)
                {
                    if (invItem == null || invItem.Item == null || invItem.Address == 0) continue;
                    var bit = _core.GameController.Files.BaseItemTypes.Translate(invItem.Item.Path);
                    if (bit == null) continue;
                    var itemData = new ItemData(invItem, bit, invItem.GetClientRect().Center);

                    if (!ItemPropertyGetters.TryGetValue(prop, out var getter)) continue;
                    var value = getter(itemData);

                    if (!string.IsNullOrEmpty(value)) values.Add(value);
                }

                return values.Distinct().OrderBy(v => v, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }
    }
}
