using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ImGuiNET;
using SharpDX;
using UiHelpers = SquirrelUi;
using static ExileCore.PoEMemory.MemoryObjects.ServerInventory;
using Point = SharpDX.Point;
using Vector2 = SharpDX.Vector2;
#pragma warning disable CS0612 // Legacy Input API (SetCursorPos, ForceMousePosition, etc.) is intentional; InputHumanizer migration deferred.

namespace SquirrelStash
{
    /// <summary>
    /// Owns the refill logic (split-stack pulling currency from a source tab up to a target) and
    /// the "Refillable Currency" settings tab UI. Behavior moved verbatim from SquirrelStashCore.
    /// </summary>
    internal class RefillController
    {
        private const int InputDelay = 15;

        private readonly SquirrelStashCore _core;

        internal List<RefillProcessor> _customRefills;
        internal bool _refillEnabled;
        internal bool _refillAddFresh = true;
        internal string _refillSourceTabName = "";
        internal List<string> _stackableCurrencies = new List<string>();
        internal readonly HashSet<(int X, int Y)> _refillReservedCells = new HashSet<(int X, int Y)>();
        internal bool _addRefillOpen;
        internal string _newRefillName = "";
        internal string _newRefillBaseName = "";
        internal string _currencyFilter = "";
        internal int _newRefillTarget = 40;
        internal Point _newRefillCell = new Point(-1, -1);
        internal RefillProcessor _pickCellFor;
        internal Point _pickCellValue = new Point(-1, -1);
        internal bool _pickCellOpen;
        private readonly List<(NormalInventoryItem, string)> _stashStacks = new List<(NormalInventoryItem, string)>();

        public RefillController(SquirrelStashCore core)
        {
            _core = core;
        }

        #region Refill

