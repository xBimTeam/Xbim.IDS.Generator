using System.Text.Json.Serialization;

namespace Xbim.IDS.Generator.Sample
{
    internal class SampleConfig
    {
        [JsonPropertyName("Project Name")]
        public string ProjectName { get; set; } = "{{ProjectName}}";
        [JsonPropertyName("Project Description")]
        public string ProjectDescription { get; set; } = "{{ProjectDescription}}";
        [JsonPropertyName("Project Phase")]
        public string ProjectPhase { get; set; } = "{{ProjectPhase}}";

        [JsonPropertyName("Site Name")]
        public string SiteName { get; set; } = "{{SiteName}}";
        [JsonPropertyName("Site Description")]
        public string SiteDescription { get; set; } = "{{SiteDescription}}";

        [JsonPropertyName("Building Name")]
        public string BuildingName { get; set; } = "{{BuildingName}}";
        [JsonPropertyName("Building Description")]
        public string BuildingDescription { get; set; } = "{{BuildingDescription}}";

        [JsonPropertyName("Building Category")]
        public string BuildingCategory { get; set; } = "{{BuildingCategory}}";
    }
}
