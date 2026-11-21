using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using JM.LinqFaster;
using SharpDX;

namespace SquirrelStash
{
    public class FilterParser
    {
        private const char SYMBOL_COMMANDSDIVIDE = ',';
        private const char SYMBOL_COMMAND_FILTER_OR = '|';
        private const char SYMBOL_NAMEDIVIDE = ':';
        private const char SYMBOL_SUBMENUNAME = ':';
        private const char SYMBOL_NOT = '!';
        private const string COMMENTSYMBOL = "#";
        private const string COMMENTSYMBOLALT = "//";

        //String compare
        private const string PARAMETER_CLASSNAME = "classname";
        private const string PARAMETER_BASENAME = "basename";
        private const string PARAMETER_PATH = "path";
        private const string PARAMETER_NAME = "name";
        private const string PARAMETER_VISCERASLOT = "visceraslot";

        //Number compare
        private const string PARAMETER_QUALITY = "itemquality";
        private const string PARAMETER_RARITY = "rarity";
        private const string PARAMETER_ILVL = "ilvl";
        private const string PARAMETER_MapTier = "tier";
        private const string PARAMETER_NUMBER_OF_SOCKETS = "numberofsockets";
        private const string PARAMETER_LARGEST_LINK_SIZE = "numberoflinks";
        private const string PARAMETER_VEILED = "veiled";
        private const string PARAMETER_FRACTUREDMODS = "fractured";
        private const string PARAMETER_SKILLGEMLEVEL = "skillgemlevel";
        private const string PARAMETER_DELIRIUMREWARDSAMOUNT = "deliriumrewards";
        private const string PARAMETER_SCOURGETIER = "scourgetier";
        private const string PARAMETER_MODAFFIX = "affix";

        //Operations
        private const string OPERATION_NONEQUALITY = "!=";
        private const string OPERATION_LESSEQUAL = "<=";
        private const string OPERATION_BIGGERQUAL = ">=";
        private const string OPERATION_EQUALITY = "=";
        private const string OPERATION_BIGGER = ">";
        private const string OPERATION_LESS = "<";
        private const string OPERATION_CONTAINS = "^";
        private const string OPERATION_NOTCONTAINS = "!^";

        // As-you-type validation in the filter editor calls Parse on every keystroke. Those partial
        // queries are expected to be invalid while the user is still typing, so they must not spam
        // the global on-screen debug log - the editor already shows the error inline. Main-thread
        // only, set and cleared around the validation call.
        public static bool SuppressDebugMessages;

        private static void LogParseMessage(string message, int seconds = 5, Color color = default)
        {
            if (SuppressDebugMessages) return;
            if (color == default) DebugWindow.LogMsg(message, seconds);
            else DebugWindow.LogMsg(message, seconds, color);
        }

        private static readonly string[] Operations =
        {
            OPERATION_NONEQUALITY,
            OPERATION_LESSEQUAL,
            OPERATION_BIGGERQUAL,
            OPERATION_NOTCONTAINS,
            OPERATION_EQUALITY,
            OPERATION_BIGGER,
            OPERATION_LESS,
            OPERATION_CONTAINS
        };

        public static List<CustomFilter> Parse(string[] filtersLines)
        {
            return Parse(filtersLines, out _);
        }

        public static List<CustomFilter> Parse(string[] filtersLines, out List<string> errors)
        {
            var allFilters = new List<CustomFilter>();
            errors = new List<string>();

            for (var i = 0; i < filtersLines.Length; ++i)
            {
                var filterLine = filtersLines[i];

                filterLine = filterLine.Replace("\t", "");

                if (filterLine.StartsWith(COMMENTSYMBOL)) continue;
                if (filterLine.StartsWith(COMMENTSYMBOLALT)) continue;

                if (filterLine.Replace(" ", "").Length == 0) continue;

                var nameIndex = filterLine.IndexOf(SYMBOL_NAMEDIVIDE);

                if (nameIndex == -1)
                {
                    var msg = $"Line {i + 1}: {filterLine}";
                    errors.Add(msg);
                    LogParseMessage("Filter parser: Cannot find a filter name in line: " + (i + 1), 5);
                    continue;
                }

                var newFilter = new CustomFilter { Name = filterLine.Substring(0, nameIndex).Trim(), Index = i + 1 };

                var filterCommandsLine = filterLine.Substring(nameIndex + 1);

                var submenuIndex = filterCommandsLine.IndexOf(SYMBOL_SUBMENUNAME);

                if (submenuIndex != -1)
                {
                    newFilter.SubmenuName = filterCommandsLine.Substring(submenuIndex + 1);
                    filterCommandsLine = filterCommandsLine.Substring(0, submenuIndex);
                }

                var filterCommands = filterCommandsLine.Split(SYMBOL_COMMANDSDIVIDE);
                newFilter.Commands = filterCommandsLine;

                var filterErrorParse = false;

                foreach (var command in filterCommands)
                {
                    if (string.IsNullOrEmpty(command.Replace(" ", ""))) continue;

                    if (command.Contains(SYMBOL_COMMAND_FILTER_OR))
                    {
                        var orFilterCommands = command.Split(SYMBOL_COMMAND_FILTER_OR);
                        var newOrFilter = new BaseFilter { BAny = true };
                        newFilter.Filters.Add(newOrFilter);

                        foreach (var t in orFilterCommands)
                        {
                            if (ProcessCommand(newOrFilter, t)) continue;
                            filterErrorParse = true;
                            break;
                        }

                        if (filterErrorParse) break;
                    }
                    else
                    {
                        if (ProcessCommand(newFilter, command)) continue;

                        filterErrorParse = true;
                        break;
                    }
                }

                if (!filterErrorParse)
                {
                    allFilters.Add(newFilter);
                }
                else
                {
                    errors.Add($"Line {i + 1}: {filterLine}");
                    LogParseMessage($"Line: {i + 1}", 5, Color.Red);
                }
            }

            return allFilters;
        }