        internal IEnumerator ProcessRefills()
        {
            if (!_refillEnabled || _customRefills.Count == 0) yield break;

            var sourceTabIndex = _core.Tabs.GetInventIndexByStashName(_refillSourceTabName);
            if (sourceTabIndex < 0)
            {
                _core.LogError("Can't process refills: no source stash tab selected. Set one in Settings > Refills.", 5);
                yield break;
            }

            var delay = (int)_core.GameController.Game.IngameState.ServerData.Latency + _core.Drop.NextRandomDelay();
            var currencyTabVisible = false;
            var inventory = _core.GameController.Game.IngameState.IngameUi.InventoryPanel[InventoryIndex.PlayerInventory];
            if (inventory?.InventoryUIElement == null) yield break;
            var playerItems = _core.Drop.GetPlayerInventory()?.InventorySlotItems;
            if (playerItems == null) yield break;

            _refillReservedCells.Clear();
            _stashStacks.Clear();
            _customRefills.ForEach(x => x.Clear());

            var filledCells = new bool[SquirrelStashCore.InventoryRows, SquirrelStashCore.InventoryColumns];

            foreach (var invItem in playerItems)
            {
                if (invItem == null || invItem.Item == null || invItem.Item.Address == 0) continue;

                var baseC = invItem.Item.GetComponent<Base>();
                var sizeX = baseC?.ItemCellsSizeX ?? 1;
                var sizeY = baseC?.ItemCellsSizeY ?? 1;
                for (var y = 0; y < sizeY; y++)
                for (var x = 0; x < sizeX; x++)
                {
                    var px = invItem.PosX + x;
                    var py = invItem.PosY + y;
                    if (px >= 0 && px < SquirrelStashCore.InventoryColumns && py >= 0 && py < SquirrelStashCore.InventoryRows) filledCells[py, px] = true;
                }
            }

            // Claim the player-inventory stack for each refill: pinned-cell rows take the
            // stack at exactly that cell (processed first), auto rows take the first
            // unclaimed matching stack. Each stack is claimed by at most one refill.
            // Stackability + names resolved once up front, not per (refill x item).
            var stackableItems = new List<(InventSlotItem, string, int)>();
            foreach (var invItem in playerItems)
            {
                if (invItem == null || invItem.Item == null || invItem.Item.Address == 0) continue;
                if (!invItem.Item.HasComponent<ExileCore.PoEMemory.Components.Stack>()) continue;
                var bit = _core.GameController.Files.BaseItemTypes.Translate(invItem.Item.Path);
                if (bit == null) continue;
                stackableItems.Add((invItem, bit.BaseName, invItem.Item.GetComponent<ExileCore.PoEMemory.Components.Stack>().Size));
            }

            var claimedStacks = new HashSet<InventSlotItem>();
            foreach (var refill in _customRefills.OrderBy(r => r.Cell.X >= 0 ? 0 : 1))
            {
                if (refill.OwnedCount > 0) continue;

                InventSlotItem claimed = null;
                var claimedSize = 0;
                foreach (var (invItem, baseName, size) in stackableItems)
                {
                    if (claimedStacks.Contains(invItem)) continue;
                    if (baseName != refill.CurrencyClass) continue;
                    if (refill.Cell.X >= 0 && (invItem.PosX != refill.Cell.X || invItem.PosY != refill.Cell.Y)) continue;

                    claimed = invItem;
                    claimedSize = size;
                    break;
                }

                if (claimed == null) continue;
                claimedStacks.Add(claimed);
                refill.OwnedCount = claimedSize;
                refill.StackSize = claimedSize;
                refill.CurrentCell = new Point(claimed.PosX, claimed.PosY);
                refill.ClickPos = claimed.GetClientRect().Center;
            }

            var cellSize = inventory.InventoryUIElement.GetClientRect().Width / SquirrelStashCore.InventoryColumns;

            foreach (var refill in _customRefills)
            {
                if (refill.OwnedCount >= refill.Target) continue;

                if (!currencyTabVisible)
                {
                    yield return _core.Drop.SwitchToTab(sourceTabIndex);
                    _core.Drop._visibleStashIndex = _core.Drop.GetIndexOfCurrentVisibleTab();

                    if (_core.Drop._visibleStashIndex != sourceTabIndex)
                    {
                        _core.LogError(
                            $"SquirrelStash: Failed to switch to the refill source tab '{_refillSourceTabName}' (index {sourceTabIndex}).", 5);
                        yield break;
                    }

                    currencyTabVisible = true;
                    yield return new WaitTime(delay);

                    // Type the source tab once: our own moves only shrink stacks,
                    // so references stay valid and sizes are re-read live below.
                    var stashItems = _core.GameController.Game.IngameState.IngameUi.StashElement.VisibleStash?.VisibleInventoryItems;
                    if (stashItems != null)
                    {
                        foreach (var source in stashItems)
                        {
                            if (source == null || source.Item == null || source.Item.Address == 0) continue;
                            if (!source.Item.HasComponent<ExileCore.PoEMemory.Components.Stack>()) continue;
                            var sourceBit = _core.GameController.Files.BaseItemTypes.Translate(source.Item.Path);
                            if (sourceBit == null) continue;
                            _stashStacks.Add((source, sourceBit.BaseName));
                        }
                    }
                }

                var moveCount = refill.Target - refill.OwnedCount;

                foreach (var (source, baseName) in _stashStacks)
                {
                    if (moveCount <= 0) break;
                    if (source == null || source.Item == null || source.Item.Address == 0) continue;
                    if (baseName != refill.CurrencyClass) continue;
                    var sourceStack = source.Item.GetComponent<ExileCore.PoEMemory.Components.Stack>();
                    if (sourceStack.Size <= 0) continue;

                    var take = Math.Min(moveCount, sourceStack.Size);
                    Point destCell;
                    Vector2 destination;

                    if (refill.OwnedCount > 0)
                    {
                        destCell = refill.CurrentCell;
                        destination = refill.ClickPos;
                    }
                    else
                    {
                        if (!_refillAddFresh) break;

                        destCell = FindFreeCell(filledCells, refill.Cell);
                        if (destCell.X < 0)
                        {
                            _core.LogMessage($"SquirrelStash: No free inventory cell for {refill.CurrencyClass}.", 5);
                            break;
                        }
                        destination = GetInventoryClickPosByCellIndex(inventory, destCell.X, destCell.Y, cellSize);
                    }

                    yield return SplitStack(take, source.GetClientRect().Center, destination);

                    if (destCell.X >= 0 && destCell.Y >= 0)
                    {
                        filledCells[destCell.Y, destCell.X] = true;
                        _refillReservedCells.Add((destCell.X, destCell.Y));
                    }

                    refill.OwnedCount += take;
                    refill.ClickPos = destination;
                    refill.CurrentCell = destCell;
                    moveCount -= take;
                }

                if (moveCount > 0)
                    _core.LogMessage($"SquirrelStash: Not enough {refill.CurrencyClass} in the source tab (need {moveCount} more).", 5);
            }
        }

        private static Vector2 GetInventoryClickPosByCellIndex(Inventory inventory, int indexX, int indexY,
            float cellSize)
        {
            return inventory.InventoryUIElement.GetClientRect().TopLeft +
                   new Vector2(cellSize * (indexX + 0.5f), cellSize * (indexY + 0.5f));
        }

