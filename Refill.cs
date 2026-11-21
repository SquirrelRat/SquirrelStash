using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpDX;

namespace SquirrelStash
{
    public class RefillProcessor
    {
        public string Name;
        public string CurrencyClass;
        public int Target = 40;
        public Point Cell = new Point(-1, -1);

        //Temp values (reset each run):
        public int OwnedCount;
        public int StackSize;
        public Point CurrentCell = new Point(-1, -1);
        public Vector2 ClickPos;

        public void Clear()
        {
            OwnedCount = 0;
            StackSize = 0;
            CurrentCell = new Point(-1, -1);
            ClickPos = Vector2.Zero;
        }
    }

    public static class RefillFile
    {
        // Line format: Name:BaseName,Target[,CellX,CellY]  (CellX 1-12, CellY 1-5, optional)
        public static List<RefillProcessor> Parse(string path)
        {
            var refills = new List<RefillProcessor>();
            if (!File.Exists(path)) return refills;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Replace("\t", "").Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#") || line.StartsWith("//")) continue;

                var colon = line.IndexOf(':');
                if (colon == -1) continue;

                var name = line.Substring(0, colon).Trim();
                var parts = line.Substring(colon + 1).Split(',');

                if (parts.Length < 2) continue;

                var refill = new RefillProcessor
                {
                    Name = name,
                    CurrencyClass = parts[0].Trim()
                };
                if (string.IsNullOrEmpty(refill.CurrencyClass)) continue;

                if (int.TryParse(parts[1].Trim(), out var target))
                    refill.Target = Math.Clamp(target, 1, 40);

                if (parts.Length >= 4 &&
                    int.TryParse(parts[2].Trim(), out var cx) &&
                    int.TryParse(parts[3].Trim(), out var cy) &&
                    cx >= 1 && cx <= 12 && cy >= 1 && cy <= 5)
                    refill.Cell = new Point(cx - 1, cy - 1);

                refills.Add(refill);
            }

            return refills;
        }

        public static void ParseSettings(string path, out bool enabled, out bool addFresh, out string sourceTab)
        {
            enabled = false;
            addFresh = true;
            sourceTab = "";
            if (!File.Exists(path)) return;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("!")) continue;
                if (line.StartsWith("!enabled=")) bool.TryParse(line.Substring("!enabled=".Length), out enabled);
                else if (line.StartsWith("!addFresh=")) bool.TryParse(line.Substring("!addFresh=".Length), out addFresh);
                else if (line.StartsWith("!sourceTab=")) sourceTab = line.Substring("!sourceTab=".Length).Trim();
            }
        }

        public static void Save(string path, List<RefillProcessor> refills, bool enabled, bool addFresh, string sourceTab)
        {
            var lines = new List<string>
            {
                "// SquirrelStash refills - managed by the in-game editor. Name:BaseName,Target,CellX,CellY",
                $"!enabled={enabled.ToString().ToLowerInvariant()}",
                $"!addFresh={addFresh.ToString().ToLowerInvariant()}",
                $"!sourceTab={sourceTab}"
            };
            foreach (var r in refills)
            {
                var cell = r.Cell.X >= 0 ? $",{r.Cell.X + 1},{r.Cell.Y + 1}" : "";
                lines.Add($"{r.Name}:{r.CurrencyClass},{r.Target}{cell}");
            }
            File.WriteAllLines(path, lines);
        }

        public static void SeedFromLegacy(string oldPath, string newPath)
        {
            // Old format: Name:ClassName,StackSize,InventoryX,InventoryY (1-based cells)
            var refills = new List<RefillProcessor>();
            if (File.Exists(oldPath))
            {
                foreach (var rawLine in File.ReadAllLines(oldPath))
                {
                    var line = rawLine.Replace("\t", "").Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#") || line.StartsWith("//")) continue;

                    var colon = line.IndexOf(':');
                    if (colon == -1) continue;

                    var name = line.Substring(0, colon).Trim();
                    var parts = line.Substring(colon + 1).Split(',');
                    if (parts.Length < 2) continue;

                    var refill = new RefillProcessor { Name = name, CurrencyClass = parts[0].Trim() };
                    if (string.IsNullOrEmpty(refill.CurrencyClass)) continue;
                    if (int.TryParse(parts[1].Trim(), out var size)) refill.Target = Math.Clamp(size, 1, 40);
                    if (parts.Length >= 4 &&
                        int.TryParse(parts[2].Trim(), out var cx) &&
                        int.TryParse(parts[3].Trim(), out var cy) &&
                        cx >= 1 && cx <= 12 && cy >= 1 && cy <= 5)
                        refill.Cell = new Point(cx - 1, cy - 1);

                    refills.Add(refill);
                }
            }

            if (refills.Count == 0)
            {
                refills.AddRange(new[]
                {
                    new RefillProcessor { Name = "Portal Scrolls", CurrencyClass = "Portal Scroll", Target = 40, Cell = new Point(11, 0) },
                    new RefillProcessor { Name = "Scrolls of Wisdom", CurrencyClass = "Scroll of Wisdom", Target = 40, Cell = new Point(11, 1) }
                });
            }

            Save(newPath, refills, false, true, "");
        }
    }
}
