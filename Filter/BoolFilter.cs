using System;

namespace SquirrelStash
{
    public class BoolFilter : IIFilter
    {
        public Func<ItemData, bool> Selector { get; set; }
        public bool Value { get; set; }
        public bool IsAffinity { get; set; }
        public bool IsShifting { get; set; }

        public bool CompareItem(ItemData itemData)
        {
            return Selector(itemData) == Value;
        }
    }
}