        private IEnumerator SplitStack(int amount, Vector2 from, Vector2 to)
        {
            var windowOffset = _core.GameController.Window.GetWindowRectangle().TopLeft;
            var delay = (int)_core.GameController.Game.IngameState.ServerData.Latency * 2 + _core.Drop.NextRandomDelay();
            Input.KeyDown(Keys.ShiftKey);

            yield return new WaitFunctionTimed(() => Input.IsKeyDown(Keys.ShiftKey), false, 2000, "SplitStack: shift key not registered");

            yield return Input.SetCursorPositionSmooth(from + windowOffset);
            yield return new WaitTime(_core.Drop.NextRandomDelay());
            Input.Click(MouseButtons.Left);
            Input.MouseMove();
            yield return new WaitTime(InputDelay);
            Input.KeyUp(Keys.ShiftKey);
            yield return new WaitTime(InputDelay + 50);

            if (amount > 40)
            {
                _core.LogMessage("Amount over 40, using 40 (had " + amount + ").", 5);
                amount = 40;
            }

            if (amount < 10)
            {
                var keyToPress = (int) Keys.D0 + amount;
                yield return Input.KeyPress((Keys) keyToPress);
            }
            else
            {
                var keyToPress = (int) Keys.D0 + amount / 10;
                yield return Input.KeyPress((Keys) keyToPress);
                yield return new WaitTime(delay);
                keyToPress = (int) Keys.D0 + amount % 10;
                yield return Input.KeyPress((Keys) keyToPress);
            }

            yield return new WaitTime(delay);
            yield return Input.KeyPress(Keys.Enter);
            yield return new WaitTime(delay + InputDelay);

            yield return Input.SetCursorPositionSmooth(to + windowOffset);
            yield return new WaitTime(_core.Drop.NextRandomDelay());
            Input.Click(MouseButtons.Left);

            yield return new WaitTime(delay + InputDelay);
        }

        internal void DrawRefillsSection()
        {
            UiHelpers.SectionHeader("Refills");

            var refillEnabled = HouseRows.Toggle("Refill currency", _refillEnabled, "refillEnabled",
                "Refills currency from the source stash tab.");
            if (refillEnabled != _refillEnabled) { _refillEnabled = refillEnabled; SaveRefills(); }

            var addFresh = HouseRows.Toggle("Add fresh stack if missing", _refillAddFresh, "refillAddFresh",
                "Creates a new stack when the currency is completely missing.");
            if (addFresh != _refillAddFresh) { _refillAddFresh = addFresh; SaveRefills(); }

            if (_core.Tabs.StashTabNamesByIndex is { Length: > 0 } sourceNames)
            {
                var sourceIndex = _core.Tabs.GetInventIndexByStashName(_refillSourceTabName) + 1;
                sourceIndex = Math.Clamp(sourceIndex, 0, sourceNames.Length - 1);
                if (UiHelpers.SearchableChoice("Source stash tab", ref sourceIndex, sourceNames, "refillSource", 300f) &&
                    sourceIndex >= 0 && sourceIndex < sourceNames.Length &&
                    sourceNames[sourceIndex] != _refillSourceTabName)
                {
                    _refillSourceTabName = sourceNames[sourceIndex];
                    SaveRefills();
                }
            }

            ImGui.Dummy(new System.Numerics.Vector2(0, 6f));

            for (var i = 0; i < _customRefills.Count; i++)
            {
                var refill = _customRefills[i];
                var target = refill.Target;
                var rowStart = ImGui.GetCursorScreenPos();
                float rowH = ImGui.GetTextLineHeight() + 8f;
                float rowW = ImGui.GetContentRegionAvail().X;
                var refillLabel = refill.Name + " (" + refill.CurrencyClass + ")";

                var cellLabel = refill.Cell.X >= 0
                    ? $"Cell {refill.Cell.X + 1},{refill.Cell.Y + 1}" : "Cell auto";
                float cellButtonW = ImGui.CalcTextSize(cellLabel).X + 20f;
                float deleteButtonW = ImGui.CalcTextSize("x").X + 20f;
                float actionWidth = cellButtonW + 8f + deleteButtonW;
                float sliderGutter = actionWidth + 12f;
                float rowAvail = Math.Max(0f, rowW);
                float valueColReserve = ImGui.CalcTextSize("40").X + 12f;
                float maxTrackStart = Math.Max(270f, rowAvail - sliderGutter - valueColReserve - 40f);
                float refillTrackStart = Math.Min(Math.Max(270f, 44f + ImGui.CalcTextSize(refillLabel).X + 12f), maxTrackStart);
                var shownRefillLabel = refillLabel;
                while (shownRefillLabel.Length > 1 && ImGui.CalcTextSize(shownRefillLabel + "..").X > refillTrackStart - 44f - 8f)
                    shownRefillLabel = shownRefillLabel.Substring(0, shownRefillLabel.Length - 1);
                if (shownRefillLabel != refillLabel) shownRefillLabel += "..";

                ImGui.SetCursorScreenPos(rowStart);
                HouseRows.IntSlider(shownRefillLabel, ref target, 1, 40,
                    "refillTarget" + i, null, sliderGutter, false, refillTrackStart);
                if (target != refill.Target) { refill.Target = target; SaveRefills(); }

                ImGui.SetCursorScreenPos(rowStart + new System.Numerics.Vector2(rowW - actionWidth, 0f));
                int refillAction = UiHelpers.ButtonRow(
                    (cellLabel, "refillCell" + i, 0),
                    ("x", "delRefill" + i, 2));
                if (refillAction == 0)
                {
                    _pickCellFor = refill;
                    _pickCellValue = refill.Cell;
                    _pickCellOpen = true;
                }
                else if (refillAction == 1)
                {
                    _customRefills.RemoveAt(i);
                    SaveRefills();
                    i--;
                    continue;
                }
                ImGui.SetCursorScreenPos(new System.Numerics.Vector2(rowStart.X, rowStart.Y + rowH));
            }

            ImGui.Dummy(new System.Numerics.Vector2(0, 6f));
            if (UiHelpers.ActionButton("Add refill", "addRefill", 1))
            {
                _newRefillName = "";
                _newRefillBaseName = "";
                _currencyFilter = "";
                _newRefillTarget = 40;
                _newRefillCell = new Point(-1, -1);
                _addRefillOpen = true;
                ImGui.OpenPopup("Add refill");
            }

            if (_pickCellOpen)
                ImGui.OpenPopup("Pick cell");

            DrawAddRefillPopup();
            DrawPickCellPopup();
        }

