using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExileCore;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Vector4 = System.Numerics.Vector4;
using UiHelpers = SquirrelUi;

namespace SquirrelStash
{
    /// <summary>
    /// Owns the filter/profile file IO, config seeding, the in-memory filter list and the
    /// "Filters" settings tab UI (filter list, query editor, profile management, category ops).
    /// Behavior moved verbatim from SquirrelStashCore.
    /// </summary>
    internal class FilterProfileStore
    {
        internal const string FiltersConfigFilePrimary = "FiltersConfig.txt";
        internal const string DefaultProfileName = "Default";

        internal static readonly HashSet<string> DefaultFilterNames = new HashSet<string>
        {
            // Kept original 24 (Jewels -> Regular Jewels, AnyInfluence + Quality Gems removed)
            "Currency", "Divination Cards", "Essences", "Fragments", "Maps", "Fossils/Resonators", "Gems",
            "6-Socket", "Prophecies", "Veiled", "Legion-Incubators",
            "Delirium-Splinter", "Delirium-Simulacrum", "Blight-AnnointOils", "Sorcerer Boots",
            "Leather Belt", "Chisel Recipe", "Quality Flasks", "Weapons", "Jewelry",
            "Belts", "Helms", "Body Armours", "Boots", "Gloves",
            // Curated additions now shipped as defaults
            "Blight Annointed Items", "Convoking Wands [ilvl86]",
            "Stacked Decks", "Tattoos", "Omens", "Catalysts", "Allflame & Coffins", "Delirium Orbs", "Incubators All",
            "Invitations", "Tomes", "Relics",
            "Contracts", "Blueprints", "Heist Equipment", "Heist Trinket", "Heist Objective",
            "Abyss Items", "Tincture Flasks", "Charms", "Talismans",
            "Influenced Shaper", "Influenced Elder", "Influenced Hunter", "Influenced Warlord", "Influenced Redeemer", "Influenced Crusader",
            "High ilvl Body Armours", "High ilvl Helmets", "High ilvl Weapons",
            "Uniques", "Flasks All", "Skill Gems High", "Skill Gems Quality Low",
            "Small Cluster Jewels", "Medium Cluster Jewels", "Large Cluster Jewels", "Abyss Jewels", "Regular Jewels",
            "6 Link", "Left Rares", "Left Magics", "Left Rare Rings", "Left Rare Amulets",
            "Wombgifts All", "Wombgifts 75+"
        };

        private readonly SquirrelStashCore _core;

        internal List<CustomFilter> _activeFilters;
        internal List<string> _parseErrors = new List<string>();
        internal CustomFilter _selectedFilter;
        internal CustomFilter _queryEditFilter;
        internal string _queryEditBuffer;
        internal string _queryEditError;
        internal bool _queryDirty;
        internal string _pendingDeleteProfile;
        internal bool _deleteProfileOpen;
        internal bool _queryEditorOpen;
        internal EditorOpts _queryEditorOpts;
        internal bool _addFilterOpen;
        internal string _newFilterName;
        internal string _newFilterCategory;
        internal bool _renameCategoryOpen;
        internal string _renameCategoryOldName;
        internal string _renameCategoryNewName;
        internal bool _deleteCategoryOpen;
        internal string _deleteCategoryName;
        internal CustomFilter _quickPickFor;
        internal bool _quickPickOpen;
        internal string _quickPickFilter = "";
        internal bool _quickPickFocus;
        internal Action _filterTabs;

        public FilterProfileStore(SquirrelStashCore core)
        {
            _core = core;
        }

        /// <summary>
        /// Creates a new file and adds the content to it if the file doesn't exists.
        /// If the file already exists, then no action is taken.
        /// </summary>
        /// <param name="path">The path to the file on disk</param>
        /// <param name="content">The content it should contain</param>
        private static void WriteToNonExistentFile(string path, string content)
        {
            if (File.Exists(path)) return;

            using (var streamWriter = new StreamWriter(path, true))
            {
                streamWriter.Write(content);
                streamWriter.Close();
            }
        }

        internal void SaveDefaultConfigsToDisk()
        {
            var path = Path.Combine(_core.DirectoryFullName, "GitUpdateConfig.txt");
            const string gitUpdateConfig = "Owner:SquirrelRat\r\n" + "Name:SquirrelStash\r\n" + "Release\r\n";
            WriteToNonExistentFile(path, gitUpdateConfig);
        }

