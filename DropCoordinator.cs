using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using SharpDX;
using static ExileCore.PoEMemory.MemoryObjects.ServerInventory;
using Vector2 = SharpDX.Vector2;
#pragma warning disable CS0612 // Legacy Input API (SetCursorPos, ForceMousePosition, etc.) is intentional; InputHumanizer migration deferred.

namespace SquirrelStash
{
    /// <summary>
    /// Owns the drop-to-stash coroutine, tab switching/clicking helpers and the per-run shared
    /// drop state (visible-stash index, click window offset, drop items, worker coroutine).
    /// Behavior moved verbatim from SquirrelStashCore.
    /// </summary>
    internal class DropCoordinator
    {
        internal const string CoroutineName = "Drop To Stash";

        private readonly SquirrelStashCore _core;

        internal List<FilterResult> _dropItems;
        internal Coroutine _coroutineWorker;
        internal int _visibleStashIndex = -1;
        internal bool LastSwitchSucceeded;

        public DropCoordinator(SquirrelStashCore core)
        {
            _core = core;
        }

        internal void StartDropItemsToStashCoroutine()
        {
            Core.ParallelRunner.Run(new Coroutine(DropToStashRoutine(), _core, "SquirrelStash_DropItemsToStash"));
        }

        internal void StopCoroutine(string routineName)
        {
            var routine = Core.ParallelRunner.FindByName(routineName);
            routine?.Done();
            CleanUp();
        }

        private IEnumerator DropToStashRoutine()
        {
            var cursorPosPreMoving = Input.ForceMousePosition; //saving cursorposition
            try
            {
                _core.Refills._refillReservedCells.Clear();
                //try stashing remaining items up to MaxRetryAttempts times
                var originTab = GetIndexOfCurrentVisibleTab();
                yield return ParseItems();
                for (int tries = 0; tries < _core.Settings.MaxRetryAttempts.Value && _dropItems.Count > 0; ++tries)
                {
                    if (_dropItems.Count > 0)
                        yield return StashItems();
                    yield return ParseItems();
                    yield return new WaitTime(NextRandomDelay());
                }
                Input.KeyUp(Keys.LControlKey);
                yield return _core.Refills.ProcessRefills();
                switch (_core.Settings.WhenDone.Value)
                {
                    case SquirrelStashSettings.WhenDoneBackToOriginal:
                        yield return SwitchToTab(originTab);
                        break;
                    case SquirrelStashSettings.WhenDoneGoToTab:
                        yield return SwitchToTab(_core.Settings.TabToVisitWhenDone.Value);
                        break;
                }
            }
            finally
            {
                CleanUp();

                //restoring cursorposition
                Input.SetCursorPos(cursorPosPreMoving);
                Input.MouseMove();
                StopCoroutine("SquirrelStash_DropItemsToStash");
            }
        }

        private void CleanUp()
        {
            Input.KeyUp(Keys.LControlKey);
            // Shift is held as Keys.ShiftKey in StashItem/SplitStack; Keys.Shift is a modifier flag
            // and would not release the physical key, leaving the player stuck mid-action.
            Input.KeyUp(Keys.ShiftKey);
        }

        private bool _reqMetCached;
        private long _reqMetTicks;

        internal bool StashingRequirementsMet()
        {
            // Called every frame: the two visibility walks cost ~9KB of Gen0 per
            // call. Town/hideout switches don't need sub-100ms reaction, so cache.
            var now = System.DateTime.UtcNow.Ticks;
            if (now - _reqMetTicks < System.TimeSpan.TicksPerMillisecond * 100) return _reqMetCached;
            _reqMetTicks = now;

            var inventoryPanel = _core.GameController.Game.IngameState.IngameUi.InventoryPanel;
            var stashElement = _core.GameController.Game.IngameState.IngameUi.StashElement;
            _reqMetCached = inventoryPanel != null && inventoryPanel.IsVisible &&
                   stashElement != null && stashElement.IsVisibleLocal;
            return _reqMetCached;
        }

