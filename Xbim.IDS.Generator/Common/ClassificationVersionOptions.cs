namespace Xbim.IDS.Generator.Common;

/// <summary>
/// Version pins for classification code files used during IDS generation.
/// Null for any field means "use the latest (unversioned) file".
/// Version tokens use underscores in place of dots (e.g. "1_32" for v1.32)
/// to avoid conflicts with embedded-resource path separators.
/// </summary>
public class ClassificationVersionOptions
{
    /// <summary>Uniclass 2015 version for both SL and EN tables, e.g. "1_32".</summary>
    public string? UniclassVersion { get; set; }

    /// <summary>NRM edition year, e.g. "2016".</summary>
    public string? NrmVersion { get; set; }

    /// <summary>SFG20 release year, e.g. "2023".</summary>
    public string? Sfg20Version { get; set; }
}
