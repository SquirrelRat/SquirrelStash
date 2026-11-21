using System.Linq;

namespace SquirrelStash
{
    public class CustomFilter : BaseFilter
    {
        public string Name { get; set; }
        public ListIndexNode StashIndexNode { get; set; }
        public string SubmenuName { get; set; }
        public string Commands { get; set; }
        public int Index { get; set; }
        public bool AllowProcess => StashIndexNode.Index != -1;

        public bool IsAffinity => Filters.OfType<BaseFilter>().Any(ContainsAffinity);
        public bool IsShifting => Filters.OfType<BaseFilter>().Any(ContainsShifting);

        private static bool ContainsAffinity(IIFilter filter) =>
            filter is BoolFilter bf && bf.IsAffinity ||
            filter is BaseFilter nested && nested.Filters.Any(ContainsAffinity);

        private static bool ContainsShifting(IIFilter filter) =>
            filter is BoolFilter bf && bf.IsShifting ||
            filter is BaseFilter nested && nested.Filters.Any(ContainsShifting);
    }
}