        private static readonly (string Keyword, Func<bool, BoolFilter> Factory)[] BooleanKeywords =
        {
            ("affinity", b => new BoolFilter { Selector = d => true, Value = true, IsAffinity = true }),
            ("shifting", b => new BoolFilter { Selector = d => true, Value = true, IsShifting = true }),
            ("elderGuardianMap", b => new BoolFilter { Selector = d => d.isElderGuardianMap, Value = b }),
            ("elder", b => new BoolFilter { Selector = d => d.isElder, Value = b }),
            ("shaper", b => new BoolFilter { Selector = d => d.isShaper, Value = b }),
            ("crusader", b => new BoolFilter { Selector = d => d.isCrusader, Value = b }),
            ("hunter", b => new BoolFilter { Selector = d => d.isHunter, Value = b }),
            ("redeemer", b => new BoolFilter { Selector = d => d.isRedeemer, Value = b }),
            ("warlord", b => new BoolFilter { Selector = d => d.isWarlord, Value = b }),
            ("synthesised", b => new BoolFilter { Selector = d => d.Synthesised, Value = b }),
            ("identified", b => new BoolFilter { Selector = d => d.BIdentified, Value = b }),
            ("corrupted", b => new BoolFilter { Selector = d => d.isCorrupted, Value = b }),
            ("influenced", b => new BoolFilter { Selector = d => d.isInfluenced, Value = b }),
            ("blightedMap", b => new BoolFilter { Selector = d => d.isBlightMap, Value = b })
        };

        private static bool TryParseInt(string s, out int result)
        {
            if (int.TryParse(s, out result)) return true;
            LogParseMessage($"Filter parser: Not a number: {s}", 5, Color.Red);
            return false;
        }

        // Wires up a numeric parameter: the int getter, the parsed compare value, and a string
        // view of the same getter (so string operations like '=' still work against it). Returns
        // false (already logged) when the value is not an integer.
        private static bool SetIntParameter(FilterParameterCompare c, Func<ItemData, int> getter, string value)
        {
            c.IntParameter = getter;
            if (!TryParseInt(value, out var n)) return false;
            c.CompareInt = n;
            c.StringParameter = d => getter(d).ToString();
            return true;
        }

