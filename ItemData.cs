using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.PoEMemory.Models;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static ExileCore.PoEMemory.MemoryObjects.ServerInventory;

namespace SquirrelStash
{
    public class ItemData
    {
        private const string ClassNameHeistContract = "HeistContract";
        private const string ModNameVeil = "Veil";
        private const string ModNameEnchantment = "Enchantment";
        private const string ModNameInfectedMap = "InfectedMap";
        private const string ModNameAfflictionMapReward = "AfflictionMapReward";
        private const string ModNameMapElderContainsBoss = "MapElderContainsBoss";

        public string Path { get; }
        public string ClassName { get; }
        public string BaseName { get; }
        public string Name { get; }
        public string HeistContractJobType { get; }
        public ItemRarity Rarity { get; }
        public int ItemQuality { get; }
        public int Veiled { get; }
        public int Fractured { get; }
        public int ItemLevel { get; }
        public int MapTier { get; }
        public int NumberOfSockets { get; }
        public int LargestLinkSize { get; }
        public int DeliriumStacks { get; }
        public int HeistContractReqJobLevel { get; }
        public int ScourgeTier { get; }
        public bool BIdentified { get; }
        public bool isCorrupted { get; }
        public bool isElder { get; }
        public bool isShaper { get; }
        public bool isCrusader { get; }
        public bool isRedeemer { get; }
        public bool isHunter { get; }
        public bool isWarlord { get; }
        public bool isInfluenced { get; }
        public bool Synthesised { get; }
        public bool isBlightMap { get; }
        public bool isElderGuardianMap { get; }
        /// <summary>Wickerman Viscera slot: "arm", "head", "leg", "body", or "" for other items.</summary>
        public string VisceraSlot { get; }
        public int SkillGemLevel { get; }
        public List<ItemMod> ItemMods { get; }
        public uint InventoryID { get; }
        public Vector2 clientRect { get; }

        public ItemData(InventSlotItem inventoryItem, BaseItemType baseItemType, Vector2 clickPos)
            : this(inventoryItem?.Item, baseItemType, clickPos)
        {
        }

        public ItemData(Entity item, BaseItemType baseItemType, Vector2 clickPos)
        {
            if (item == null) return;

            // Component Declarations
            item.TryGetComponent<MapKey>(out var mapComp);
            item.TryGetComponent<Base>(out var baseComp);
            item.TryGetComponent<Mods>(out var modsComp);
            item.TryGetComponent<Sockets>(out var socketsComp);
            item.TryGetComponent<Quality>(out var qualityComp);
            item.TryGetComponent<SkillGem>(out var skillGemComp);
            item.TryGetComponent<HeistContract>(out var heistComp);


            // Processing Components
            Path = item.Path;
            InventoryID = item.InventoryId;
            ScourgeTier = baseComp?.ScourgedTier ?? 0;
            isElder = baseComp?.isElder ?? false;
            isShaper = baseComp?.isShaper ?? false;
            isHunter = baseComp?.isHunter ?? false;
            isWarlord = baseComp?.isWarlord ?? false;
            isCrusader = baseComp?.isCrusader ?? false;
            isRedeemer = baseComp?.isRedeemer ?? false;
            isCorrupted = baseComp?.isCorrupted ?? false;
            isInfluenced = isCrusader || isRedeemer || isWarlord || isHunter || isShaper || isElder;

            // Processing Heist Contract
            if (baseItemType.ClassName == ClassNameHeistContract)
            {
                HeistContractJobType = heistComp?.RequiredJob?.Name ?? "";
                HeistContractReqJobLevel = heistComp?.RequiredJobLevel ?? 0;
            }

            // Processing Mods
            ItemMods = modsComp?.ItemMods;
            ItemLevel = modsComp?.ItemLevel ?? 0;
            Fractured = modsComp?.CountFractured ?? 0;
            BIdentified = modsComp?.Identified ?? true;
            Synthesised = modsComp?.Synthesised ?? false;
            Rarity = modsComp?.ItemRarity ?? ItemRarity.Normal;
            Veiled = modsComp?.ItemMods?.Count(m => m.DisplayName?.Contains(ModNameVeil) == true) ?? 0;
            isBlightMap = modsComp?.ItemMods?.Count(m => m.Name.Contains(ModNameInfectedMap)) > 0;
            DeliriumStacks = modsComp?.ItemMods?
                .Where(m => m.Name.Contains(ModNameAfflictionMapReward))
                .Sum(m => m.Values != null && m.Values.Count > 0 ? m.Values[0] : 0) ?? 0;
            isElderGuardianMap = modsComp?.ItemMods?.Count(m => m.Name.Contains(ModNameMapElderContainsBoss)) > 0;

            // Processing Skill Gem
            SkillGemLevel = skillGemComp?.Level ?? 0;

            // Processing Sockets
            NumberOfSockets = socketsComp?.NumberOfSockets ?? 0;
            LargestLinkSize = socketsComp?.LargestLinkSize ?? 0;

            // Processing Quality
            ItemQuality = qualityComp?.ItemQuality ?? 0;

            // Other Assignments
            // Prefer the unique item name (e.g. "Headhunter") over the base-type name ("Leather Belt")
            // so a Name=... filter can actually match uniques.
            Name = modsComp?.UniqueName ?? baseComp?.Name ?? "";
            BaseName = baseItemType.BaseName;
            ClassName = baseItemType.ClassName;
            VisceraSlot = GetVisceraSlot(baseItemType?.Tags);

            // Processing Map
            MapTier = mapComp?.Tier ?? 0;

            // Final Assignment
            clientRect = clickPos;
        }

        // Every Wickerman Viscera base type carries exactly one tag naming its slot. Names are not
        // usable for this: "Justicar's Helm" is a head but "Coven Queen's Crown" is also a head
        // while "Golemic Stone" and "Aristocrat's Torso" are bodies, and "Coagulated Essence" is
        // a leg. The tag is the game's own slot classification and stays correct as items are added.
        private static string GetVisceraSlot(string[] tags)
        {
            if (tags == null) return "";
            foreach (var tag in tags)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                if (tag.StartsWith("viscera_", StringComparison.OrdinalIgnoreCase))
                    return tag.Substring("viscera_".Length).ToLowerInvariant();
            }
            return "";
        }

        public Vector2 GetClickPosCache()
        {
            return clientRect;
        }
    }
}
