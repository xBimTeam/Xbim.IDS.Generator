using System;

namespace Xbim.IDS.Generator.Common
{
    /// <summary>
    /// Determines the mode/pass of the generation allowing subsets of the requirements to be packaged separately from a monolithic
    /// IDS file
    /// </summary>
    /// <remarks>This cross cuts the <see cref="RibaStages"/> filtering</remarks>
    [Flags]
    public enum GenerationPass
    {
        /// <summary>
        /// The core requirements
        /// </summary>
        Core = 1 << 1,
        /// <summary>
        /// Complex requirements - e.g naming
        /// </summary>
        Complex = 1 << 2,
        /// <summary>
        /// All requirements
        /// </summary>
        All = Core | Complex
    }
}