        private static readonly Regex AffixParameterRegex =
            new Regex(@"(?=.*affix(?:\[\d+\])?=)affix(?:\[(\d+)\])?=", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool ProcessCommand(BaseFilter newFilter, string command)
        {
            command = command.Trim();

            // Boolean keywords are whole commands (optionally negated with '!'), e.g. "influenced"
            // or "!corrupted". Match the token exactly so a value/base/path/name that merely contains
            // a keyword (e.g. BaseName=Synthesised, Path^.../Crusader, Name=Elder Bow) is not silently
            // misrouted into a boolean filter.
            var negated = command.Length > 0 && command[0] == SYMBOL_NOT;
            var token = negated ? command.Substring(1) : command;
            foreach (var (keyword, factory) in BooleanKeywords)
            {
                if (token.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    newFilter.Filters.Add(factory(!negated));
                    return true;
                }
            }

            string parameter;
            string operation;
            string stringValue;
            string value;
            string affixValueIndex;

            if (!ParseCommand(command, out parameter, out operation, out stringValue, out value, out affixValueIndex))
            {
                LogParseMessage($"Unknown operation: {command}", 5, Color.Red);
                return false;
            }

            var stringComp = new FilterParameterCompare { CompareString = value };

            switch (parameter.ToLower())
            {
                case PARAMETER_CLASSNAME:
                    stringComp.StringParameter = data => data.ClassName;
                    break;
                case PARAMETER_BASENAME:
                    stringComp.StringParameter = data => data.BaseName;
                    break;
                case PARAMETER_NAME:
                    stringComp.StringParameter = data => data.Name;
                    break;
                case PARAMETER_PATH:
                    stringComp.StringParameter = data => data.Path;
                    break;
                case PARAMETER_VISCERASLOT:
                    // Slots come from the game's "viscera_arm/head/leg/body" tags and are lowercased
                    // by ItemData, so fold the comparison value to match ("VisceraSlot=Arm" works).
                    stringComp.StringParameter = data => data.VisceraSlot;
                    stringComp.CompareString = value.ToLowerInvariant();
                    break;
                case PARAMETER_RARITY:
                    stringComp.StringParameter = data => data.Rarity.ToString();
                    break;
                case PARAMETER_QUALITY:
                    if (!SetIntParameter(stringComp, data => data.ItemQuality, value)) return false;
                    break;
                case PARAMETER_MapTier:
                    if (!SetIntParameter(stringComp, data => data.MapTier, value)) return false;
                    break;
                case PARAMETER_ILVL:
                    if (!SetIntParameter(stringComp, data => data.ItemLevel, value)) return false;
                    break;
                case PARAMETER_NUMBER_OF_SOCKETS:
                    if (!SetIntParameter(stringComp, data => data.NumberOfSockets, value)) return false;
                    break;
                case PARAMETER_LARGEST_LINK_SIZE:
                    if (!SetIntParameter(stringComp, data => data.LargestLinkSize, value)) return false;
                    break;
                case PARAMETER_MODAFFIX:
                    if (!TryParseInt(affixValueIndex, out var affixIndex)) return false;
                    stringComp.CompareAffixModIndex = affixIndex;
                    stringComp.ModsParameter = data => data.ItemMods;
                    if (!TryParseInt(value, out var affixValue)) return false;
                    stringComp.CompareAffixValue = affixValue;
                    stringComp.CompareAffixString = stringValue;
                    break;
                case PARAMETER_VEILED:
                    if (!SetIntParameter(stringComp, data => data.Veiled, value)) return false;
                    break;
                case PARAMETER_FRACTUREDMODS:
                    if (!SetIntParameter(stringComp, data => data.Fractured, value)) return false;
                    break;
                case PARAMETER_DELIRIUMREWARDSAMOUNT:
                    if (!SetIntParameter(stringComp, data => data.DeliriumStacks, value)) return false;
                    break;
                case PARAMETER_SCOURGETIER:
                    if (!SetIntParameter(stringComp, data => data.ScourgeTier, value)) return false;
                    break;
                case PARAMETER_SKILLGEMLEVEL:
                    if (!SetIntParameter(stringComp, data => data.SkillGemLevel, value)) return false;
                    break;

                default:
                    LogParseMessage($"Filter parser: Unknown parameter: {parameter}", 10);
                    return false;
            }

            if (parameter.ToLower() != PARAMETER_MODAFFIX)
            {
                switch (operation.ToLower())
                {
                    case OPERATION_EQUALITY:
                        stringComp.CompDeleg = data => stringComp.StringParameter(data).Equals(stringComp.CompareString);
                        break;
                    case OPERATION_NONEQUALITY:
                        stringComp.CompDeleg = data => !stringComp.StringParameter(data).Equals(stringComp.CompareString);
                        break;
                    case OPERATION_CONTAINS:
                        stringComp.CompDeleg = data => stringComp.StringParameter(data).Contains(stringComp.CompareString);
                        break;
                    case OPERATION_NOTCONTAINS:
                        stringComp.CompDeleg = data => !stringComp.StringParameter(data).Contains(stringComp.CompareString);
                        break;

                    case OPERATION_BIGGER:
                        if (stringComp.IntParameter == null)
                        {
                            LogParseMessage(
                                $"Filter parser error: Cannot compare a string parameter with {OPERATION_BIGGER} (numerical) operation. Statement: {command}",
                                10);

                            return false;
                        }

                        stringComp.CompDeleg = data => stringComp.IntParameter(data) > stringComp.CompareInt;
                        break;
                    case OPERATION_LESS:
                        if (stringComp.IntParameter == null)
                        {
                            LogParseMessage(
                                $"Filter parser error: Cannot compare a string parameter with {OPERATION_LESS} (numerical) operation. Statement: {command}",
                                10);

                            return false;
                        }

                        stringComp.CompDeleg = data =>
                        {
                            return stringComp.IntParameter(data) < stringComp.CompareInt;
                        };
                        break;
                    case OPERATION_LESSEQUAL:
                        if (stringComp.IntParameter == null)
                        {
                            LogParseMessage(
                                $"Filter parser error: Cannot compare a string parameter with {OPERATION_LESSEQUAL} (numerical) operation. Statement: {command}",
                                10);

                            return false;
                        }

                        stringComp.CompDeleg = data => stringComp.IntParameter(data) <= stringComp.CompareInt;
                        break;

                    case OPERATION_BIGGERQUAL:
                        if (stringComp.IntParameter == null)
                        {
                            LogParseMessage(
                                $"Filter parser error: Cannot compare a string parameter with {OPERATION_BIGGERQUAL} (numerical) operation. Statement: {command}",
                                10);

                            return false;
                        }

                        stringComp.CompDeleg = data => stringComp.IntParameter(data) >= stringComp.CompareInt;
                        break;

                    default:
                        LogParseMessage($"Filter parser: Unknown operation: {operation}", 10);
                        return false;
                }
            }
            else
            {
                switch (operation.ToLower())
                {
                    case OPERATION_EQUALITY:
                        stringComp.CompDeleg = data => stringComp.ModsParameter(data)?.Any(m => AffixMatches(m, stringComp, (a, b) => a == b)) == true;
                        break;
                    case OPERATION_NONEQUALITY:
                        stringComp.CompDeleg = data => stringComp.ModsParameter(data)?.Any(m => AffixMatches(m, stringComp, (a, b) => a != b)) == true;
                        break;
                    case OPERATION_BIGGER:
                        stringComp.CompDeleg = data => stringComp.ModsParameter(data)?.Any(m => AffixMatches(m, stringComp, (a, b) => a > b)) == true;
                        break;
                    case OPERATION_LESS:
                        stringComp.CompDeleg = data => stringComp.ModsParameter(data)?.Any(m => AffixMatches(m, stringComp, (a, b) => a < b)) == true;
                        break;
                    case OPERATION_LESSEQUAL:
                        stringComp.CompDeleg = data => stringComp.ModsParameter(data)?.Any(m => AffixMatches(m, stringComp, (a, b) => a <= b)) == true;
                        break;
                    case OPERATION_BIGGERQUAL:
                        stringComp.CompDeleg = data => stringComp.ModsParameter(data)?.Any(m => AffixMatches(m, stringComp, (a, b) => a >= b)) == true;
                        break;
                    default:
                        LogParseMessage($"Filter parser: Unknown operation: {operation}", 10);
                        return false;
                }
            }
            newFilter.Filters.Add(stringComp);
            return true;
        }

        private static bool AffixMatches(ItemMod mod, FilterParameterCompare c, Func<int, int, bool> op)
        {
            return mod.Name.ToLower().Contains(c.CompareAffixString.ToLower())
                && mod.Values != null
                && mod.Values.Count > c.CompareAffixModIndex
                && op(mod.Values[c.CompareAffixModIndex], c.CompareAffixValue);
        }

        private static bool ParseCommand(string command, out string parameter, out string operation, out string stringValue, out string value, out string affixValueIndex)
        {
            parameter = "";
            operation = "";
            stringValue = "";
            value = "";
            affixValueIndex = "0";

            if (AffixParameterRegex.IsMatch(command))
            {
                var match = AffixParameterRegex.Match(command);
                if (match.Groups[1].Success)
                {
                    affixValueIndex = match.Groups[1].Value;
                }

                command = AffixParameterRegex.Replace(command, "");

                var operationIndex = -1;

                foreach (var t in Operations)
                {
                    operationIndex = command.IndexOf(t, StringComparison.Ordinal);

                    if (operationIndex == -1) continue;

                    operation = t;
                    break;
                }

                if (operationIndex == -1) return false;

                parameter = PARAMETER_MODAFFIX;
                stringValue = command.Substring(0, operationIndex).Trim();
                value = command.Substring(operationIndex + operation.Length).Trim();
            }
            else
            {
                var operationIndex = -1;

                foreach (var t in Operations)
                {
                    operationIndex = command.IndexOf(t, StringComparison.Ordinal);

                    if (operationIndex == -1) continue;

                    operation = t;
                    break;
                }

                if (operationIndex == -1) return false;

                parameter = command.Substring(0, operationIndex).Trim();
                value = command.Substring(operationIndex + operation.Length).Trim();
            }
            return true;
        }
    }
}