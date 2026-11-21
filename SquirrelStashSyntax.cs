using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SquirrelStash;

/// <summary>
/// Tokenizer and completions for SquirrelStash's own filter DSL: a comma/pipe separated list of
/// parameter commands, e.g. <c>ClassName=MapKey,!blightedMap,ilvl&gt;=60</c>.
/// </summary>
public static class SquirrelStashSyntax
{
    // Optional source of live inventory values for value-completion (ClassName/BaseName/Path/Name/Rarity).
    // Wired by SquirrelStashCore; when null or empty, value suggestions simply don't appear.
    public static Func<string, IEnumerable<string>> ValueProvider;

    private readonly record struct KeywordMeta(string Keyword, string Type, string Detail, bool CanHaveValues);

    static readonly KeywordMeta[] Metadata =
    {
        new("ClassName", "string", "Item class, e.g. StackableCurrency. '=' exact, '^' contains.", true),
        new("BaseName", "string", "Base item name, e.g. Portal Scroll. '=' exact, '^' contains.", true),
        new("Path", "string", "Full metadata path, e.g. Metadata/Items/Currency/CurrencyPortal.", true),
        new("Name", "string", "Displayed item name.", true),
        new("VisceraSlot", "string", "Wickerman Viscera slot: arm, head, leg or body.", true),
        new("ItemQuality", "number", "Item quality 0-20+.", false),
        new("Rarity", "number", "Normal, Magic, Rare or Unique.", true),
        new("ilvl", "number", "Item level.", false),
        new("Tier", "number", "Map tier.", false),
        new("NumberOfSockets", "number", "Total sockets.", false),
        new("NumberOfLinks", "number", "Largest link group.", false),
        new("Veiled", "number", "Veiled mod count.", false),
        new("Fractured", "number", "Fractured mod count.", false),
        new("SkillGemLevel", "number", "Skill gem level.", false),
        new("DeliriumRewards", "number", "Delirium reward count.", false),
        new("ScourgeTier", "number", "Scourge tier.", false),
        new("Affix", "number", "Mod value by index, e.g. affix[0]>=10.", false),
        new("Identified", "bool", "Item is identified. Negate with '!'.", false),
        new("Corrupted", "bool", "Item is corrupted.", false),
        new("Influenced", "bool", "Has any influence.", false),
        new("Elder", "bool", "Elder influence.", false),
        new("Shaper", "bool", "Shaper influence.", false),
        new("Crusader", "bool", "Crusader influence.", false),
        new("Hunter", "bool", "Hunter influence.", false),
        new("Redeemer", "bool", "Redeemer influence.", false),
        new("Warlord", "bool", "Warlord influence.", false),
        new("Synthesised", "bool", "Synthesised item.", false),
        new("BlightedMap", "bool", "Blighted (InfectedMap) map.", false),
        new("ElderGuardianMap", "bool", "Elder guardian map.", false),
        new("Affinity", "bool", "Stash-tab affinity (deposit without switching).", false),
        new("Shifting", "bool", "Shift-click deposit.", false),
    };

    static readonly Dictionary<string, KeywordMeta> MetaByKeyword =
        Metadata.ToDictionary(m => m.Keyword, StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> KeywordSet = new(Metadata.Select(m => m.Keyword), StringComparer.OrdinalIgnoreCase);

    static readonly string Operators = "=!<>^|,:[]";

    private static readonly string[] StringOps = { "=", "!=", "^", "!^" };
    private static readonly string[] NumberOps = { "=", "!=", "<", ">", "<=", ">=" };

    public static int Tokenize(string line, int stateIn, List<Token> into)
    {
        line ??= "";
        int i = 0;

        while (i < line.Length)
        {
            char c = line[i];

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                into.Add(new Token(i, line.Length - i, TokenKind.Comment));
                return 0;
            }

            if (c == '#')
            {
                into.Add(new Token(i, line.Length - i, TokenKind.Comment));
                return 0;
            }

            if (c == '"')
            {
                int j = i + 1;
                while (j < line.Length && line[j] != '"') j++;
                if (j >= line.Length) { into.Add(new Token(i, line.Length - i, TokenKind.String)); return 0; }
                into.Add(new Token(i, j - i + 1, TokenKind.String));
                i = j + 1;
                continue;
            }

            if (char.IsDigit(c))
            {
                int j = i;
                while (j < line.Length && char.IsDigit(line[j])) j++;
                into.Add(new Token(i, j - i, TokenKind.Number));
                i = j;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int j = i;
                while (j < line.Length && (char.IsLetterOrDigit(line[j]) || line[j] == '_')) j++;
                var word = line.Substring(i, j - i);
                var kind = KeywordSet.Contains(word) ? TokenKind.Keyword : TokenKind.Identifier;
                into.Add(new Token(i, j - i, kind));
                i = j;
                continue;
            }

            if (Operators.IndexOf(c) >= 0)
            {
                // two-character operators: !=  <=  >=  !^
                if (i + 1 < line.Length && (line[i + 1] == '=' || line[i + 1] == '^') && (c == '!' || c == '<' || c == '>'))
                {
                    into.Add(new Token(i, 2, TokenKind.Operator));
                    i += 2;
                }
                else
                {
                    into.Add(new Token(i, 1, TokenKind.Operator));
                    i++;
                }
                continue;
            }

            i++;   // whitespace and anything else stays uncolored
        }

        return 0;
    }