        internal void HandleSwitchToTabEvent(object tab)
        {
            // A drop coroutine and a tab-switch coroutine both mutate _visibleStashIndex and (for the
            // switcher) share the same CoroutineName. Refuse a second switcher while either is running
            // so they can't interleave across yields and so the ProcessSwitchToTab cleanup Done() can't
            // terminate the wrong instance.
            if (Core.ParallelRunner.FindByName(CoroutineName) != null ||
                Core.ParallelRunner.FindByName("SquirrelStash_DropItemsToStash") != null)
            {
                DebugWindow.LogMsg($"{_core.Name}: a stash operation is already in progress, ignoring switch_to_tab event.");
                return;
            }

            Coroutine worker = null;
            switch (tab)
            {
                case int index:
                    worker = new Coroutine(ProcessSwitchToTab(index), _core, CoroutineName);
                    break;
                case string name:
                    if (_core.Tabs.RenamedAllStashNames == null || !_core.Tabs.RenamedAllStashNames.Contains(name))
                    {
                        DebugWindow.LogMsg($"{_core.Name}: cannot find a tab named '{name}'.");
                        return;
                    }

                    var tempIndex = _core.Tabs.GetInventIndexByStashName(name);
                    worker = new Coroutine(ProcessSwitchToTab(tempIndex), _core, CoroutineName);
                    DebugWindow.LogMsg($"{_core.Name}: Switching to tab {tempIndex} ('{name}').");
                    break;
                default:
                    DebugWindow.LogMsg("The received argument is not a string or an integer.");
                    return;
            }

            _coroutineWorker = worker;
            Core.ParallelRunner.Run(_coroutineWorker);
        }

        private IEnumerator ProcessSwitchToTab(int index)
        {
            yield return SwitchToTab(index);
            _coroutineWorker = Core.ParallelRunner.FindByName(CoroutineName);
            _coroutineWorker?.Done();
        }

        internal ServerInventory GetPlayerInventory() =>
            _core.GameController.Game.IngameState.Data.ServerData.PlayerInventories[0].Inventory;

        private IEnumerator ParseItems()
        {
            _dropItems = new List<FilterResult>();
            var inventory = GetPlayerInventory();
            if (inventory?.InventorySlotItems == null) yield break;
            var invItems = inventory.InventorySlotItems;

            var filterSnapshot = _core.Store._activeFilters?.ToArray() ?? Array.Empty<CustomFilter>();
            // Pinned refill cells once per pass: avoids a Translate + refill-list scan per item.
            var refillPins = new HashSet<(int, int, string)>();
            if (_core.Refills._refillEnabled)
            {
                foreach (var r in _core.Refills._customRefills)
                    if (r.Cell.X >= 0) refillPins.Add((r.Cell.X, r.Cell.Y, r.CurrencyClass));
            }

            foreach (var invItem in invItems)
            {
                if (invItem.Item == null || invItem.Address == 0) continue;
                var baseItemType = _core.GameController.Files.BaseItemTypes.Translate(invItem.Item.Path);
                if (baseItemType == null) continue;
                if (CheckIgnoreCells(invItem, baseItemType.BaseName, refillPins)) continue;

                var testItem = new ItemData(invItem, baseItemType, invItem.GetClientRect().Center);
                var result = CheckFilters(testItem, filterSnapshot);
                if (result == null) continue;
                _dropItems.Add(result);
            }
        }

        private bool CheckIgnoreCells(InventSlotItem inventItem, string baseName, HashSet<(int, int, string)> refillPins)
        {
            var inventPosX = inventItem.PosX;
            var inventPosY = inventItem.PosY;

            if (_core.Refills._refillEnabled)
            {
                if (_core.Refills._refillReservedCells.Contains((inventPosX, inventPosY))) return true;
                if (refillPins.Contains((inventPosX, inventPosY, baseName))) return true;
            }

            if (inventPosX < 0 || inventPosX >= SquirrelStashCore.InventoryColumns) return true;

            if (inventPosY < 0 || inventPosY >= SquirrelStashCore.InventoryRows) return true;

            return _core.Settings.IgnoredCells[inventPosY, inventPosX] != 0; //No need to check all item size
        }

