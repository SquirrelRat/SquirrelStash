using System.Collections.Generic;
using JM.LinqFaster;

namespace SquirrelStash
{
    public class BaseFilter : IIFilter
    {
        public List<IIFilter> Filters { get; } = new List<IIFilter>();
        public bool BAny { get; set; }
        public bool CompareItem(ItemData itemData)
        {
            if (Filters.Count == 0) return false;
            return BAny ? Filters.AnyF(x => x.CompareItem(itemData)) : Filters.AllF(x => x.CompareItem(itemData));
        }
    }
}
