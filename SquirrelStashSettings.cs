using System.Collections.Generic;
using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace SquirrelStash
{
    public class SquirrelStashSettings : ISettings
    {
        public const string WhenDoneBackToOriginal = "Go back to original panel";
        public const string WhenDoneGoToTab = "Go to tab";
        public const string WhenDoneNone = "None";
        public const string TabSwitchClick = "Click";
        public const string TabSwitchArrow = "Arrow keys";

        public Dictionary<string, ListIndexNode> CustomFilterOptions { get; set; } =
            new Dictionary<string, ListIndexNode>();

        public SquirrelStashSettings()
        {
            Enable = new ToggleNode(false);
            DropHotkey = new HotkeyNodeV2(Keys.F3);
            SwitchFilterhotkey = new HotkeyNodeV2(Keys.F4);
            TestFilterHotkey = new HotkeyNodeV2(Keys.F6);
            MinDelay = new RangeNode<int>(30, 0, 500);
            MaxDelay = new RangeNode<int>(90, 0, 500);
            TabLoadTimeout = new RangeNode<int>(5000, 1000, 10000);
            HoverInfo = new ToggleNode(true);
            MaxRetryAttempts = new RangeNode<int>(2, 1, 5);
            WhenDone = new ListNode
            {
                Value = WhenDoneBackToOriginal,
                Values = new List<string> { WhenDoneBackToOriginal, WhenDoneGoToTab, WhenDoneNone }
            };
            TabToVisitWhenDone = new RangeNode<int>(0, 0, 40);
            TabSwitchMode = new ListNode
            {
                Value = TabSwitchClick,
                Values = new List<string> { TabSwitchClick, TabSwitchArrow }
            };
        }


        [Menu("Stash Hotkey")] 
        public HotkeyNodeV2 DropHotkey { get; set; }
        [Menu("Switch Filter Hotkey")]
        public HotkeyNodeV2 SwitchFilterhotkey { get; set; }

        [Menu("Test Filter Hotkey", "Open the filter workbench window (test queries against your inventory).")]
        public HotkeyNodeV2 TestFilterHotkey { get; set; }

        [Menu("Min Delay", "Minimum pause between actions (ms). Sampled randomly for each action.")]
        public RangeNode<int> MinDelay { get; set; }
        [Menu("Max Delay", "Maximum pause between actions (ms). Sampled randomly for each action.")]
        public RangeNode<int> MaxDelay { get; set; }
        [Menu("Tab load timeout", "How long to wait for a stash tab to become ready before skipping an item (ms).")]
        public RangeNode<int> TabLoadTimeout { get; set; }

        [Menu("Hover info", "Show item details when hovering inventory while the test filter window is open.")]
        public ToggleNode HoverInfo { get; set; }

        [Menu("Max retry attempts", "How many stash passes to run for items that failed to drop.")]
        public RangeNode<int> MaxRetryAttempts { get; set; }

        [Menu("When done",
            "What to do after all items have been dropped to their tabs.")]
        public ListNode WhenDone { get; set; }

        [Menu("Go to tab (index)",
            "Only used when 'When done' is 'Go to tab'. 0 = first stash tab.")]
        public RangeNode<int> TabToVisitWhenDone { get; set; }

        [Menu("Tab switch mode",
            "How stash tabs are switched. Click is default.")]
        public ListNode TabSwitchMode { get; set; }
        public ToggleNode Enable { get; set; }

        public string ActiveProfile { get; set; } = "Default";

        public int[,] IgnoredCells { get; set; } =
        {
            {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1},
            {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1},
            {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1},
            {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1},
            {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1}
        };
        
    }
}