    public static void Complete(in CompletionContext ctx, List<Completion> into)
    {
        if (ctx.Trigger != '\0') return;

        var text = ctx.Text ?? "";
        var caret = Math.Clamp(ctx.Caret, 0, text.Length);
        var upToCaret = text.Substring(0, caret);
        var nl = upToCaret.LastIndexOf('\n');
        var linePrefix = nl < 0 ? upToCaret : upToCaret.Substring(nl + 1);

        var lastSep = Math.Max(linePrefix.LastIndexOf(','), linePrefix.LastIndexOf('|'));
        var chunk = lastSep >= 0 ? linePrefix.Substring(lastSep + 1) : linePrefix;
        chunk = chunk.TrimStart();

        var word = ctx.Word ?? "";

        if (chunk.StartsWith("!"))
        {
            AddKeywords(into, word, true);
            return;
        }

        var m = Regex.Match(chunk, @"^(?<prop>[A-Za-z]+)(?<op>!=|!^|<=|>=|=|\^|<|>)?");
        if (m.Success)
        {
            var prop = m.Groups["prop"].Value;
            var op = m.Groups["op"].Value;
            var hasMeta = MetaByKeyword.TryGetValue(prop, out var meta);

            if (op.Length > 0)
            {
                if (hasMeta && meta.CanHaveValues)
                    AddValues(into, prop, meta, word);
                return;
            }

            if (word.Length > 0 && hasMeta && word.Equals(prop, StringComparison.OrdinalIgnoreCase))
            {
                AddOperators(into, meta);
                return;
            }
        }

        AddKeywords(into, word, false);
    }

    private static void AddKeywords(List<Completion> into, string word, bool onlyBooleans)
    {
        foreach (var meta in Metadata)
        {
            if (onlyBooleans && meta.Type != "bool") continue;
            if (!meta.Keyword.StartsWith(word, StringComparison.OrdinalIgnoreCase)) continue;
            if (meta.Keyword.Equals(word, StringComparison.OrdinalIgnoreCase)) continue;
            into.Add(new Completion(meta.Keyword, meta.Detail, TokenKind.Keyword));
        }
    }

    private static void AddOperators(List<Completion> into, KeywordMeta meta)
    {
        var ops = meta.Type == "string" ? StringOps : meta.Type == "number" ? NumberOps : Array.Empty<string>();
        foreach (var op in ops)
            into.Add(new Completion(meta.Keyword + op, op + " operator", TokenKind.Operator));
    }

    private static void AddValues(List<Completion> into, string prop, KeywordMeta meta, string word)
    {
        var values = new List<string>();
        if (meta.Keyword.Equals("VisceraSlot", StringComparison.OrdinalIgnoreCase))
            values.AddRange(new[] { "arm", "head", "leg", "body" });

        if (meta.Keyword.Equals("Rarity", StringComparison.OrdinalIgnoreCase))
            values.AddRange(new[] { "Normal", "Magic", "Rare", "Unique" });

        if (ValueProvider != null)
            values.AddRange(ValueProvider(prop.ToLowerInvariant()) ?? Enumerable.Empty<string>());

        foreach (var v in values.Distinct().Where(v => v.StartsWith(word, StringComparison.OrdinalIgnoreCase)))
            into.Add(new Completion(v, "value", TokenKind.Type));
    }
}