        private FilterResult CheckFilters(ItemData itemData, CustomFilter[] filterSnapshot)
        {
            foreach (var filter in filterSnapshot)
            {
                try
                {
                    if (!filter.AllowProcess) continue;

                    if (filter.CompareItem(itemData)) return new FilterResult(filter, itemData);
                }
                catch (Exception ex)
                {
                    DebugWindow.LogError($"Filter check error: {ex}");
                }
            }
            return null;
        }

        private IEnumerator StashItems()
        {
            _core.PublishEvent("stashie_start_drop_items", null);

            _visibleStashIndex = GetIndexOfCurrentVisibleTab();
            if (_visibleStashIndex < 0)
            {
                _core.LogMessage($"SquirrelStash: VisibleStashIndex was invalid: {_visibleStashIndex}, stopping.");
                yield break;
            }
            var itemsSortedByStash = _dropItems.OrderBy(x => x.SkipSwitchTab || x.StashIndex == _visibleStashIndex ? 0 : 1).ThenBy(x => x.StashIndex).ToList();

            _core.LogMessage($"Want to drop {itemsSortedByStash.Count} items.");
            // Window doesn't move mid-run: read its offset once instead of per click.
            var windowOffset = _core.GameController.Window.GetWindowRectangle().TopLeft;
            // Hold ctrl for the whole loop so the plain clicks deposit into the visible tab (fast-stash).
            Input.KeyDown(Keys.LControlKey);
            foreach(var stashresult in itemsSortedByStash)
            {
                //move to correct tab
                if (!stashresult.SkipSwitchTab)
                {
                    yield return SwitchToTab(stashresult.StashIndex);
                    _visibleStashIndex = GetIndexOfCurrentVisibleTab();
                    if (!LastSwitchSucceeded || _visibleStashIndex != stashresult.StashIndex)
                    {
                        _core.LogError(
                            $"SquirrelStash: Skipping deposit of '{stashresult.ItemData?.Name}'. Target tab {stashresult.StashIndex} is not visible (on {_visibleStashIndex}). Item left in inventory.",
                            5);
                        continue;
                    }
                }
                // Ctrl+scroll tab switching releases Ctrl - re-assert it before the deposit click.
                Input.KeyDown(Keys.LControlKey);
                // Wait for the visible stash to become ready (bounded by the timeout setting).
                // Note: StopCode is always true and not a failure signal - the original code just
                // yielded the wait and continued, so we do the same.
                yield return new WaitFunctionTimed(
                    () =>
                    {
                        var stashEl = _core.GameController.Game.IngameState.IngameUi.StashElement;
                        var vs = stashEl?.VisibleStash;
                        return vs != null && vs.IsValid;
                    },
                    true, _core.Settings.TabLoadTimeout.Value, $"Stash on tab {_visibleStashIndex} is not ready.");

                if (!IsVisibleStashInventoryLoaded())
                {
                    _core.LogError(
                        $"SquirrelStash: Skipping deposit of '{stashresult.ItemData?.Name}'. Stash on tab {_visibleStashIndex} did not load in time. Item left in inventory.",
                        5);
                    continue;
                }

                Input.KeyDown(Keys.LControlKey);
                yield return StashItem(stashresult, windowOffset);

                _core.PublishEvent("stashie_finish_drop_items_to_stash_tab", null);
            }
        }

