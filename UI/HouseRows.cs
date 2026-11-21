using System;
using System.Numerics;
using ExileCore.Shared.Nodes;
using Hexa.NET.ImGui;
using UiHelpers = SquirrelUi;
using Vector2N = System.Numerics.Vector2;

namespace SquirrelStash;

// Local v2 row helpers for dense settings pages. Same grid as the other
// squirrelrat plugins: control at x=0, label at x=44, track at x=270.
internal static class HouseRows
{
    private static string _editingSliderId;
    private static bool _sliderEditFocus;

    public static bool Toggle(string label, bool value, string id, string help = null)
    {
        var start = ImGui.GetCursorScreenPos();
        float lineH = ImGui.GetTextLineHeight();
        float rowH = lineH + 8f;
        float w = ImGui.GetContentRegionAvail().X;

        ImGui.SetCursorScreenPos(start);
        var pillValue = UiHelpers.PillMini(value, "st_pill_" + id, rowH);
        ImGui.SetCursorScreenPos(start + new Vector2N(44f, 0f));
        ImGui.InvisibleButton("##st_row_" + id, new Vector2N(w - 44f, rowH));
        bool rowClicked = ImGui.IsItemClicked();
        bool hovered = ImGui.IsItemHovered();

        ImGui.SetCursorScreenPos(start + new Vector2N(44f, (rowH - lineH) * 0.5f));
        ImGui.TextColored(hovered ? new Vector4(1f, 1f, 1f, 1f) : UiHelpers.Muted, label);
        if (help != null)
        {
            ImGui.SetCursorScreenPos(start + new Vector2N(44f + ImGui.CalcTextSize(label).X + 8f, (rowH - lineH) * 0.5f));
            UiHelpers.HelpMarker(help);
        }

        if (pillValue != value) value = pillValue;
        else if (rowClicked) value = !value;
        ImGui.SetCursorScreenPos(new Vector2N(start.X, start.Y + rowH));
        return value;
    }

    public static void NodeSlider(RangeNode<int> node, string label, string id, string help = null, bool disabled = false)
    {
        var value = node.Value;
        IntSlider(label, ref value, node.Min, node.Max, id, help, 44f, disabled);
        if (value != node.Value) node.Value = value;
    }

    public static void Hotkey(HotkeyNodeV2 node, string label, string id, string help = null)
    {
        var start = ImGui.GetCursorScreenPos();
        float h = ImGui.GetTextLineHeight() + 8f;
        var text = label + " [" + node.Value + "]";
        var textSize = ImGui.CalcTextSize(text);

        ImGui.PushStyleColor(ImGuiCol.Button, 0u);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, 0u);
        ImGui.PushStyleColor(ImGuiCol.Text, 0u);
        ImGui.SetCursorScreenPos(start + new Vector2N(34f, 0f));
        node.DrawPickerButton(new string(' ', Math.Max(8, text.Length + 4)) + "##" + id + "_picker");
        ImGui.PopStyleColor(4);