        private static string GetDefaultFilterConfig()
        {
            const string filtersConfig =

                #region default config String

                "//FilterName(menu name):\tfilters\t\t:ParentMenu(optionally, will be created automatically for grouping)\r\n" +
                "//Filter parts are separated by commas, | means OR (any part can pass)\r\n" +
                "\r\n" +
                "////////////\tAvailable properties:\t/////////////////////\r\n" +
                "/////////\tString (name) properties:\r\n" +
                "//classname\r\n" +
                "//basename\r\n" +
                "//path\r\n" +
                "//visceraslot (Wickerman Viscera: arm, head, leg, body)\r\n" +
                "/////////\tNumerical properties:\r\n" +
                "//itemquality\r\n" +
                "//rarity\r\n" +
                "//ilvl\r\n" +
                "//tier\r\n" +
                "//numberofsockets\r\n" +
                "//numberoflinks\r\n" +
                "//veiled\r\n" +
                "//fractured\r\n" +
                "/////////\tBoolean properties:\r\n" +
                "//identified\r\n" +
                "//fractured\r\n" +
                "//corrupted\r\n" +
                "//influenced\r\n" +
                "//Elder\r\n" +
                "//Shaper\r\n" +
                "//Crusader\r\n" +
                "//Hunter\r\n" +
                "//Redeemer\r\n" +
                "//Warlord\r\n" +
                "//blightedMap\r\n" +
                "//elderGuardianMap\r\n" +
                "/////////////////////////////////////////////////////////////\r\n" +
                "////////////\tAvailable operations:\t/////////////////////\r\n" +
                "/////////\tString (name) operations:\r\n" +
                "//!=\t(not equal)\r\n" +
                "//=\t\t(equal)\r\n" +
                "//^\t\t(contains)\r\n" +
                "//!^\t(not contains)\r\n" +
                "/////////\tNumerical operations:\r\n" +
                "//!=\t(not equal)\r\n" +
                "//=\t\t(equal)\r\n" +
                "//>\t\t(bigger)\r\n" +
                "//<\t\t(less)\r\n" +
                "//<=\t(less or equal)\r\n" +
                "//>=\t(greater or equal)\r\n" +
                "/////////\tBoolean operations:\r\n" +
                "//!\t\t(not/invert)\r\n" +
                "/////////////////////////////////////////////////////////////\r\n" +
                "\r\n" +
                "//Default Tabs\r\n" +
                "Currency:ClassName=StackableCurrency,path!^Essence,BaseName!^Remnant,path!^CurrencyDelveCrafting,BaseName!^Splinter,Path!^CurrencyItemisedProphecy,Path!^CurrencyAfflictionOrb,Path!^Mushrune:Default Tabs\r\n" +
                "Divination Cards:ClassName=DivinationCard:Default Tabs\r\n" +
                "Essences:BaseName^Essence|BaseName^Remnant,ClassName=StackableCurrency:Default Tabs\r\n" +
                "Fragments:ClassName=MapFragment|BaseName^Splinter,ClassName=StackableCurrency|ClassName=LabyrinthMapItem|BaseName^Scarab:Default Tabs\r\n" +
                "Maps:ClassName=MapKey,!blightedMap:Default Tabs\r\n" +
                "Fossils/Resonators:path^CurrencyDelveCrafting | path^DelveStackableSocketableCurrency:Default Tabs\r\n" +
                "Gems:ClassName^Skill Gem,ItemQuality=0:Default Tabs\r\n" +
                "6-Socket:numberofsockets=6,numberoflinks!=6:Default Tabs\r\n" +
                "Prophecies:Path^CurrencyItemisedProphecy:Default Tabs\r\n" +
                "Veiled:Veiled>0:Special items\r\n" +
                "Legion-Incubators:path^CurrencyIncubation:League Items\r\n" +
                "Delirium-Splinter:path^CurrencyAfflictionShard:League Items\r\n" +
                "Delirium-Simulacrum:path^CurrencyAfflictionFragment:League Items\r\n" +
                "Blight-AnnointOils:path^Mushrune:League Items\r\n" +
                "Sorcerer Boots:BaseName=Sorcerer Boots,Rarity=Normal:Chance Items\r\n" +
                "Leather Belt:BaseName=Leather Belt,Rarity=Normal:Chance Items\r\n" +
                "Chisel Recipe:BaseName=Stone Hammer|BaseName=Rock Breaker,ItemQuality=20:Vendor Recipes\r\n" +
                "Quality Flasks:ClassName^Flask,ItemQuality>0:Vendor Recipes\r\n" +
                "Weapons:!identified,Rarity=Rare,ilvl>=60,ClassName^Two Hand|ClassName^One Hand|ClassName=Bow|ClassName=Staff|ClassName=Sceptre|ClassName=Wand|ClassName=Dagger|ClassName=Claw|ClassName=Shield :Chaos Recipe\r\n" +
                "Jewelry:!identified,Rarity=Rare,ilvl>=60,ClassName=Ring|ClassName=Amulet :Chaos Recipe\r\n" +
                "Belts:!identified,Rarity=Rare,ilvl>=60,ClassName=Belt :Chaos Recipe\r\n" +
                "Helms:!identified,Rarity=Rare,ilvl>=60,ClassName=Helmet :Chaos Recipe\r\n" +
                "Body Armours:!identified,Rarity=Rare,ilvl>=60,ClassName=Body Armour :Chaos Recipe\r\n" +
                "Boots:!identified,Rarity=Rare,ilvl>=60,ClassName=Boots :Chaos Recipe\r\n" +
                "Gloves:!identified,Rarity=Rare,ilvl>=60,ClassName=Gloves :Chaos Recipe\r\n" +
                "Blight Annointed Items:affix[0]=grantedpassive>=0|affix[0]=BlightEnchantment>=0,shifting:Overwrites\r\n" +
                "Convoking Wands [ilvl86]:BaseName=Convoking Wand,ilvl>=86:Overwrites\r\n" +
                "Stacked Decks:BaseName=Stacked Deck,affinity:Currency & League Items\r\n" +
                "Tattoos:ClassName=StackableCurrency,BaseName^Tattoo ,affinity:Currency & League Items\r\n" +
                "Omens:ClassName=StackableCurrency,BaseName^Omen ,affinity:Currency & League Items\r\n" +
                "Catalysts:BaseName^Catalyst,ClassName!=DivinationCard,affinity:Currency & League Items\r\n" +
                "Allflame & Coffins:ClassName=NecropolisPack|BaseName=Filled Coffin,affinity:Currency & League Items\r\n" +
                "Delirium Orbs:Path^CurrencyAfflictionOrb,affinity:Currency & League Items\r\n" +
                "Incubators All:ClassName=IncubatorStackable:Currency & League Items\r\n" +
                "Invitations:ClassName=MiscMapItem,BaseName^ Invitation,affinity:Maps & Fragments\r\n" +
                "Tomes:ClassName=ItemisedSanctum:Sanctum\r\n" +
                "Relics:ClassName=Relic,shifting:Sanctum\r\n" +
                "Contracts:ClassName^HeistContract,affinity:Heist Locker\r\n" +
                "Blueprints:ClassName^HeistBlueprint,affinity:Heist Locker\r\n" +
                "Heist Equipment:ClassName^HeistEquipment:Heist Locker\r\n" +
                "Heist Trinket:ClassName^Trinket:Heist Locker\r\n" +
                "Heist Objective:ClassName^HeistObjective:Heist Locker\r\n" +
                "Abyss Items:BaseName^Stygian:Special Gear & Crafting Bases\r\n" +
                "Tincture Flasks:ClassName=Tincture:Special Gear & Crafting Bases\r\n" +
                "Charms:ClassName=AnimalCharm:Special Gear & Crafting Bases\r\n" +
                "Talismans:ClassName=Amulet,BaseName^Talisman,Rarity!=Unique:Special Gear & Crafting Bases\r\n" +
                "Influenced Shaper:shaper,shifting:Special Gear & Crafting Bases\r\n" +
                "Influenced Elder:elder,shifting:Special Gear & Crafting Bases\r\n" +
                "Influenced Hunter:hunter,shifting:Special Gear & Crafting Bases\r\n" +
                "Influenced Warlord:warlord,shifting:Special Gear & Crafting Bases\r\n" +
                "Influenced Redeemer:redeemer,shifting:Special Gear & Crafting Bases\r\n" +
                "Influenced Crusader:crusader,shifting:Special Gear & Crafting Bases\r\n" +
                "High ilvl Body Armours:ilvl>=86,ClassName=Body Armour,shifting:Special Gear & Crafting Bases\r\n" +
                "High ilvl Helmets:ilvl>=86,ClassName=Helmet,shifting:Special Gear & Crafting Bases\r\n" +
                "High ilvl Weapons:ilvl>=86,ClassName^Two Hand|ClassName^One Hand|ClassName=Bow|ClassName=Staff|ClassName=Sceptre|ClassName=Wand|ClassName=Dagger|ClassName=Claw,shifting:Special Gear & Crafting Bases\r\n" +
                "Uniques:Rarity=Unique,ClassName!=Map,affinity:Uniques\r\n" +
                "Flasks All:ClassName^Flask,Rarity!=Unique,affinity:Flasks\r\n" +
                "Skill Gems High:ClassName^Skill Gem,ItemQuality>=1,SkillGemLevel>=18,shifting:Skill Gems\r\n" +
                "Skill Gems Quality Low:ClassName^Skill Gem,ItemQuality>0,SkillGemLevel<18,shifting:Skill Gems\r\n" +
                "Small Cluster Jewels:BaseName^Small Cluster Jewel,shifting:Jewels & Cluster Jewels\r\n" +
                "Medium Cluster Jewels:BaseName^Medium Cluster Jewel,shifting:Jewels & Cluster Jewels\r\n" +
                "Large Cluster Jewels:BaseName^Large Cluster Jewel,shifting:Jewels & Cluster Jewels\r\n" +
                "Abyss Jewels:ClassName=AbyssJewel,shifting:Jewels & Cluster Jewels\r\n" +
                "Regular Jewels:ClassName=Jewel,BaseName!^Small Cluster Jewel,BaseName!^Medium Cluster Jewel,BaseName!^Large Cluster Jewel,ClassName!=AbyssJewel,Rarity != Unique:Jewels & Cluster Jewels\r\n" +
                "6 Link:NumberOfLinks=6,shifting:6 Link + 6 Sockets\r\n" +
                "Left Rares:Rarity=Rare,ClassName!=Map,shifting:Left overs\r\n" +
                "Left Magics:Rarity=Magic,ClassName!=Map,shifting:Left overs\r\n" +
                "Left Rare Rings:ClassName=Ring,Rarity=Rare,shifting:Left overs\r\n" +
                "Left Rare Amulets:ClassName=Amulet,Rarity=Rare,shifting:Left overs\r\n" +
                "Wombgifts All:ClassName^BrequelFruit,affinity:Wombgifts\r\n" +
                "Wombgifts 75+:ClassName^BrequelFruit,ilvl>=75,shifting:Wombgifts\r\n" +
                "Viscera Head:VisceraSlot=head:Viscera\r\n" +
                "Viscera Arm:VisceraSlot=arm:Viscera\r\n" +
                "Viscera Leg:VisceraSlot=leg:Viscera\r\n" +
                "Viscera Body:VisceraSlot=body:Viscera\r\n" +
                "Wombgift Provisioning:BaseName=Provisioning Wombgift:Wombgifts\r\n" +
                "Wombgift Lavish:BaseName=Lavish Wombgift:Wombgifts\r\n" +
                "Wombgift Ancient:BaseName=Ancient Wombgift:Wombgifts\r\n" +
                "Wombgift Mysterious:BaseName=Mysterious Wombgift:Wombgifts\r\n" +
                "Wombgift Growing:BaseName=Growing Wombgift:Wombgifts";

            #endregion

            return filtersConfig;
        }

