using System.Text.Json.Serialization;
using Xbim.Flex.Services.Abstractions;

namespace Xbim.IDS.Generator.Dfe
{
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

        [JsonPropertyName("Number of Storeys")]
        public int NumberOfStoreys { get; set; } = 3;

        public static async Task<DfeConfig> Read(IAirTableService airtable, string table)
        {
            var configs = airtable.ListRecordsAsync<DfeConfig>(table);
            await foreach (var c in configs) return c;

            return null;
        }
    }
}