        private bool IsVisibleStashInventoryLoaded()
        {
            try
            {
                var stashElement = _core.GameController.Game.IngameState.IngameUi.StashElement;
                if (stashElement == null || _visibleStashIndex < 0 || _visibleStashIndex >= stashElement.Inventories.Count)
                    return false;
                return stashElement.Inventories[_visibleStashIndex]?.Inventory != null;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerator StashItem(FilterResult stashresult, Vector2 windowOffset)
        {
            var clickPos = stashresult.ClickPos + windowOffset;
            clickPos += new Vector2(Random.Shared.Next(-3, 4), Random.Shared.Next(-3, 4));
            Input.SetCursorPos(clickPos);
            yield return new WaitTime(NextRandomDelay());

            // Atomic Input.Click (down+up in one frame) with Ctrl held for the whole drop loop - the proven deposit mechanism.
            // Shift is held for filters flagged with the 'shifting' command (circumvent affinities).
            bool shiftused = false;
            if (stashresult.ShiftForStashing)
            {
                Input.KeyDown(Keys.ShiftKey);
                shiftused = true;
            }
            Input.Click(MouseButtons.Left);
            if (shiftused)
            {
                Input.KeyUp(Keys.ShiftKey);
            }

            yield return new WaitTime(NextRandomDelay());
        }

        #region Switching between StashTabs

        public IEnumerator SwitchToTab(int tabIndex)
        {
            // We don't want to Switch to a tab that we are already on or that has the magic number for affinities
            if (tabIndex < 0)
            {
                LastSwitchSucceeded = false;
                yield break;
            }

            _visibleStashIndex = GetIndexOfCurrentVisibleTab();
            if (_visibleStashIndex < 0)
            {
                LastSwitchSucceeded = false;
                yield break;
            }

            if (tabIndex == _visibleStashIndex)
            {
                LastSwitchSucceeded = true;
                yield break;
            }

            var mode = _core.Settings.TabSwitchMode?.Value ?? SquirrelStashSettings.TabSwitchClick;
            var useClickFirst = mode == SquirrelStashSettings.TabSwitchClick;

            if (useClickFirst)
            {
                yield return SwitchToTabViaViewAllList(tabIndex);
                _visibleStashIndex = GetIndexOfCurrentVisibleTab();
                if (_visibleStashIndex != tabIndex)
                {
                    _core.LogMessage($"SquirrelStash: Click switch to tab {tabIndex} failed, trying arrow keys.", 5);
                    yield return SwitchToTabViaArrowKeys(tabIndex);
                }
            }
            else
            {
                yield return SwitchToTabViaArrowKeys(tabIndex);
                _visibleStashIndex = GetIndexOfCurrentVisibleTab();
                if (_visibleStashIndex != tabIndex)
                {
                    _core.LogMessage($"SquirrelStash: Arrow-key switch to tab {tabIndex} failed, using the tab list.", 5);
                    yield return SwitchToTabViaViewAllList(tabIndex);
                }
            }

            yield return Delay();
            _visibleStashIndex = GetIndexOfCurrentVisibleTab();
            LastSwitchSucceeded = _visibleStashIndex == tabIndex;
            if (!LastSwitchSucceeded)
                _core.LogError($"SquirrelStash: Failed to switch to tab {tabIndex} (currently on {_visibleStashIndex}).", 5);
            yield return WaitForTabReady(tabIndex);
        }

        private const int MaxArrowTravelDistance = 60;
        private const int ArrowRetryAttempts = 3;
        private const int MaxViewAllScrollAttempts = 12;

        // Arrow-key navigation is the primary tab switch mechanism: coordinate-independent and
        // robust to layout changes (the stash tab strip stays keyboard-navigable in current PoE).
        private IEnumerator SwitchToTabViaArrowKeys(int tabIndex)
        {
            var targetReached = false;
            for (var attempt = 0; attempt < ArrowRetryAttempts && !targetReached; attempt++)
            {
                var travel = tabIndex - GetIndexOfCurrentVisibleTab();
                if (travel == 0)
                {
                    targetReached = true;
                    break;
                }

                if (Math.Abs(travel) > MaxArrowTravelDistance) break;

                var key = travel < 0 ? Keys.Left : Keys.Right;
                for (var i = 0; i < Math.Abs(travel); i++)
                {
                    yield return Input.KeyPress(key);
                    yield return new WaitTime(NextRandomDelay());
                }

                yield return new WaitTime(NextRandomDelay());
                targetReached = GetIndexOfCurrentVisibleTab() == tabIndex;
            }
        }

        // Fallback: click the target tab in the "view all tabs" list. The list is always visible in
        // the current stash UI; if the target label has scrolled out of the visible panel we scroll
        // the list (wheel over the list) until the label is in view, then click it.
        private IEnumerator SwitchToTabViaViewAllList(int tabIndex)
        {
            var stashElement = _core.GameController.Game.IngameState.IngameUi.StashElement;
            if (stashElement == null || tabIndex < 0 || tabIndex >= stashElement.TabListButtons.Count) yield break;

            if (!stashElement.ViewAllStashPanel.IsVisible)
            {
                yield return ClickElement(stashElement.ViewAllStashButton.GetClientRectCache.Center);
                for (var i = 0; i < 20 && !stashElement.ViewAllStashPanel.IsVisible; i++)
                    yield return new WaitTime(50);
            }

            if (!stashElement.ViewAllStashPanel.IsVisible) yield break;

            var panelRect = stashElement.ViewAllStashPanel.GetClientRectCache;
            var label = stashElement.TabListButtons[tabIndex];

            for (var i = 0; i < MaxViewAllScrollAttempts; i++)
            {
                var rect = label.GetClientRectCache;
                if (panelRect.Intersects(rect)) break;
                VerticalScroll(!(rect.Center.Y > panelRect.Center.Y), 4);
                yield return new WaitTime(NextRandomDelay());
            }

            var labelRect = label.GetClientRect();
            if (!panelRect.Intersects(labelRect))
            {
                _core.LogMessage($"SquirrelStash: Tab {tabIndex} label never appeared in the tab list. Click switch aborted.", 5);
                yield break;
            }

            yield return ClickElement(labelRect.Center);
        }

        private IEnumerator WaitForTabReady(int tabIndex)
        {
            yield return new WaitFunctionTimed(
                () =>
                {
                    var stashElement = _core.GameController.Game.IngameState.IngameUi.StashElement;
                    if (stashElement == null || tabIndex < 0 || tabIndex >= stashElement.Inventories.Count) return true;
                    return stashElement.Inventories[tabIndex]?.Inventory != null;
                },
                true, 2000, $"Error while loading tab, Index: {tabIndex}");
        }

        private IEnumerator ClickElement(Vector2 pos, MouseButtons mouseButton = MouseButtons.Left)
        {
            MoveMouseToElement(pos);
            yield return Click(mouseButton);
        }

        private IEnumerator Click(MouseButtons mouseButton = MouseButtons.Left)
        {
            Input.Click(mouseButton);
            yield return Delay();
        }

        private void MoveMouseToElement(Vector2 pos)
        {
            Input.SetCursorPos(pos + _core.GameController.Window.GetWindowRectangle().TopLeft);
        }

        private IEnumerator Delay(int ms = 0)
        {
            yield return new WaitTime(NextRandomDelay() + ms);
        }

        internal int NextRandomDelay()
        {
            var min = Math.Min(_core.Settings.MinDelay.Value, _core.Settings.MaxDelay.Value);
            var max = Math.Max(_core.Settings.MinDelay.Value, _core.Settings.MaxDelay.Value);
            return Random.Shared.Next(min, max + 1);
        }

        internal int GetIndexOfCurrentVisibleTab()
        {
            var stashElement = _core.GameController.Game.IngameState.IngameUi.StashElement;
            return stashElement == null ? -1 : stashElement.IndexVisibleStash;
        }

        #endregion

        #region Stashes update

        private static void VerticalScroll(bool scrollUp, int clicks)
        {
            const int wheelDelta = 120;
            if (scrollUp)
                WinApi.mouse_event(Input.MOUSE_EVENT_WHEEL, 0, 0, clicks * wheelDelta, 0);
            else
                WinApi.mouse_event(Input.MOUSE_EVENT_WHEEL, 0, 0, -(clicks * wheelDelta), 0);
        }

        #endregion
    }
}