        internal void LoadCustomFilters()
        {
            EnsureProfilesDirectory();
            var filterFilePath = ActiveProfileFile;

            try
            {
                if (!File.Exists(filterFilePath))
                {
                    File.WriteAllLines(filterFilePath, new[]
                    {
                        "// SquirrelStash filter profile - add filters with the in-game editor."
                    });
                    _core.LogMessage($"SquirrelStash: Created missing profile file {Path.GetFileName(filterFilePath)}.", 3);
                }
            }
            catch (Exception e)
            {
                _activeFilters ??= new List<CustomFilter>();
                _parseErrors = new List<string> { $"Failed to create {Path.GetFileName(filterFilePath)}: {e.Message}" };
                _core.LogError($"SquirrelStash: Failed to create profile file {filterFilePath}: {e}", 5);
                return;
            }

            try
            {
                var filterLines = File.ReadAllLines(filterFilePath);
                _activeFilters = FilterParser.Parse(filterLines, out var parseErrors);
                _parseErrors = parseErrors;
            }
            catch (Exception e)
            {
                _activeFilters ??= new List<CustomFilter>();
                _parseErrors = new List<string> { $"Failed to read {Path.GetFileName(filterFilePath)}: {e.Message}" };
                _core.LogError($"SquirrelStash: Failed to load filters from {filterFilePath}: {e}", 5);
                return;
            }

            foreach (var customFilter in _activeFilters)
            {
                if (!_core.Settings.CustomFilterOptions.TryGetValue(customFilter.Name, out var indexNodeS))
                {
                    indexNodeS = new ListIndexNode {Value = "Ignore", Index = -1};
                    _core.Settings.CustomFilterOptions.Add(customFilter.Name, indexNodeS);
                }

                customFilter.StashIndexNode = indexNodeS;

                // enforce mutual exclusivity: can't have both affinity and shifting
                if (customFilter.IsAffinity && customFilter.IsShifting)
                {
                    // keep shifting, drop affinity (shifting is explicit deposit mode)
                    // rewrite Commands without affinity
                    var cmds = (customFilter.Commands ?? "").Split(',').Select(p => p.Trim()).ToList();
                    var filtered = new List<string>();
                    foreach (var p in cmds)
                    {
                        if (p.IndexOf('|') >= 0)
                        {
                            var toks = p.Split('|').Select(t => t.Trim()).Where(t => !t.Equals("affinity", StringComparison.OrdinalIgnoreCase)).ToArray();
                            if (toks.Length > 0) filtered.Add(string.Join("|", toks));
                        }
                        else if (!p.Equals("affinity", StringComparison.OrdinalIgnoreCase)) filtered.Add(p);
                    }
                    customFilter.Commands = string.Join(",", filtered);
                    var line = customFilter.Name + ":" + customFilter.Commands + ":" + customFilter.SubmenuName;
                    var reparsed = FilterParser.Parse(new[] { line }, out var _affShFixErrors);
                    if (reparsed.Count == 1)
                    {
                        customFilter.Filters.Clear();
                        customFilter.Filters.AddRange(reparsed[0].Filters);
                        customFilter.BAny = reparsed[0].BAny;
                    }
                }
            }
        }

        internal string ProfilesDirectory => Path.Combine(_core.ConfigDirectory, "Profiles");

        internal string ActiveProfileFile =>
            Path.Combine(ProfilesDirectory, (string.IsNullOrEmpty(_core.Settings.ActiveProfile) ? DefaultProfileName : _core.Settings.ActiveProfile) + ".txt");

