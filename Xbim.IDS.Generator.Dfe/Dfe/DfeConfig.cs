using System.Text.Json.Serialization;
#if AirTable
using Xbim.Flex.Services.Abstractions;
#endif

namespace Xbim.IDS.Generator.Dfe
{
    public enum ImrVersion { S25, S21 }

    /// <summary>
    /// Runtime options for the DfE generator (resolved from CLI args / DI).
    /// Kept separate from DfeConfig so template-token configs stay clean.
    /// </summary>
    internal class DfeOptions
    {
        public ImrVersion Version { get; set; } = ImrVersion.S25;
        public string Status { get; set; } = "Sn";
        public string Revision { get; set; } = "Pnn";
        public int? BuildingStoreys { get; set; }
        /// <summary>Uniclass 2015 version for SL and EN tables, e.g. "1_32". Null = latest.</summary>
        public string? UniclassVersion { get; set; }
        /// <summary>NRM edition year, e.g. "2016". Null = latest.</summary>
        public string? NrmVersion { get; set; }
        /// <summary>SFG20 release year, e.g. "2023". Null = latest.</summary>
        public string? Sfg20Version { get; set; }
        /// <summary>Root folder for all outputs. Null = "Outputs" (relative to working directory).</summary>
        public string? OutputPath { get; set; }
        /// <summary>Include RIBA Stage 1 outputs (off by default; not yet fully implemented).</summary>
        public bool IncludeStage1 { get; set; } = false;
        /// <summary>Include RIBA Stage 2 outputs (off by default; not yet fully implemented).</summary>
        public bool IncludeStage2 { get; set; } = false;
        /// <summary>Include RIBA Stage 6 outputs (off by default; same check coverage as Stage 5).</summary>
        public bool IncludeStage6 { get; set; } = false;
    }

    /// <summary>
    /// Dfe Project Config
    /// </summary>
    public class DfeConfig
    {
        [JsonPropertyName("Project Name")]
        public string ProjectName { get; set; } = "{{IfcProjectName}}";
        [JsonPropertyName("Project Description")]
        public string ProjectDescription { get; set; } = "{{IfcProjectDescription}}";
        [JsonPropertyName("Project Phase")]
        public string ProjectPhase { get; set; } = "{{IfcProjectPhase}}";

        [JsonPropertyName("Site Name")]
        public string SiteName { get; set; } = "{{IfcSiteName}}";
        [JsonPropertyName("Site Description")]
        public string SiteDescription { get; set; } = "{{IfcSiteDescription}}";

        [JsonPropertyName("Building Name")]
        public string BuildingName { get; set; } = "{{IfcBuildingName}}";
        [JsonPropertyName("Building Description")]
        public string BuildingDescription { get; set; } = "{{IfcBuildingDescription}}";

        [JsonPropertyName("Building Category")]
        public string BuildingCategory { get; set; } = "{{IfcBuildingClassificationReference}}";

        [JsonPropertyName("Block Construction Type")]
        public string BuildingBlockConstructionType { get; set; } = "{{BuildingBlockConstructionType}}";

        [JsonPropertyName("Max Block Height")]
        public double? BuildingMaximumBlockHeight { get; set; }

        [JsonPropertyName("Number of Storeys")]
        public int? BuildingNumberOfStoreys { get; set; }

        [JsonPropertyName("Building UPRN")]
        public string BuildingUPRN { get; set; } = "{{IfcBuildingUPRN}}";

        public int NumberOfStoreys { get; set; } = 3;

        [JsonPropertyName("Level 00 Height")]
        public string Level00Height { get; set; } = "{{IfcBuildingStorey.Level 00.Height}}";
        [JsonPropertyName("Level 01 Height")]
        public string Level01Height { get; set; } = "{{IfcBuildingStorey.Level 01.Height}}";
        [JsonPropertyName("Level 02 Height")]
        public string Level02Height { get; set; } = "{{IfcBuildingStorey.Level 02.Height}}";
        [JsonPropertyName("Level 03 Height")]
        public string Level03Height { get; set; } = "{{IfcBuildingStorey.Level 03.Height}}";
        [JsonPropertyName("Level 04 Height")]
        public string Level04Height { get; set; } = "{{IfcBuildingStorey.Level 04.Height}}";

#if AirTable
        public static async Task<DfeConfig> Read(IAirTableService airtable, string table)
        {
            var configs = airtable.ListRecordsAsync<DfeConfig>(table);
            await foreach (var c in configs) return c;

            return null;
        }
#endif
    }
}
