using System.ComponentModel;

namespace Xbim.IDS.Generator.Common
{
    [Flags]
    public enum RibaStages
    {
        None = 0,
        [Description("Stage 1")]
        Stage1 = 1 << 1,
        [Description("Stage 2")]
        Stage2 = 1 << 2,
        [Description("Stage 3")]
        Stage3 = 1 << 3,
        [Description("Stage 4")]
        Stage4 = 1 << 4,
        [Description("Stage 5")]
        Stage5 = 1 << 5,
        [Description("Stage 6")]
        Stage6 = 1 << 6,
        [Description("Stage 7")]
        Stage7 = 1 << 7,

        Stage2Plus = Stage2 | Stage3Plus,
        Stage3Plus = Stage3 | Stage4Plus,
        Stage4Plus = Stage4 | Stage5Plus,
        Stage5Plus = Stage5 | Stage6Plus,
        Stage6Plus = Stage6 | Stage7,
        All = Stage1 | Stage2 | Stage3 | Stage4 | Stage5 | Stage6 | Stage7,
    }
}