        internal void EnsureProfilesDirectory()
        {
            try
            {
                Directory.CreateDirectory(ProfilesDirectory);

                var defaultFile = Path.Combine(ProfilesDirectory, DefaultProfileName + ".txt");
                if (File.Exists(defaultFile)) return;

                var legacy = Path.Combine(_core.DirectoryFullName, FiltersConfigFilePrimary);
                if (File.Exists(legacy))
                {
                    File.Copy(legacy, defaultFile);
                    _core.LogMessage($"SquirrelStash: Seeded '{DefaultProfileName}' profile from {FiltersConfigFilePrimary}.", 3);
                }
                else
                {
                    File.WriteAllText(defaultFile, GetDefaultFilterConfig());
                    _core.LogMessage($"SquirrelStash: Seeded '{DefaultProfileName}' profile with the default filter template.", 3);
                }
            }
            catch (Exception e)
            {
                _core.LogError($"SquirrelStash: Failed to ensure profiles directory: {e}");
            }
        }

        private List<string> _profilesCache;
        private DateTime _profilesCacheTime = DateTime.MinValue;
        private const int ProfilesCacheMs = 2000;

        internal List<string> GetProfiles()
        {
            if (_profilesCache != null && (DateTime.UtcNow - _profilesCacheTime).TotalMilliseconds < ProfilesCacheMs)
                return _profilesCache;

            EnsureProfilesDirectory();
            try
            {
                _profilesCache = Directory.GetFiles(ProfilesDirectory, "*.txt")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception e)
            {
                _core.LogError($"SquirrelStash: Failed to list profiles: {e}");
                return new List<string> { DefaultProfileName };
            }

            _profilesCacheTime = DateTime.UtcNow;
            return _profilesCache;
        }

        private void InvalidateProfilesCache() => _profilesCache = null;

        internal void CreateProfile(string name)
        {
            EnsureProfilesDirectory();
            var file = Path.Combine(ProfilesDirectory, name + ".txt");
            if (!File.Exists(file))
            {
                File.WriteAllLines(file, new[]
                {
                    "// SquirrelStash filter profile - add filters with the in-game editor."
                });
                InvalidateProfilesCache();
            }
        }

        internal void SwitchProfile(string profileName)
        {
            if (string.IsNullOrEmpty(profileName)) return;

            CreateProfile(profileName);
            InvalidateProfilesCache();
            _core.Settings.ActiveProfile = profileName;
            _core.SetupOrClose();
            _core.LogMessage($"SquirrelStash: Active profile: {profileName}", 5);
        }

        internal void CreateNewProfile()
        {
            var baseName = "New Profile";
            var name = baseName;
            var suffix = 2;
            while (File.Exists(Path.Combine(ProfilesDirectory, name + ".txt")))
                name = $"{baseName} {suffix++}";

            CreateProfile(name);
            SwitchProfile(name);
        }

        internal void ConfirmDeleteProfile()
        {
            if (string.IsNullOrEmpty(_pendingDeleteProfile)) return;
            if (_pendingDeleteProfile == DefaultProfileName) return;

            var activeProfile = string.IsNullOrEmpty(_core.Settings.ActiveProfile) ? DefaultProfileName : _core.Settings.ActiveProfile;
            var deletingActive = _pendingDeleteProfile == activeProfile;

            try
            {
                File.Delete(Path.Combine(ProfilesDirectory, _pendingDeleteProfile + ".txt"));
                InvalidateProfilesCache();
                _core.LogMessage($"SquirrelStash: Deleted profile '{_pendingDeleteProfile}'.", 3);
            }
            catch (Exception e)
            {
                _core.LogError($"SquirrelStash: Failed to delete profile: {e}");
                _pendingDeleteProfile = null;
                return;
            }

            if (deletingActive)
            {
                _core.Settings.ActiveProfile = DefaultProfileName;
                _core.LogMessage("SquirrelStash: Active profile was deleted - switched back to Default.", 3);
            }

            _pendingDeleteProfile = null;
            _core.SetupOrClose();
        }

        internal string GetAssignedTabName(CustomFilter filter)
        {
            if (_core.Tabs.StashTabNamesByIndex == null || _core.Tabs.StashTabNamesByIndex.Length == 0) return "Ignore";
            if (_core.Settings.CustomFilterOptions.TryGetValue(filter.Name, out var node))
            {
                var i = node.Index + 1;
                if (i >= 0 && i < _core.Tabs.StashTabNamesByIndex.Length) return _core.Tabs.StashTabNamesByIndex[i];
            }
            return "Ignore";
        }

        internal void DrawFiltersTab()
        {
            if (_parseErrors.Count > 0)
            {
                ImGui.TextColored(new Vector4(1f, 0.55f, 0f, 1f),
                    $"{_parseErrors.Count} filter(s) failed to load.");
                if (ImGui.CollapsingHeader("Show errors"))
                {
                    foreach (var parseError in _parseErrors)
                        ImGui.TextWrapped((parseError ?? "").Replace("%", "%%"));
                }
                ImGui.Spacing();
            }

            if (_filterTabs != null)
                _filterTabs.Invoke();
            else
                ImGui.TextDisabled("Open your stash once to load the tab list.");
        }