        ImGui.SetCursorScreenPos(start + new Vector2N(34f, 0f));
        UiHelpers.Button(text, id, 0);
        if (help != null)
        {
            ImGui.SetCursorScreenPos(start + new Vector2N(34f + textSize.X + 28f, (h - ImGui.GetTextLineHeight()) * 0.5f));
            UiHelpers.HelpMarker(help);
        }
        ImGui.SetCursorScreenPos(new Vector2N(start.X, start.Y + h));
    }

    public static bool ActionButton(string label, string id, int kind = 0, string help = null)
    {
        var start = ImGui.GetCursorScreenPos();
        bool clicked = UiHelpers.ActionButton(label, id, kind);
        if (help != null)
        {
            float buttonW = ImGui.CalcTextSize(label).X + 20f;
            ImGui.SetCursorScreenPos(start + new Vector2N(34f + buttonW + 8f, 4f));
            UiHelpers.HelpMarker(help);
        }
        ImGui.SetCursorScreenPos(new Vector2N(start.X, start.Y + ImGui.GetTextLineHeight() + 8f));
        return clicked;
    }

    public static bool GridCell(bool selected, string id, string tooltip, float size = 30f)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##grid_" + id, new Vector2N(size, size));
        bool clicked = ImGui.IsItemClicked();
        bool hovered = ImGui.IsItemHovered();

        uint fill = selected
            ? UiHelpers.U32(124, 92, 255, 230)
            : hovered
                ? UiHelpers.U32(61, 66, 79, 255)
                : UiHelpers.U32(42, 46, 53, 255);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2N(size, size), fill, 2f);

        if (selected)
        {
            var mark = "X";
            var markSize = ImGui.CalcTextSize(mark);
            dl.AddText(pos + new Vector2N((size - markSize.X) * 0.5f, (size - markSize.Y) * 0.5f),
                UiHelpers.U32(255, 255, 255, 255), mark);
        }

        if (hovered && !string.IsNullOrEmpty(tooltip)) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    public static void Choice(ref int index, string[] values, string label, string id, string help = null, float width = 260f)
    {
        var start = ImGui.GetCursorScreenPos();
        float lineH = ImGui.GetTextLineHeight();
        ImGui.SetCursorScreenPos(start + new Vector2N(44f, 0f));
        ImGui.TextColored(UiHelpers.Muted, label);
        if (help != null)
        {
            ImGui.SetCursorScreenPos(start + new Vector2N(44f + ImGui.CalcTextSize(label).X + 8f, 0f));
            UiHelpers.HelpMarker(help);
        }
        ImGui.SetCursorScreenPos(start + new Vector2N(270f, 0f));
        ImGui.SetNextItemWidth(width);
        ImGui.Combo("##" + id, ref index, values, values.Length);
        ImGui.SetCursorScreenPos(new Vector2N(start.X, start.Y + lineH + 8f));
    }

    public static void ListChoice(ListNode node, string label, string id, string help = null, bool disabled = false)
    {
        var start = ImGui.GetCursorScreenPos();
        float lineH = ImGui.GetTextLineHeight();
        ImGui.SetCursorScreenPos(start + new Vector2N(44f, 0f));
        ImGui.TextColored(UiHelpers.Muted, label);
        if (help != null)
        {
            ImGui.SetCursorScreenPos(start + new Vector2N(44f + ImGui.CalcTextSize(label).X + 8f, 0f));
            UiHelpers.HelpMarker(help);
        }
        ImGui.SetCursorScreenPos(start + new Vector2N(270f, 0f));
        ImGui.BeginDisabled(disabled);
        ImGui.SetNextItemWidth(260f);
        int index = Math.Max(0, node.Values.IndexOf(node.Value));
        if (ImGui.Combo("##" + id, ref index, node.Values.ToArray(), node.Values.Count))
            node.Value = node.Values[index];
        ImGui.EndDisabled();
        ImGui.SetCursorScreenPos(new Vector2N(start.X, start.Y + lineH + 8f));
    }

    public static bool IntSlider(string label, ref int value, int min, int max, string id,
        string help = null, float rightGutter = 44f, bool disabled = false, float trackStart = 270f)
    {
        var start = ImGui.GetCursorScreenPos();
        float lineH = ImGui.GetTextLineHeight();
        float h = lineH + 8f;
        float w = ImGui.GetContentRegionAvail().X;
        float valueRight = w - rightGutter;
        var maxValueText = Math.Max(Math.Abs((long)min), Math.Abs((long)max)).ToString();
        float valueColumnW = ImGui.CalcTextSize(maxValueText).X;
        float trackEnd = Math.Max(trackStart, valueRight - valueColumnW - 12f);
        float trackY = start.Y + h * 0.5f;

        ImGui.BeginDisabled(disabled);
        ImGui.SetCursorScreenPos(start + new Vector2N(trackStart, 0f));
        ImGui.InvisibleButton("##st_slider_" + id, new Vector2N(Math.Max(1f, trackEnd - trackStart), h));
        bool active = ImGui.IsItemActive();
        bool hovered = ImGui.IsItemHovered();

        var text = value.ToString();
        var textSize = ImGui.CalcTextSize(text);
        var textPos = new Vector2N(start.X + valueRight - textSize.X, start.Y + (h - lineH) * 0.5f);
        bool editing = _editingSliderId == id;

        if (!editing && ImGui.IsMouseHoveringRect(textPos - new Vector2N(6f, 3f), textPos + textSize + new Vector2N(6f, 3f))
            && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _editingSliderId = id;
            _sliderEditFocus = true;
            editing = true;
        }

        var dl = ImGui.GetWindowDrawList();
        uint accent = ImGui.GetColorU32(new Vector4(0.486f, 0.360f, 1f, 1f));
        uint trackCol = ImGui.GetColorU32(new Vector4(0.165f, 0.180f, 0.208f, 1f));
        uint labelCol = ImGui.GetColorU32(new Vector4(0.91f, 0.92f, 0.93f, 1f));
        uint valueCol = ImGui.GetColorU32(new Vector4(0.72f, 0.68f, 1f, 1f));

        float norm = max > min ? Math.Clamp((value - min) / (float)(max - min), 0f, 1f) : 0f;
        float lineStart = start.X + trackStart;
        float lineEnd = start.X + trackEnd;
        float dotX = lineStart + norm * (lineEnd - lineStart);

        dl.AddText(new Vector2N(start.X + 44f, start.Y + (h - lineH) * 0.5f), labelCol, label);
        var labelW = ImGui.CalcTextSize(label).X;
        if (help != null && 44f + labelW + 8f < trackStart - 8f)
        {
            ImGui.SetCursorScreenPos(new Vector2N(start.X + 44f + labelW + 8f, start.Y + (h - lineH) * 0.5f));
            UiHelpers.HelpMarker(help);
        }
        dl.AddLine(new Vector2N(lineStart, trackY), new Vector2N(lineEnd, trackY), trackCol, 3f);
        if (dotX > lineStart) dl.AddLine(new Vector2N(lineStart, trackY), new Vector2N(dotX, trackY), accent, 3f);
        dl.AddCircleFilled(new Vector2N(dotX, trackY), hovered || active ? 6.5f : 5.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)));
        if (!editing) dl.AddText(textPos, valueCol, text);

        if (editing)
        {
            ImGui.SetCursorScreenPos(new Vector2N(start.X + valueRight - 64f, start.Y + (h - lineH) * 0.5f - 2f));
            ImGui.SetNextItemWidth(64f);
            bool justOpened = false;
            if (_sliderEditFocus)
            {
                ImGui.SetKeyboardFocusHere();
                _sliderEditFocus = false;
                justOpened = true;
            }
            float editValue = value;
            bool enter = ImGui.InputFloat("##st_edit_" + id, ref editValue, 0f, 0f, "%g", ImGuiInputTextFlags.EnterReturnsTrue);
            value = Math.Clamp((int)Math.Round((double)editValue), min, max);
            if (enter || (!justOpened && ImGui.IsItemDeactivated()))
            {
                value = Math.Clamp((int)Math.Round((double)editValue), min, max);
                _editingSliderId = null;
            }
        }
        else if (active && max > min && lineEnd > lineStart)
        {
            float mx = ImGui.GetMousePos().X;
            float nn = Math.Clamp((mx - lineStart) / (lineEnd - lineStart), 0f, 1f);
            value = (int)Math.Round((double)(min + nn * (max - min)));
        }

        ImGui.EndDisabled();
        ImGui.SetCursorScreenPos(new Vector2N(start.X, start.Y + h));
        return true;
    }
}
