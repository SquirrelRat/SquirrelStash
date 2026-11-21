using System;
using System.Numerics;
using System.Text;
using ImGuiNET;
using SColor = SharpDX.Color;

namespace SquirrelStash;

// Minimal re-implementations of the few ExileImgui helpers the copied Editor.cs depends on, so the
// editor is self-contained and does not drag in the rest of that toolkit.

public static class EColor
{
    public static Vector4 ToVector4(SColor c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    public static uint U32(SColor c) => ImGui.GetColorU32(ToVector4(c));

    public static SColor Fade(SColor c, float a)
    {
        float clamped = Math.Clamp(a, 0f, 1f);
        return new SColor(c.R, c.G, c.B, (byte)Math.Round(c.A * clamped));
    }

    public readonly struct StyleColorScope : IDisposable
    {
        readonly int _n;

        public StyleColorScope(params (ImGuiCol col, uint rgba)[] colors)
        {
            colors ??= Array.Empty<(ImGuiCol, uint)>();
            _n = colors.Length;
            foreach (var (col, rgba) in colors) ImGui.PushStyleColor(col, rgba);
        }

        public void Dispose() => ImGui.PopStyleColor(_n);
    }
}

public static class Text
{
    public static string Ascii(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c < 128) { sb.Append(c); continue; }
            switch (c)
            {
                case '–': case '—': case '−': sb.Append('-'); break;
                case '→': sb.Append("->"); break;
                case '←': sb.Append("<-"); break;
                case '•': case '·': case '●': case '◇': case '▶': sb.Append('*'); break;
                case '✓': case '✔': case '×': sb.Append('x'); break;
                case '“': case '”': sb.Append('"'); break;
                case '‘': case '’': sb.Append('\''); break;
                default: sb.Append('?'); break;
            }
        }
        return sb.ToString();
    }
}

public static class Overlay
{
    public static void Chrome(ImDrawListPtr dl, Vector2 min, Vector2 max, SColor background, SColor border,
        float thickness = 1f, float radius = 0f)
    {
        if (background.A > 0) dl.AddRectFilled(min, max, EColor.U32(background), radius);
        if (thickness > 0) dl.AddRect(min, max, EColor.U32(border), radius, thickness, ImDrawFlags.None);
    }
}

public static class Combo
{
    public static bool Matches(string filter, string text)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return (text ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