        internal void GenerateMenu()
        {
            if (_core.Tabs.RenamedAllStashNames == null || _core.Tabs.RenamedAllStashNames.Count == 0) return;

            _core.Tabs.StashTabNamesByIndex = _core.Tabs.RenamedAllStashNames.ToArray();

            _filterTabs = null;

            if (_activeFilters == null) return;

            if (_selectedFilter != null)
                _selectedFilter = _activeFilters.FirstOrDefault(x => x.Name == _selectedFilter.Name);

            // Group once per menu build, not once per frame: this lambda runs
            // every frame while settings are open.
            var groupedFilters = _activeFilters.GroupBy(x => x.SubmenuName, x => x).ToList();
            var allCategoryNames = _activeFilters.Select(x => x.SubmenuName).Distinct().ToArray();
            // Per-row ImGui id strings: building "##chip_x" / "##flt_y" per row per
            // frame was ~0.5MB/s of Gen0. Ids key off the filter's stable index,
            // so they survive tab picks (only add/delete rebuilds the menu).
            var rowUi = new Dictionary<CustomFilter, (string chip, string sel, string label)>();
            for (var i = 0; i < _activeFilters.Count; i++)
            {
                var f = _activeFilters[i];
                var label = (f.Name ?? "").Replace("%", "%%");
                rowUi[f] = ("pick" + i + "_" + i, label + "##flt" + i + "_" + i, label);
            }

            _filterTabs = () =>
            {
                var profiles = GetProfiles();
                var activeProfile = string.IsNullOrEmpty(_core.Settings.ActiveProfile) ? DefaultProfileName : _core.Settings.ActiveProfile;
                var profileIndex = Math.Max(0, profiles.IndexOf(activeProfile));

                HouseRows.Choice(ref profileIndex, profiles.ToArray(), "Filter profile", "profileChoice");
                var profileTarget = profiles[profileIndex];
                if (profileTarget != activeProfile) SwitchProfile(profileTarget);

                var selectedName = profiles[profileIndex];
                var canDelete = selectedName != DefaultProfileName;
                var profileActions = UiHelpers.ButtonRow(
                    ("New profile", "newProfile", 1),
                    ("Delete profile", "deleteProfile", 2));
                if (profileActions == 0) CreateNewProfile();
                else if (profileActions == 1 && canDelete)
                {
                    _pendingDeleteProfile = selectedName;
                    _deleteProfileOpen = true;
                    ImGui.OpenPopup("Delete profile?");
                }

                if (ImGui.BeginPopupModal("Delete profile?", ref _deleteProfileOpen,
                    ImGuiWindowFlags.AlwaysAutoResize))
                {
                    UiHelpers.HouseTitle("Delete profile");
                    ImGui.TextUnformatted($"Delete profile '{_pendingDeleteProfile}'?");
                    ImGui.Text("This cannot be undone.");
                    ImGui.Spacing();
                    var choice = UiHelpers.ButtonRow(("Delete", "delProfile", 2), ("Cancel", "cancelProfile", 0));
                    if (choice == 0)
                    {
                        ConfirmDeleteProfile();
                        ImGui.CloseCurrentPopup();
                    }
                    else if (choice == 1) ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }

                ImGui.Spacing();

                ImGui.BeginChild("##filterListLeft", new System.Numerics.Vector2(280, 0), ImGuiChildFlags.Borders);
                if (UiHelpers.Button("Add filter", "addFilterButton", 1))
                {
                    _newFilterName = "";
                    _newFilterCategory = _selectedFilter?.SubmenuName ?? "Custom";
                    _addFilterOpen = true;
                    ImGui.OpenPopup("Add filter");
                }
                ImGui.Spacing();
                if (_activeFilters.Count == 0)
                {
                    ImGui.TextDisabled("No filters yet. Use Add filter.");
                }
                foreach (var group in groupedFilters)
                {
                    var groupStart = ImGui.GetCursorScreenPos();
                    float groupLineH = ImGui.GetTextLineHeight();
                    float groupW = ImGui.GetContentRegionAvail().X;
                    ImGui.SetCursorScreenPos(groupStart);
                    float categoryButtonW = ImGui.CalcTextSize("R").X + 20f;
                    float categoryActionsW = categoryButtonW * 2f + 8f;
                    var categoryLabel = group.Key.Replace("%", "%%");
                    float categoryMaxW = Math.Max(0f, groupW - categoryActionsW - 8f);
                    while (categoryLabel.Length > 1 && ImGui.CalcTextSize(categoryLabel + "..").X > categoryMaxW)
                        categoryLabel = categoryLabel.Substring(0, categoryLabel.Length - 1);
                    if (ImGui.CalcTextSize(group.Key.Replace("%", "%%")).X > categoryMaxW)
                        categoryLabel += "..";
                    ImGui.TextColored(new Vector4(0f, 1f, 0.022f, 1f), categoryLabel);
                    var categoryActionsPos = groupStart +
                        new System.Numerics.Vector2(groupW - categoryActionsW, 0f);
                    ImGui.SetCursorScreenPos(categoryActionsPos);
                    int categoryAction = UiHelpers.ButtonRow(
                        ("R", "renameCat" + group.Key, 0),
                        ("D", "deleteCat" + group.Key, 2));
                    if (categoryAction == 0)
                    {
                        _renameCategoryOldName = group.Key;
                        _renameCategoryNewName = group.Key;
                        _renameCategoryOpen = true;
                        ImGui.OpenPopup("Rename category");
                    }
                    else if (categoryAction == 1)
                    {
                        _deleteCategoryName = group.Key;
                        _deleteCategoryOpen = true;
                        ImGui.OpenPopup("Delete category");
                    }
                    var renameMin = categoryActionsPos;
                    var renameMax = renameMin + new System.Numerics.Vector2(categoryButtonW, groupLineH + 8f);
                    var deleteMin = renameMin + new System.Numerics.Vector2(categoryButtonW + 8f, 0f);
                    var deleteMax = deleteMin + new System.Numerics.Vector2(categoryButtonW, groupLineH + 8f);
                    if (ImGui.IsMouseHoveringRect(renameMin, renameMax)) ImGui.SetTooltip("Rename category");
                    else if (ImGui.IsMouseHoveringRect(deleteMin, deleteMax)) ImGui.SetTooltip("Delete category");
                    ImGui.SetCursorScreenPos(new System.Numerics.Vector2(groupStart.X, groupStart.Y + groupLineH + 8f));
                    ImGui.Separator();
                    var filterIdx = 0;
                    foreach (var filter in group)
                    {
                        var assignedTab = GetAssignedTabName(filter);
                        var tabLabel = assignedTab.Length > 8 ? assignedTab.Substring(0, 6) + ".." : assignedTab;
                        var filterRowStart = ImGui.GetCursorScreenPos();
                        float filterRowH = ImGui.GetTextLineHeight() + 8f;
                        if (!rowUi.TryGetValue(filter, out var ids))
                        {
                            var fbLabel = (filter.Name ?? "").Replace("%", "%%");
                            ids = ("pick" + filterIdx + "_" + filterIdx, fbLabel + "##flt" + filterIdx, fbLabel);
                        }
                        if (UiHelpers.FilterChip(tabLabel, false, ids.chip))
                        {
                            _quickPickFor = filter;
                            _selectedFilter = filter;
                            _quickPickFilter = "";
                            _quickPickFocus = true;
                            _quickPickOpen = true;
                        }
                        if (ImGui.IsMouseHoveringRect(
                                filterRowStart,
                                filterRowStart + new System.Numerics.Vector2(
                                    ImGui.CalcTextSize(tabLabel).X + 12f,
                                    ImGui.GetTextLineHeight() + 4f)))
                            ImGui.SetTooltip("Choose the stash tab for this filter.");
                        const float stashTabColumnWidth = 84f;
                        ImGui.SetCursorScreenPos(filterRowStart + new System.Numerics.Vector2(
                            stashTabColumnWidth, 0f));
                        if (ImGui.Selectable(ids.sel, filter == _selectedFilter))
                            _selectedFilter = filter;
                        ImGui.SetCursorScreenPos(new System.Numerics.Vector2(
                            filterRowStart.X, filterRowStart.Y + filterRowH));
                        filterIdx++;
                    }
                }

                if (_quickPickOpen)
                    ImGui.OpenPopup("Pick tab");

                if (ImGui.BeginPopupModal("Pick tab", ref _quickPickOpen, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    UiHelpers.HouseTitle("Pick tab");
                    if (_quickPickFor != null)
                    {
                        ImGui.TextUnformatted("Pick tab for: " + _quickPickFor.Name);
                        if (_core.Tabs.StashTabNamesByIndex != null &&
                            _core.Settings.CustomFilterOptions.TryGetValue(_quickPickFor.Name, out var quickNode))
                        {
                            var current = quickNode.Index + 1;
                            ImGui.SetNextItemWidth(280f);
                            if (_quickPickFocus)
                            {
                                ImGui.SetKeyboardFocusHere();
                                _quickPickFocus = false;
                            }
                            ImGui.InputTextWithHint("##pickTabFilter", "Search tabs...", ref _quickPickFilter, 64);
                            var allTabs = _core.Tabs.StashTabNamesByIndex;
                            var matches = new List<int>();
                            for (var m = 0; m < allTabs.Length; m++)
                            {
                                if (string.IsNullOrWhiteSpace(_quickPickFilter) ||
                                    (allTabs[m] ?? "").IndexOf(_quickPickFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                                    matches.Add(m);
                            }
                            ImGui.TextDisabled($"{matches.Count} of {allTabs.Length}");
                            ImGui.BeginChild("##pickTabList", new System.Numerics.Vector2(280, 320), ImGuiChildFlags.Borders);
                            foreach (var i in matches)
                            {
                                var tabName = allTabs[i] ?? "";
                                if (ImGui.Selectable(tabName.Replace("%", "%%") + "##tab" + i, i == current))
                                {
                                    quickNode.Value = allTabs[i];
                                    OnSettingsStashNameChanged(quickNode, allTabs[i]);
                                    SaveCustomFilters();
                                    _quickPickOpen = false;
                                    ImGui.CloseCurrentPopup();
                                }
                            }
                            ImGui.EndChild();
                            if (ImGui.IsKeyPressed(ImGuiKey.Enter) && matches.Count > 0)
                            {
                                var first = matches[0];
                                quickNode.Value = allTabs[first];
                                OnSettingsStashNameChanged(quickNode, allTabs[first]);
                                SaveCustomFilters();
                                _quickPickOpen = false;
                                ImGui.CloseCurrentPopup();
                            }
                        }
                    }
                    ImGui.Spacing();
                    if (UiHelpers.Button("Cancel"))
                    {
                        _quickPickOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }

                if (ImGui.BeginPopupModal("Add filter", ref _addFilterOpen, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    UiHelpers.HouseTitle("Add filter");
                    ImGui.InputText("Name", ref _newFilterName, 128);
                    ImGui.InputText("Category", ref _newFilterCategory, 128);
                    ImGui.Spacing();
                    var choice = UiHelpers.ButtonRow(("Create", "addFilter", 1), ("Cancel", "cancelAddFilter", 0));
                    if (choice == 0 && !string.IsNullOrWhiteSpace(_newFilterName))
                    {
                        AddCustomFilter(_newFilterName.Trim(), _newFilterCategory);
                        SaveCustomFilters();
                        GenerateMenu();
                        _addFilterOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    else if (choice == 1)
                    {
                        _addFilterOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }

                if (ImGui.BeginPopupModal("Rename category", ref _renameCategoryOpen, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    UiHelpers.HouseTitle("Rename category");
                    ImGui.TextUnformatted($"Rename '{_renameCategoryOldName}' to:");
                    ImGui.InputText("Name", ref _renameCategoryNewName, 128);
                    ImGui.Spacing();
                    var choice = UiHelpers.ButtonRow(("Rename", "renameCat", 1), ("Cancel", "cancelRename", 0));
                    if (choice == 0 &&
                        !string.IsNullOrWhiteSpace(_renameCategoryNewName) &&
                        _renameCategoryNewName != _renameCategoryOldName)
                    {
                        RenameCategory(_renameCategoryOldName, _renameCategoryNewName.Trim());
                        _renameCategoryOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    else if (choice == 1)
                    {
                        _renameCategoryOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }

                if (ImGui.BeginPopupModal("Delete category", ref _deleteCategoryOpen, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    UiHelpers.HouseTitle("Delete category");
                    var categoryFilterCount = _activeFilters.Count(f => f.SubmenuName == _deleteCategoryName);
                    ImGui.TextUnformatted($"Delete category '{_deleteCategoryName}' and its {categoryFilterCount} filter(s)?");
                    ImGui.Text("This cannot be undone.");
                    ImGui.Spacing();
                    var choice = UiHelpers.ButtonRow(("Delete", "delCat", 2), ("Cancel", "cancelDelCat", 0));
                    if (choice == 0)
                    {
                        DeleteCategory(_deleteCategoryName);
                        _deleteCategoryOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    else if (choice == 1)
                    {
                        _deleteCategoryOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
                ImGui.Dummy(new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, 1f));
                ImGui.EndChild();

                ImGui.SameLine();

                ImGui.BeginChild("##filterDetailRight", new System.Numerics.Vector2(0, 0), ImGuiChildFlags.Borders);
                if (_selectedFilter == null)
                {
                    ImGui.TextDisabled("Select a filter on the left to edit it.");
                }
                else
                {
                    if (_queryEditFilter != _selectedFilter)
                    {
                        _queryEditFilter = _selectedFilter;
                        _queryEditBuffer = _selectedFilter.Commands ?? "";
                        _queryEditError = null;
                        _queryDirty = false;
                    }

                    var isDefault = activeProfile == DefaultProfileName &&
                                    DefaultFilterNames.Contains(_selectedFilter.Name);
                    ImGui.TextColored(new Vector4(0f, 1f, 0.022f, 1f),
                        (_selectedFilter.Name + (isDefault ? "  (default)" : "")).Replace("%", "%%"));
                    ImGui.Separator();

                    var categoryNames = allCategoryNames;
                    var categoryIndex = Array.IndexOf(categoryNames, _selectedFilter.SubmenuName);
                    if (categoryIndex < 0) categoryIndex = 0;
                    HouseRows.Choice(ref categoryIndex, categoryNames, "Category", "filterCategory");
                    if (categoryNames.Length > 0 && categoryIndex < categoryNames.Length)
                    {
                        var target = categoryNames[categoryIndex];
                        if (_selectedFilter.SubmenuName != target)
                        {
                            _selectedFilter.SubmenuName = target;
                            SaveCustomFilters();
                            GenerateMenu();
                        }
                    }

                    if (_core.Settings.CustomFilterOptions.TryGetValue(_selectedFilter.Name, out var indexNode))
                    {
                        var item = indexNode.Index + 1;
                        if (UiHelpers.SearchableChoice("Stash to tab", ref item, _core.Tabs.StashTabNamesByIndex,
                                "selectedFilterTab", 260f) &&
                            item >= 0 && item < _core.Tabs.StashTabNamesByIndex.Length)
                        {
                            indexNode.Value = _core.Tabs.StashTabNamesByIndex[item];
                            OnSettingsStashNameChanged(indexNode, _core.Tabs.StashTabNamesByIndex[item]);
                        }
                    }

                    // Affinity = no tab switch (stash affinity), Shifting = hold shift while dropping.
                    // Mirrors BoolFilter IsAffinity/IsShifting (FilterParser BooleanKeywords "affinity","shifting").
                    var isAffinity = _selectedFilter.IsAffinity;
                    var isShifting = _selectedFilter.IsShifting;
                    isAffinity = HouseRows.Toggle("Affinity (no tab switch)", isAffinity, "affinity",
                        "Deposit without switching tabs. Adds ,affinity to the query.");
                    if (isAffinity != _selectedFilter.IsAffinity)
                    {
                        SetFilterFlag(_selectedFilter, "affinity", isAffinity);
                        isAffinity = _selectedFilter.IsAffinity;
                        isShifting = _selectedFilter.IsShifting;
                    }

                    isShifting = HouseRows.Toggle("Shifting", isShifting, "shifting",
                        "Hold Shift while dropping. Adds ,shifting to the query.");
                    if (isShifting != _selectedFilter.IsShifting)
                        SetFilterFlag(_selectedFilter, "shifting", isShifting);

                    UiHelpers.SectionHeader("Query");
                    ImGui.TextWrapped((_queryEditBuffer ?? "").Replace("%", "%%"));
                    if (_queryDirty)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(1f, 0.6f, 0f, 1f), "Unsaved");
                    }

                    ImGui.Spacing();
                    var queryActions = UiHelpers.ButtonRow(
                        ("Edit query", "editQuery", 1),
                        ("Delete filter", "deleteFilter", 2));
                    if (queryActions == 0)
                    {
                        Editor.SetText("stashieQuery", _queryEditBuffer);
                        _queryEditorOpen = true;
                        ImGui.OpenPopup("Edit query");
                    }
                    else if (queryActions == 1 && !isDefault) DeleteCustomFilter();
                    if (isDefault) ImGui.TextColored(UiHelpers.Muted, "Default filters cannot be deleted.");
                }

                ImGui.SetNextWindowSize(new System.Numerics.Vector2(680, 560), ImGuiCond.Appearing);
                if (ImGui.BeginPopupModal("Edit query", ref _queryEditorOpen))
                {
                    UiHelpers.HouseTitle("Edit query");
                    var changed = Editor.Code("stashieQuery", ref _queryEditBuffer, GetQueryEditorOpts());
                    if (changed)
                    {
                        _queryDirty = true;
                        ValidateQuery();
                    }

                    if (_queryEditError == null)
                    {
                        ImGui.TextColored(new Vector4(0f, 1f, 0f, 1f), "Query is valid.");
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), (_queryEditError ?? "").Replace("%", "%%"));
                    }
                    ImGui.TextDisabled("Each line becomes its own filter. Saving again adds more copies.");

                    ImGui.Spacing();
                    var choice = UiHelpers.ButtonRow(("Save", "saveQuery", 1), ("Cancel", "cancelQuery", 0));
                    if (choice == 0 && _queryEditError == null)
                    {
                        SaveCurrentFilter();
                        _queryEditorOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    else if (choice == 1)
                    {
                        _queryEditBuffer = _selectedFilter == null ? "" : _selectedFilter.Commands ?? "";
                        _queryEditError = null;
                        _queryDirty = false;
                        Editor.SetText("stashieQuery", _queryEditBuffer);
                        _queryEditorOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
                ImGui.EndChild();
            };
        }

        private void ValidateQuery()
        {
            _queryEditError = null;

            if (_queryEditBuffer == null) return;

            var lines = _queryEditBuffer.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;

                if (line.Contains(":"))
                {
                    _queryEditError =
                        $"Line {i + 1}: Query cannot contain ':' - it's used as the name/submenu separator.";
                    return;
                }

                List<string> errors;
                FilterParser.SuppressDebugMessages = true;
                try { FilterParser.Parse(new[] { "__:" + line + ":" }, out errors); }
                finally { FilterParser.SuppressDebugMessages = false; }
                if (errors.Count > 0)
                {
                    _queryEditError = $"Line {i + 1}: {errors[0]}";
                    return;
                }
            }
        }

        internal int ErrorLineIndex(string message)
        {
            if (message != null && message.StartsWith("Line "))
            {
                var end = message.IndexOf(':', 5);
                if (end > 5 && int.TryParse(message.Substring(5, end - 5), out var n))
                    return Math.Max(0, n - 1);
            }
            return 0;
        }

        internal static List<string> SplitQueryLines(string text) =>
            (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        private void SaveCurrentFilter()
        {
            if (_selectedFilter == null) return;

            var lines = SplitQueryLines(_queryEditBuffer);

            if (lines.Count == 0) return;

            _selectedFilter.Commands = lines[0];
            ApplyParsedConditions(_selectedFilter);

            if (_core.Settings.CustomFilterOptions.TryGetValue(_selectedFilter.Name, out var templateNode))
            {
                for (var i = 1; i < lines.Count; i++)
                    CreateFilter(_selectedFilter.Name, _selectedFilter.SubmenuName, lines[i], templateNode);
            }

            SaveCustomFilters();
            GenerateMenu();
            _queryEditError = null;
            _queryDirty = false;
            Editor.SetText("stashieQuery", _queryEditBuffer);
        }

        private static void ApplyParsedConditions(CustomFilter filter)
        {
            var line = filter.Name + ":" + (filter.Commands ?? "") + ":" + filter.SubmenuName;
            var parsed = FilterParser.Parse(new[] { line }, out _);
            if (parsed.Count == 1)
            {
                filter.Filters.Clear();
                filter.Filters.AddRange(parsed[0].Filters);
                filter.BAny = parsed[0].BAny;
            }
        }

        internal CustomFilter CreateFilter(string name, string category, string commands, ListIndexNode template)
        {
            var uniqueName = name;
            var suffix = 2;
            while (_activeFilters.Any(f => f.Name == uniqueName))
                uniqueName = $"{name} {suffix++}";

            var indexNode = new ListIndexNode
            {
                Value = template?.Value ?? "Ignore",
                Index = template?.Index ?? -1
            };
            _core.Settings.CustomFilterOptions[uniqueName] = indexNode;

            var newFilter = new CustomFilter
            {
                Name = uniqueName,
                SubmenuName = string.IsNullOrWhiteSpace(category) ? "Custom" : category.Trim(),
                Commands = commands,
                Index = _activeFilters.Count,
                StashIndexNode = indexNode
            };
            ApplyParsedConditions(newFilter);
            _activeFilters.Add(newFilter);
            return newFilter;
        }

        internal void SaveCustomFilters()
        {
            EnsureProfilesDirectory();
            var filterFilePath = ActiveProfileFile;

            var lines = new List<string> { "// SquirrelStash filter config - saved from the in-game editor." };
            lines.AddRange(_activeFilters.Select(f =>
                string.IsNullOrEmpty(f.SubmenuName)
                    ? $"{f.Name}:{f.Commands}"
                    : $"{f.Name}:{f.Commands}:{f.SubmenuName}"));

            try
            {
                if (File.Exists(filterFilePath))
                    File.Copy(filterFilePath, filterFilePath + ".bak", true);

                File.WriteAllLines(filterFilePath, lines);
                _core.LogMessage(
                    $"SquirrelStash: Saved {_activeFilters.Count} filters to profile '{Path.GetFileNameWithoutExtension(filterFilePath)}'.", 3);
            }
            catch (Exception e)
            {
                _core.LogError($"SquirrelStash: Failed to save filters: {e}");
            }
        }

        private EditorOpts GetQueryEditorOpts()
        {
            if (_queryEditorOpts == null)
            {
                _queryEditorOpts = new EditorOpts
                {
                    Tokenize = SquirrelStashSyntax.Tokenize,
                    Complete = SquirrelStashSyntax.Complete,
                    LineNumbers = true,
                    Size = new System.Numerics.Vector2(560, 340)
                };
            }

            _queryEditorOpts.Errors = _queryEditError == null
                ? null
                : new Dictionary<int, string> {[ErrorLineIndex(_queryEditError)] = _queryEditError};
            return _queryEditorOpts;
        }

        private void RenameCategory(string oldName, string newName)
        {
            foreach (var filter in _activeFilters.Where(f => f.SubmenuName == oldName))
                filter.SubmenuName = newName;

            SaveCustomFilters();
            GenerateMenu();
        }

        private void DeleteCategory(string name)
        {
            var doomed = _activeFilters.Where(f => f.SubmenuName == name).ToList();
            foreach (var filter in doomed)
            {
                _core.Settings.CustomFilterOptions.Remove(filter.Name);
                _activeFilters.Remove(filter);
            }

            if (_selectedFilter == null || _selectedFilter.SubmenuName == name)
            {
                _selectedFilter = null;
                _queryEditFilter = null;
            }

            SaveCustomFilters();
            GenerateMenu();
        }

        private void AddCustomFilter(string name, string category)
        {
            var newFilter = CreateFilter(name, category, "", null);
            _selectedFilter = newFilter;

            SaveCustomFilters();
            GenerateMenu();
        }

        private void DeleteCustomFilter()
        {
            if (_selectedFilter == null) return;
            var inDefaultProfile = string.IsNullOrEmpty(_core.Settings.ActiveProfile) || _core.Settings.ActiveProfile == DefaultProfileName;
            if (inDefaultProfile && DefaultFilterNames.Contains(_selectedFilter.Name))
            {
                _core.LogMessage("SquirrelStash: Default filters cannot be deleted.", 3);
                return;
            }

            _core.Settings.CustomFilterOptions.Remove(_selectedFilter.Name);
            _activeFilters.Remove(_selectedFilter);
            _selectedFilter = null;
            _queryEditFilter = null;
            _queryEditBuffer = null;
            _queryEditError = null;
            _queryDirty = false;

            SaveCustomFilters();
            GenerateMenu();
        }

        private void SetFilterFlag(CustomFilter filter, string flag, bool enable)
        {
            // mutual exclusivity: affinity and shifting cannot both be set
            var opposite = flag.Equals("affinity", StringComparison.OrdinalIgnoreCase) ? "shifting"
                : flag.Equals("shifting", StringComparison.OrdinalIgnoreCase) ? "affinity" : null;
            var commands = filter.Commands ?? "";
            var parts = commands.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            var cleaned = new List<string>();
            foreach (var part in parts)
            {
                if (part.IndexOf('|') >= 0)
                {
                    var orTokens = part.Split('|').Select(t => t.Trim())
                        .Where(t => t.Length > 0
                            && !t.Equals(flag, StringComparison.OrdinalIgnoreCase)
                            && (opposite == null || !t.Equals(opposite, StringComparison.OrdinalIgnoreCase)))
                        .ToArray();
                    if (orTokens.Length == 0) continue;
                    cleaned.Add(string.Join("|", orTokens));
                }
                else
                {
                    if (part.Equals(flag, StringComparison.OrdinalIgnoreCase)) continue;
                    if (opposite != null && part.Equals(opposite, StringComparison.OrdinalIgnoreCase))
                    {
                        if (enable) continue; // drop opposite when enabling this flag
                    }
                    cleaned.Add(part);
                }
            }
            if (enable)
            {
                cleaned.Add(flag);
                // ensure opposite is not present
                cleaned = cleaned.Where(c =>
                {
                    if (c.IndexOf('|') >= 0) return true;
                    return !c.Equals(opposite, StringComparison.OrdinalIgnoreCase);
                }).ToList();
            }
            var newCommands = string.Join(",", cleaned);
            filter.Commands = newCommands;
            // re-parse to keep in-memory Filters (IsAffinity/IsShifting) in sync
            var line = filter.Name + ":" + newCommands + ":" + filter.SubmenuName;
            var parsed = FilterParser.Parse(new[] { line }, out var dummyErrors);
            if (parsed.Count == 1)
            {
                filter.Filters.Clear();
                filter.Filters.AddRange(parsed[0].Filters);
                filter.BAny = parsed[0].BAny;
            }
            if (_queryEditFilter == filter)
            {
                _queryEditBuffer = newCommands;
                Editor.SetText("stashieQuery", _queryEditBuffer);
                _queryDirty = false;
                _queryEditError = null;
            }
            SaveCustomFilters();
            // ensure UI shows saved state (SaveCustomFilters does not touch dirty flags)
            if (_queryEditFilter == filter)
            {
                _queryDirty = false;
                _queryEditError = null;
            }
        }

        private void OnSettingsStashNameChanged(ListIndexNode node, string newValue)
        {
            node.Index = _core.Tabs.GetInventIndexByStashName(newValue);
        }
    }
}
