using SharpDX;

namespace SquirrelStash
{
    public class FilterResult
    {
        public FilterResult(CustomFilter filter, ItemData itemData)
        {
            Filter = filter;
            ItemData = itemData;
            StashIndex = filter.StashIndexNode.Index;
            ClickPos = itemData.GetClickPosCache();
            SkipSwitchTab = Filter.IsAffinity;
            ShiftForStashing = Filter.IsShifting;
        }

        public CustomFilter Filter { get; }
        public ItemData ItemData { get; }
        public int StashIndex { get; }
        public Vector2 ClickPos { get; }
        public bool SkipSwitchTab { get; }
        public bool ShiftForStashing { get; }
    }
}