        private void DrawAddRefillPopup()
        {
            if (!ImGui.BeginPopupModal("Add refill", ref _addRefillOpen, ImGuiWindowFlags.AlwaysAutoResize)) return;

            UiHelpers.HouseTitle("Add refill");
            ImGui.Text("Name:");
            ImGui.InputText("##newRefillName", ref _newRefillName, 128);

            ImGui.Text("Currency:");
            var preview = string.IsNullOrEmpty(_newRefillBaseName)
                ? "-- pick a currency --"
                : _newRefillBaseName + (_currencyFilter.Length > 0 ? $" (filter: \"{_currencyFilter}\")" : "");
            if (ImGui.BeginCombo("##currencyPick", preview))
            {
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputText("##currencyFilter", ref _currencyFilter, 64);

                var filtered = string.IsNullOrWhiteSpace(_currencyFilter)
                    ? _stackableCurrencies
                    : _stackableCurrencies
                        .Where(c => c.IndexOf(_currencyFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                        .Take(200).ToList();

                if (filtered.Count == 0)
                {
                    ImGui.TextDisabled("No matching currencies.");
                }

                foreach (var currency in filtered)
                {
                    if (ImGui.Selectable(currency, currency == _newRefillBaseName))
                    {
                        _newRefillBaseName = currency;
                        _currencyFilter = "";
                        if (string.IsNullOrWhiteSpace(_newRefillName)) _newRefillName = currency;
                    }
                }
                ImGui.EndCombo();
            }

            var target = _newRefillTarget;
            if (UiHelpers.ModernSlider("Target", ref target, 1, 40, "newRefillTarget"))
                _newRefillTarget = target;

            ImGui.Text("Stash cell (optional):");
            DrawCellGrid(ref _newRefillCell, "addRefillGrid");
            ImGui.TextDisabled(_newRefillCell.X >= 0
                ? $"Selected: ({_newRefillCell.X + 1}, {_newRefillCell.Y + 1})"
                : "Selected: auto");

            var addChoice = UiHelpers.ButtonRow(("Add", "refillAdd", 1), ("Cancel", "refillCancel", 0));
            if (addChoice == 0 && !string.IsNullOrEmpty(_newRefillBaseName))
            {
                _customRefills.Add(new RefillProcessor
                {
                    Name = string.IsNullOrWhiteSpace(_newRefillName) ? _newRefillBaseName : _newRefillName.Trim(),
                    CurrencyClass = _newRefillBaseName,
                    Target = _newRefillTarget,
                    Cell = _newRefillCell
                });
                SaveRefills();
                _addRefillOpen = false;
                ImGui.CloseCurrentPopup();
            }
            else if (addChoice == 1)
            {
                _addRefillOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        private void DrawPickCellPopup()
        {
            if (!ImGui.BeginPopupModal("Pick cell", ref _pickCellOpen, ImGuiWindowFlags.AlwaysAutoResize)) return;

            UiHelpers.HouseTitle("Pick cell");
            if (_pickCellFor != null)
            {
                ImGui.TextUnformatted(_pickCellFor.Name);
                ImGui.TextDisabled("Click the cell where the stack lives, or Auto.");
                DrawCellGrid(ref _pickCellValue, "pickCellGrid");

                var cellChoice = UiHelpers.ButtonRow(
                    ("OK", "cellOK", 1), ("Auto", "cellAuto", 0), ("Cancel", "cellCancel", 0));
                if (cellChoice == 0)
                {
                    _pickCellFor.Cell = _pickCellValue;
                    SaveRefills();
                    _pickCellOpen = false;
                    ImGui.CloseCurrentPopup();
                }
                else if (cellChoice == 1)
                {
                    _pickCellFor.Cell = new Point(-1, -1);
                    SaveRefills();
                    _pickCellOpen = false;
                    ImGui.CloseCurrentPopup();
                }
                else if (cellChoice == 2)
                {
                    _pickCellOpen = false;
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndPopup();
        }

        private void DrawCellGrid(ref Point cell, string id)
        {
            var gridStart = ImGui.GetCursorScreenPos();
            const float cellSize = 30f;
            const float cellGap = 2f;
            float cellStep = cellSize + cellGap;

            for (var y = 0; y < SquirrelStashCore.InventoryRows; y++)
            {
                for (var x = 0; x < SquirrelStashCore.InventoryColumns; x++)
                {
                    var selected = cell.X == x && cell.Y == y;
                    ImGui.SetCursorScreenPos(gridStart + new System.Numerics.Vector2(x * cellStep, y * cellStep));
                    if (HouseRows.GridCell(selected, $"{id}_{x}_{y}",
                            $"Cell {x + 1},{y + 1}", cellSize))
                        cell = new Point(x, y);
                }
            }
            ImGui.SetCursorScreenPos(gridStart + new System.Numerics.Vector2(
                0f, SquirrelStashCore.InventoryRows * cellStep - cellGap));
        }

        private static Point FindFreeCell(bool[,] filled, Point preferred)
        {
            if (preferred.X >= 0 && preferred.Y >= 0 &&
                preferred.X < SquirrelStashCore.InventoryColumns && preferred.Y < SquirrelStashCore.InventoryRows &&
                !filled[preferred.Y, preferred.X]) return preferred;
            for (var y = 0; y < SquirrelStashCore.InventoryRows; y++)
            for (var x = 0; x < SquirrelStashCore.InventoryColumns; x++)
                if (!filled[y, x]) return new Point(x, y);
            return new Point(-1, -1);
        }

        internal void LoadCustomRefills()
        {
            try
            {
                var refillsPath = Path.Combine(_core.ConfigDirectory, "Refills.txt");
                if (!File.Exists(refillsPath))
                {
                    RefillFile.SeedFromLegacy(Path.Combine(_core.DirectoryFullName, "RefillCurrency.txt"), refillsPath);
                    _core.LogMessage($"SquirrelStash: Created refills config at {refillsPath}.", 3);
                }

                _customRefills = RefillFile.Parse(refillsPath);
                RefillFile.ParseSettings(refillsPath, out _refillEnabled, out _refillAddFresh, out _refillSourceTabName);
            }
            catch (Exception e)
            {
                _customRefills ??= new List<RefillProcessor>();
                _core.LogError($"SquirrelStash: Failed to load refills config: {e}", 5);
            }

            RefreshStackableCurrencies();
        }

        internal void SaveRefills()
        {
            try
            {
                var path = Path.Combine(_core.ConfigDirectory, "Refills.txt");
                if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                RefillFile.Save(path, _customRefills, _refillEnabled, _refillAddFresh, _refillSourceTabName);
            }
            catch (Exception e)
            {
                _core.LogError($"SquirrelStash: Failed to save refills: {e}", 5);
            }
        }

        private void RefreshStackableCurrencies()
        {
            try
            {
                _stackableCurrencies = _core.GameController.Files.BaseItemTypes.Contents.Values
                    .Where(x => x != null && x.ClassName == "StackableCurrency" && !string.IsNullOrEmpty(x.BaseName))
                    .Select(x => x.BaseName).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception e)
            {
                _core.LogError($"SquirrelStash: Failed to enumerate stackable currencies: {e}");
            }
        }

        #endregion
    }
}
