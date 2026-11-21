using System;
using System.Diagnostics;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ImGuiNET;
using SharpDX;
using UiHelpers = SquirrelUi;
#pragma warning disable CS0612 // Legacy Input API (SetCursorPos, ForceMousePosition, etc.) is intentional; InputHumanizer migration deferred.

namespace SquirrelStash
{
    public class SquirrelStashCore : BaseSettingsPlugin<SquirrelStashSettings>
    {
        internal const int InventoryColumns = 12;
        internal const int InventoryRows = 5;
        private const int WhileDelay = 5;

        private readonly Stopwatch _stackItemTimer = new Stopwatch();
        private int _settingsTab;

        // Collaborators - constructed in Initialise. Accessed cross-collaborator via these.
        internal StashTabNameTracker Tabs;
        internal FilterProfileStore Store;
        internal RefillController Refills;
        internal WorkbenchController Workbench;
        internal DropCoordinator Drop;

        public SquirrelStashCore()
        {
            Name = "SquirrelStash";
        }

        public override void ReceiveEvent(string eventId, object args)
        {
            if (!Settings.Enable.Value)
            {
                return;
            }

            switch (eventId)
            {
                case "switch_to_tab":
                    Drop.HandleSwitchToTabEvent(args);
                    break;
                default:
                    break;
            }
        }

