using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Nodes;
using static ExileCore.PoEMemory.MemoryObjects.ServerInventory;

namespace SquirrelStash
{
    /// <summary>
    /// Owns the stash-tab-name tracking coroutine and the canonical renamed tab-name list and
    /// index-by-name lookup. Behavior moved verbatim from SquirrelStashCore.
    /// </summary>
    internal class StashTabNameTracker
    {
        internal const string CoroutineName = "Stash Tabs Name Checker";
        private const int MinStashTabCount = 4;

        private static readonly WaitTime Wait2Sec = new WaitTime(2000);
        private static readonly WaitTime Wait1Sec = new WaitTime(1000);

        private readonly SquirrelStashCore _core;

        // Eager-initialised so reads before the first successful UpdateStashNames (e.g. an early
        // switch_to_tab event or a refill lookup) don't NullReferenceException.
        internal List<string> RenamedAllStashNames = new List<string> { "Ignore" };
        internal List<string> LastRealNames;
        internal string[] StashTabNamesByIndex;
        internal Coroutine StashTabNamesCoroutine;

        public StashTabNameTracker(SquirrelStashCore core)
        {
            _core = core;
        }

        internal void InitStashTabNameCoRoutine()
        {
            Core.ParallelRunner.FindByName(CoroutineName)?.Done();
            StashTabNamesCoroutine = new Coroutine(StashTabNamesUpdater_Thread(), _core, CoroutineName);
            Core.ParallelRunner.Run(StashTabNamesCoroutine);
        }

        internal int GetInventIndexByStashName(string name)
        {
            if (RenamedAllStashNames == null) return -1;
            var index = RenamedAllStashNames.IndexOf(name);
            if (index != -1) index--;

            return index;
        }

        internal List<string> GetStashNames()
        {
            try
            {
                return _core.GameController.Game.IngameState.IngameUi.StashElement.Inventories
                    .Where(i => i != null)
                    .Select(i => i.TabName)
                    .Where(n => n != null)
                    .ToList();
            }
            catch (Exception e)
            {
                _core.LogError($"SquirrelStash: Could not read stash tab names: {e}");
                return new List<string>();
            }
        }

        internal void UpdateStashNames(ICollection<string> newNames)
        {
            if (newNames.Count < MinStashTabCount)
            {
                _core.LogError("Could not parse stash tab names.");
                return;
            }

            RenamedAllStashNames = new List<string> {"Ignore"};

            var nameCounts = new Dictionary<string, int>();
            foreach (var name in newNames)
            {
                var realStashName = name;

                if (RenamedAllStashNames.Contains(realStashName))
                {
                    var count = nameCounts.TryGetValue(name, out var c) ? c : 1;
                    string candidate;
                    do
                    {
                        candidate = $"{realStashName} ({count++})";
                    } while (RenamedAllStashNames.Contains(candidate));
                    realStashName = candidate;
                    nameCounts[name] = count;
#if DebugMode
                    _core.LogMessage("SquirrelStash: Renamed a duplicate stash tab to: " + realStashName, 3);
#endif
                }

                RenamedAllStashNames.Add(realStashName);
            }

            foreach (var lOption in _core.Settings.CustomFilterOptions.Values.ToList())
                try
                {
                    lOption.SetListValues(RenamedAllStashNames);
                    var inventoryIndex = GetInventIndexByStashName(lOption.Value);

                    if (inventoryIndex == -1) //If the value doesn't exist in list (renamed)
                    {
                        if (lOption.Index != -1) //If the value doesn't exist in list and the value was not Ignore
                        {
#if DebugMode
                        _core.LogMessage("Tab renamed: " + lOption.Value + " to " + RenamedAllStashNames[lOption.Index + 1],
                            5);
#endif
                            if (lOption.Index + 1 >= RenamedAllStashNames.Count)
                            {
                                lOption.Index = -1;
                                lOption.Value = RenamedAllStashNames[0];
                            }
                            else
                            {
                                lOption.Value = RenamedAllStashNames[lOption.Index + 1]; //    Just update it's name
                            }
                        }
                        else
                        {
                            lOption.Value =
                                RenamedAllStashNames[0]; //Actually it was "Ignore", we just update it (can be removed)
                        }
                    }
                    else //tab just change it's index
                    {
#if DebugMode
                    if (lOption.Index != inventoryIndex)
                    {
                        _core.LogMessage("Tab moved: " + lOption.Index + " to " + inventoryIndex, 5);
                    }
#endif
                        lOption.Index = inventoryIndex;
                        lOption.Value = RenamedAllStashNames[inventoryIndex + 1];
                    }
                }
                catch (Exception e)
                {
                    DebugWindow.LogError($"UpdateStashNames CustomFilterOptions {e}");
                }

            LastRealNames = newNames.ToList();
            _core.Store.GenerateMenu();
        }

        public IEnumerator StashTabNamesUpdater_Thread()
        {
            while (true)
            {
                while (!_core.GameController.Game.IngameState.InGame) yield return Wait2Sec;

                var stashPanel = _core.GameController.Game.IngameState?.IngameUi?.StashElement;

                while (stashPanel == null || !stashPanel.IsVisibleLocal) yield return Wait1Sec;

                var realNames = GetStashNames();

                if (realNames.Count < MinStashTabCount)
                {
                    _core.LogError("Could not parse stash tab names.");
                    yield return Wait1Sec;
                    continue;
                }

                if (LastRealNames == null || realNames.Count != LastRealNames.Count ||
                    !realNames.SequenceEqual(LastRealNames))
                {
                    UpdateStashNames(realNames);
                    continue;
                }

                yield return Wait1Sec;
            }
        }
    }
}