        public override bool Initialise()
        {
            Tabs = new StashTabNameTracker(this);
            Store = new FilterProfileStore(this);
            Refills = new RefillController(this);
            Workbench = new WorkbenchController(this);
            Drop = new DropCoordinator(this);

            Settings.Enable.OnValueChanged += (sender, b) =>
            {
                if (b)
                {
                    if (Core.ParallelRunner.FindByName(StashTabNameTracker.CoroutineName) == null) Tabs.InitStashTabNameCoRoutine();
                    Tabs.StashTabNamesCoroutine?.Resume();
                }
                else
                {
                    Tabs.StashTabNamesCoroutine?.Pause();
                }

                SetupOrClose();
            };

            Tabs.InitStashTabNameCoRoutine();
            SetupOrClose();

            Input.RegisterKey(Settings.DropHotkey.Value);

            Settings.DropHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.DropHotkey.Value); };
            Settings.SwitchFilterhotkey.OnValueChanged += () => { Input.RegisterKey(Settings.SwitchFilterhotkey.Value); };
            Input.RegisterKey(Settings.TestFilterHotkey.Value);
            Settings.TestFilterHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.TestFilterHotkey.Value); };
            SquirrelStashSyntax.ValueProvider = Workbench.GetSuggestionValues;

            return true;
        }

        public override void AreaChange(AreaInstance area)
        {
            if (Tabs?.StashTabNamesCoroutine == null) return;
            var isStashArea = area.IsHideout || area.IsTown ||
                               area.DisplayName.IndexOf("Azurite Mine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               area.DisplayName.IndexOf("Tane's Laboratory", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isStashArea)
            {
                if (Core.ParallelRunner.FindByName("SquirrelStash_DropItemsToStash") != null)
                    Drop.StopCoroutine("SquirrelStash_DropItemsToStash");
            }

            if (Tabs.StashTabNamesCoroutine.Running)
            {
                if (!isStashArea)
                    Tabs.StashTabNamesCoroutine?.Pause();
            }
            else
            {
                if (isStashArea)
                    Tabs.StashTabNamesCoroutine?.Resume();
            }
        }

        // Collaborators are built in Initialise, but the settings window can open
        // while the plugin is disabled (Initialise hasn't run). Build them on
        // demand so DrawSettings never touches a null holder.
        private void EnsureCollaborators()
        {
            Tabs ??= new StashTabNameTracker(this);
            Store ??= new FilterProfileStore(this);
            Refills ??= new RefillController(this);
            Workbench ??= new WorkbenchController(this);
            Drop ??= new DropCoordinator(this);
        }

        internal void SetupOrClose()
        {
            EnsureCollaborators();
            MigrateLegacyConfig();
            Store.SaveDefaultConfigsToDisk();
            Refills.LoadCustomRefills();
            Store.LoadCustomFilters();

            try
            {
                Settings.TabToVisitWhenDone.Max =
                    (int) GameController.Game.IngameState.IngameUi.StashElement.TotalStashes - 1;
                var names = Tabs.GetStashNames();
                Tabs.UpdateStashNames(names);
            }
            catch (Exception e)
            {
                LogError($"Could not read stash tab names on init. {e}");
            }
        }

        // One-time move from the old "Stashie" config dir (plugin was renamed to
        // SquirrelStash; ExileCore keys ConfigDirectory off the plugin name).
        private void MigrateLegacyConfig()
        {
            try
            {
                var parent = System.IO.Directory.GetParent(ConfigDirectory)?.FullName;
                if (parent == null) return;
                var legacy = System.IO.Path.Combine(parent, "Stashie");
                if (!System.IO.Directory.Exists(legacy) ||
                    string.Equals(legacy, ConfigDirectory, System.StringComparison.OrdinalIgnoreCase)) return;

                System.IO.Directory.CreateDirectory(ConfigDirectory);
                foreach (var file in System.IO.Directory.GetFiles(legacy, "*.txt"))
                {
                    var dest = System.IO.Path.Combine(ConfigDirectory, System.IO.Path.GetFileName(file));
                    if (!System.IO.File.Exists(dest)) System.IO.File.Copy(file, dest);
                }

                var legacyProfiles = System.IO.Path.Combine(legacy, "Profiles");
                if (System.IO.Directory.Exists(legacyProfiles))
                {
                    var newProfiles = System.IO.Path.Combine(ConfigDirectory, "Profiles");
                    System.IO.Directory.CreateDirectory(newProfiles);
                    foreach (var file in System.IO.Directory.GetFiles(legacyProfiles, "*.txt"))
                    {
                        var dest = System.IO.Path.Combine(newProfiles, System.IO.Path.GetFileName(file));
                        if (!System.IO.File.Exists(dest)) System.IO.File.Copy(file, dest);
                    }
                }

                LogMessage("SquirrelStash: Migrated config from the old Stashie folder.", 3);
            }
            catch (Exception e)
            {
                LogError($"SquirrelStash: Config migration failed: {e}");
            }
        }

        public override void DrawSettings()
        {
            UiHelpers.PushSettingsStyle();
            try
            {
                EnsureCollaborators();
                if (ImGui.BeginChild("##stSide", new System.Numerics.Vector2(140, 0), ImGuiChildFlags.None, ImGuiWindowFlags.None))
                {
                    SettingsTab("Filters", 0);
                    SettingsTab("Refills", 1);
                    SettingsTab("Ignored cells", 2);
                    SettingsTab("Settings", 3);
                }
                ImGui.EndChild();
                ImGui.SameLine();
                if (ImGui.BeginChild("##stMain", new System.Numerics.Vector2(0, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.None))
                {
                    var ver = GetType().Assembly.GetName().Version?.ToString(2);
                    UiHelpers.HouseHeader("SquirrelStash", ver);
                    DrawReloadConfigButton();
                    switch (_settingsTab)
                    {
                        case 0: Store.DrawFiltersTab(); break;
                        case 1: Refills.DrawRefillsSection(); break;
                        case 2: DrawIgnoredCellsSettings(); break;
                        case 3: DrawSquirrelStashSettings(); break;
                    }
                }
                ImGui.EndChild();
            }
            finally
            {
                UiHelpers.PopSettingsStyle();
            }
        }

        private void SettingsTab(string label, int id)
        {
            if (UiHelpers.SidebarRow(label, null, _settingsTab == id, "sttab" + id, 0)) _settingsTab = id;
        }

        private void DrawSquirrelStashSettings()
        {
            UiHelpers.SectionHeader("Plugin");
            var enabled = HouseRows.Toggle("Enable", Settings.Enable.Value, "enable",
                "Turns the plugin on or off.");
            if (enabled != Settings.Enable.Value) Settings.Enable.Value = enabled;

            UiHelpers.SectionHeader("Hotkeys");
            HouseRows.Hotkey(Settings.DropHotkey, "Stash hotkey", "dropKey",
                "Drops items into stash tabs.");
            HouseRows.Hotkey(Settings.SwitchFilterhotkey, "Switch filter hotkey", "switchFilterKey",
                "Cycles to the next filter profile.");
            HouseRows.Hotkey(Settings.TestFilterHotkey, "Test filter hotkey", "testFilterKey",
                "Opens the filter workbench.");

            UiHelpers.SectionHeader("Timing");
            HouseRows.NodeSlider(Settings.MinDelay, "Min delay", "minDelay",
                "Shortest pause between actions, in milliseconds.");
            HouseRows.NodeSlider(Settings.MaxDelay, "Max delay", "maxDelay",
                "Longest pause between actions, in milliseconds.");
            HouseRows.NodeSlider(Settings.TabLoadTimeout, "Tab load timeout", "tabTimeout",
                "How long to wait for a stash tab to load.");

            UiHelpers.SectionHeader("Behavior");
            var hoverInfo = HouseRows.Toggle("Hover info", Settings.HoverInfo.Value, "hoverInfo",
                "Shows item details while the workbench is open.");
            if (hoverInfo != Settings.HoverInfo.Value) Settings.HoverInfo.Value = hoverInfo;
            HouseRows.NodeSlider(Settings.MaxRetryAttempts, "Max retry attempts", "maxRetries",
                "How many stash passes to run for failed items.");
            HouseRows.ListChoice(Settings.WhenDone, "When done", "whenDone",
                "What happens after all items are dropped.");
            HouseRows.NodeSlider(Settings.TabToVisitWhenDone, "Go to tab (index)", "goToTab",
                "First tab is 0.",
                Settings.WhenDone.Value != SquirrelStashSettings.WhenDoneGoToTab);
            HouseRows.ListChoice(Settings.TabSwitchMode, "Tab switch mode", "tabSwitchMode",
                "How stash tabs are switched.");
        }

        public override void Render()
        {
            if (Settings.TestFilterHotkey.PressedOnce())
            {
                Workbench._workbenchOpen = !Workbench._workbenchOpen;
                if (Workbench._workbenchOpen) Workbench.RefreshWorkbenchMatches();
            }

            if (Workbench._workbenchOpen)
                Workbench.DrawWorkbench();

            if (Workbench._workbenchOpen && Settings.HoverInfo.Value)
                Workbench.DrawHoverInfoOverlay();
        }

        public override Job Tick()
        {
            if (Settings.SwitchFilterhotkey.PressedOnce())
            {
                var profiles = Store.GetProfiles();
                var active = string.IsNullOrEmpty(Settings.ActiveProfile) ? FilterProfileStore.DefaultProfileName : Settings.ActiveProfile;
                var index = profiles.IndexOf(active);
                if (index < 0) index = 0;
                Store.SwitchProfile(profiles[(index + 1) % profiles.Count]);
            }
            if (!Drop.StashingRequirementsMet() && Core.ParallelRunner.FindByName("SquirrelStash_DropItemsToStash") != null)
            {
                Drop.StopCoroutine("SquirrelStash_DropItemsToStash");
                return null;
            }

            if (Settings.DropHotkey.PressedOnce())
            {
                if (Core.ParallelRunner.FindByName("SquirrelStash_DropItemsToStash") == null)
                {
                    Drop.StartDropItemsToStashCoroutine();
                }
                else
                {
                    Drop.StopCoroutine("SquirrelStash_DropItemsToStash");
                }
            }
            return null;
        }

        public void SaveIgnoredSlotsFromInventoryTemplate()
        {
            Settings.IgnoredCells = new[,]
            {
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
            };
            try
            {
                var serverInventory = GameController.IngameState.Data.ServerData.PlayerInventories[0];

                foreach (var item in serverInventory.Inventory.InventorySlotItems)
                {
                    var baseC = item.Item.GetComponent<Base>();
                    var itemSizeX = baseC.ItemCellsSizeX;
                    var itemSizeY = baseC.ItemCellsSizeY;
                    var inventPosX = item.PosX;
                    var inventPosY = item.PosY;
                    for (var y = 0; y < itemSizeY; y++)
                    for (var x = 0; x < itemSizeX; x++)
                    {
                        var cellY = y + inventPosY;
                        var cellX = x + inventPosX;
                        if (cellX < 0 || cellX >= InventoryColumns || cellY < 0 || cellY >= InventoryRows)
                            continue;
                        Settings.IgnoredCells[cellY, cellX] = 1;
                    }
                }
            }
            catch (Exception e)
            {
                LogError($"{e}", 5);
            }
        }

        private void DrawReloadConfigButton()
        {
            if (HouseRows.ActionButton("Reload config", "stReload", 0,
                    "Reloads the filter and stash tab config from disk."))
            {
                SetupOrClose();
                DebugWindow.LogMsg("Reloaded SquirrelStash config", 2, Color.LimeGreen);
            }
        }

        private void DrawIgnoredCellsSettings()
        {
            try
            {
                if (HouseRows.ActionButton("Copy inventory", "stCopyInv", 0,
                        "Fills the grid from your current inventory layout."))
                    SaveIgnoredSlotsFromInventoryTemplate();

                ImGui.TextColored(UiHelpers.Muted, "Checked cells are ignored. Unchecked cells are processed.");
            }
            catch (Exception e)
            {
                DebugWindow.LogError(e.ToString(), 10);
            }

            var gridStart = ImGui.GetCursorScreenPos();
            const float ignoredCellSize = 30f;
            const float ignoredCellGap = 2f;
            float ignoredStep = ignoredCellSize + ignoredCellGap;

            for (var i = 0; i < InventoryRows; i++)
            for (var j = 0; j < InventoryColumns; j++)
            {
                var toggled = Convert.ToBoolean(Settings.IgnoredCells[i, j]);
                ImGui.SetCursorScreenPos(gridStart + new System.Numerics.Vector2(j * ignoredStep, i * ignoredStep));
                if (HouseRows.GridCell(toggled, $"ignored_{i}_{j}",
                        $"Cell {j + 1},{i + 1}" + (toggled ? " (ignored - click to process)" : " (click to ignore)"),
                        ignoredCellSize))
                    Settings.IgnoredCells[i, j] ^= 1;
            }
            ImGui.SetCursorScreenPos(gridStart + new System.Numerics.Vector2(
                0f, InventoryRows * ignoredStep - ignoredCellGap));
        }

        public override void OnClose()
        {
        }

        public override void OnUnload()
        {
            Drop?.StopCoroutine("SquirrelStash_DropItemsToStash");
            Core.ParallelRunner.FindByName(DropCoordinator.CoroutineName)?.Done();

            if (Tabs?.StashTabNamesCoroutine != null)
            {
                Tabs.StashTabNamesCoroutine.Done();
                Tabs.StashTabNamesCoroutine = null;
            }
        }
    }
}
