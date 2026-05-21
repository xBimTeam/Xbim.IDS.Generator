using IdsLib.IfcSchema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Xbim.Common.Step21;
using Xbim.IDS.Generator.Common;
using Xbim.IDS.Generator.Common.Internal;
using Xbim.IDS.Validator.Core.Interfaces;
using Xbim.Ifc4.Interfaces;
using Xbim.InformationSpecifications;
using Xbim.InformationSpecifications.Cardinality;
using static Xbim.InformationSpecifications.RequirementCardinalityOptions;


[assembly: InternalsVisibleTo("Xbim.IDS.Generator.Tests")]

namespace Xbim.IDS.Generator.Dfe
{
    /// <summary>
    /// Builds DfE IDS standards based on their PIS standards; also builds test models using the same standards
    /// </summary>
    public partial class DfeGenerator: BaseGenerator
    {
        public DfeGenerator(IServiceProvider provider)
        {
            this.provider = provider;   // Stopgap until fix up DI
            var options = provider.GetRequiredService<DfeOptions>();
            _version = options.Version;
            _status = options.Status;
            _revision = options.Revision;
            _buildingStoreys = options.BuildingStoreys;
            _classificationVersions = new ClassificationVersionOptions
            {
                UniclassVersion = options.UniclassVersion,
                NrmVersion = options.NrmVersion,
                Sfg20Version = options.Sfg20Version,
            };
            ClassificationVersions = _classificationVersions;
            _outputRoot = string.IsNullOrWhiteSpace(options.OutputPath) ? "Outputs" : options.OutputPath;
            _includeStage1 = options.IncludeStage1;
            _includeStage2 = options.IncludeStage2;
            _includeStage6 = options.IncludeStage6;
            WarnIfUniclassVersionMismatch(options);

            Xids.Settings.ApplyPrefixToSpecGroupFileNames = false;

            GenerationSchema = XbimSchemaVersion.Ifc2X3;    // The base IDS Schema we use to generate specifications from

            SupportedIfcSchemas = IdsLib.IfcSchema.IfcSchemaVersions.Ifc2x3;    // The IDS ifcVersion(s) to target. Can be overridden per spec

            UseIfc4TypesIn2x3 = true;   // Set true to extend entity types to IFC4 inferable entities. e.g. Enforce naming conventions on things like IfcAirTerminals that are not in 2x3 at the occurrence level
            ValidateIDSOutputs = false; // Set true to run ids-audit over the outputs 
            GroupCommonApplicableRequirements = true;  // Set true to group spec requirements that have a common applicability
        }

        internal const string spaceNameRegex = "((EX|00|01|02|03|RF|R2|ZZ|M0|M1|B1|B2)-)?[0-9]+[A-Za-z]?";
        internal static readonly Regex spaceNameExpression = new($"{spaceNameRegex}");
        internal static readonly Regex adsNameExpression = new(@".*(DfE ADS|dfe ads|DFE ADS).*");
        internal static readonly Regex spaceClassExpression = new(@".*(DfE Space|dfe space|DFE SPACE).*");
        internal static readonly Regex uniclassExpression = new(@".*[Uu]niclass.*");
        internal const string emailRegex = @"([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,})";
        internal static readonly Regex emailOrNaExpression = new($@"n\/a|{emailRegex}");
        internal static readonly Regex emailExpression = new(emailRegex);
        internal static readonly Regex numericExpression = new($@"\d+(\.\d+)?");
        internal static readonly Regex numericOrNaExpression = new($@"n\/a|\d+(\.\d+)?");
        internal static readonly Regex monetaryOrNaExpression = new($@"n\/a|�?\d+(\.\d{{2}})?");
        internal static readonly Regex textOrNaExpression = new($@"n\/a|(\w.*)+");
        internal static readonly Regex numberOrNaExpression = new($@"n\/a|(\d|-| |_)+");
        internal static readonly Regex dateOrDefaultExpression = new(@"1900-12-31T23:59:59(Z|[+-]\d{2}:\d{2})?|20\d{2}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12][0-9]|3[01])(?:T(?:[01][0-9]|2[0-3]):(?:[0-5][0-9]):(?:[0-5][0-9])(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?)?");
        internal static readonly Regex nonZeroDurationExpression = new(@"[1-9]\d*(\.\d+)?");
        internal static readonly Regex actualDateExpression = new(@"20\d{2}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12][0-9]|3[01])(?:T(?:[01][0-9]|2[0-3]):(?:[0-5][0-9]):(?:[0-5][0-9])(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?)?");
        internal static readonly Regex notNaExpression = new(@"[^n].*|n([^/].*)?|n\/([^a].*)?|n\/a.+");
        private readonly IServiceProvider provider;
        private readonly ImrVersion _version;
        private readonly string _status;
        private readonly string _revision;
        private readonly int? _buildingStoreys;
        private readonly ClassificationVersionOptions _classificationVersions;
        private readonly string _outputRoot;
        private readonly bool _includeStage1;
        private readonly bool _includeStage2;
        private readonly bool _includeStage6;

        /// <summary>
        /// Returns the version-appropriate explicit rule ID, or null (auto-counter) when no ID is defined for the current version.
        /// Mirrors the existing title: ternary pattern so a single call site handles both S21 and S25.
        /// </summary>
        private string? RuleId(string? s25Id, string? s21Id = null) =>
            _version == ImrVersion.S25 ? s25Id : s21Id;

        private static string HeightToken(string floorCode) =>
            $"{{{{IfcBuildingStorey.Level {floorCode}.Height}}}}";

        private string UniclassSystemLabel =>
            _version == ImrVersion.S25 ? "Uniclass Classification" : uniclassExpression.ToString();

        /// <summary>
        /// Returns the version-appropriate rule ID with the stage prefix replaced by the context's target stage.
        /// E.g. "4_07_14" becomes "5_07_14" when generating a Stage 5 IDS file.
        /// </summary>
        private string? RuleId(string? s25Id, SpecContext context, string? s21Id = null)
        {
            var id = RuleId(s25Id, s21Id);
            if (id == null || string.IsNullOrEmpty(context.StageId)) return id;
            var underscore = id.IndexOf('_');
            return underscore > 0 ? $"{context.StageId}_{id.Substring(underscore + 1)}" : id;
        }

        static readonly IDictionary<RibaStages, string> ribaStagesS21 = new Dictionary<RibaStages, string>()
        {
            [RibaStages.Stage1] = "RIBA Stage 1: Preparation and Brief",
            [RibaStages.Stage2] = "RIBA Stage 2: Concept Design",
            [RibaStages.Stage3] = "RIBA Stage 3: Spatial Coordination",
            [RibaStages.Stage4] = "RIBA Stage 4: Technical Design",
            [RibaStages.Stage5] = "RIBA Stage 5: Construction and Manufacturing",
            [RibaStages.Stage6] = "RIBA Stage 6: Handover and Close Out",
            [RibaStages.Stage7] = "RIBA Stage 7: Use",
        };

        static readonly IDictionary<RibaStages, string> ribaStagesS25 = new Dictionary<RibaStages, string>()
        {
            [RibaStages.Stage1] = "RIBA Stage 1 : Preparation and Brief",
            [RibaStages.Stage2] = "RIBA Stage 2 : Concept Design",
            [RibaStages.Stage3] = "RIBA Stage 3 : Spatial Coordination",
            [RibaStages.Stage4] = "RIBA Stage 4 : Technical Design",
            [RibaStages.Stage5] = "RIBA Stage 5 : Construction and Manufacturing",
            [RibaStages.Stage6] = "RIBA Stage 6 : Handover and Close Out",
            [RibaStages.Stage7] = "RIBA Stage 7 : Use",
        };

        private IDictionary<RibaStages, string> StageNames => _version == ImrVersion.S25 ? ribaStagesS25 : ribaStagesS21;

        static readonly IDictionary<string, Floor> floorDict = new Dictionary<string, Floor>()
        {
            ["XX"] = new Floor("XX", null, "No spatial sub-division is applicable", "n/a"),
            ["ZZ"] = new Floor("ZZ", null, "Multiple spatial sub-divisions are applicable", "n/a"),
            ["B2"] = new Floor("B2", "Level B2", "Basement level 2", "Floor"),
            ["B1"] = new Floor("B1", "Level B1", "Basement level 1", "Floor"),
            ["00"] = new Floor("00", "Level 00", "Base level of building", "Floor"),
            ["M0"] = new Floor("M0", "Level M0", "Mezzanine above base level", "Floor"),
            ["01"] = new Floor("01", "Level 01", "First floor", "Floor"),
            ["M1"] = new Floor("M1", "Level M1", "Mezzanine above first floor", "Floor"),
            ["02"] = new Floor("02", "Level 02", "Second floor", "Floor"),
            ["03"] = new Floor("03", "Level 03", "Third floor", "Floor"),
            ["04"] = new Floor("04", "Level 04", "Fourth floor", "Floor"),
            ["RF"] = new Floor("RF", "Level RF", "Main roof", "Roof"),
            ["R2"] = new Floor("R2", "Level R2", "Additional roof above main roof", "Roof"),

        };

        static readonly IDictionary<string, TypeMap> typeCodeDict = new Dictionary<string, TypeMap>()
        {
            ["Actuator"] = new TypeMap("ATR"),
            ["AirTerminal"] = new TypeMap("AIR")
                .OverrideWith("DIFFUSER", "(ASD|AED)")
                .OverrideWith("LINEARDIFFUSER", "(ASD|AED)")
                .OverrideWith("GRILLE", "AEG")
                .OverrideWith("LINEARGRILLE", "AEG")
                .OverrideWith("USERDEFINED", "ATG"),
            ["AirTerminalBox"] = new TypeMap("ATB")
                .OverrideWith("USERDEFINED", "(PLB|BC)"),
            ["AirToAirHeatRecovery"] = new TypeMap("ATA"),
            ["Alarm"] = new TypeMap("ALR"),
            ["Boiler"] = new TypeMap("BLR"),
            ["BuildingElementProxy"] = new TypeMap("OTH"),
            ["Chiller"] = new TypeMap("CHL"),
            ["Coil"] = new TypeMap("CCO")
                .OverrideWith("ELECTRICHEATINGCOIL", "HCO")
                .OverrideWith("GASHEATINGCOIL", "HCO")
                .OverrideWith("HEATINGCOIL", "HCO")
                .OverrideWith("STEAMHEATINGCOIL", "HCO")
                .OverrideWith("WATERHEATINGCOIL", "HCO")
                .OverrideWith("USERDEFINED", "RAC"),
            ["Compressor"] = new TypeMap("CMP"),
            ["Condenser"] = new TypeMap("CND"),
            ["Controller"] = new TypeMap("CRL")
                .OverrideWith("USERDEFINED", "(LCM|LCMD)"),
            ["CooledBeam"] = new TypeMap("CBM"),
            ["CoolingTower"] = new TypeMap("CTR"),
            ["Damper"] = new TypeMap("DMP")
                .OverrideWith("CONTROLDAMPER", "(CAV|MLO|MLH|VAV)")
                .OverrideWith("FIREDAMPER", "FDM")
                .OverrideWith("FIRESMOKEDAMPER", "FSD")
                .OverrideWith("SMOKEDAMPER", "(SMD|MSD)"),
            ["DiscreteAccessory"] = new TypeMap("DAC"),
            ["DistributionChamberElement"] = new TypeMap("DCE"),
            ["Door"] = new TypeMap("D").SpaceNaming(),
            ["ElectricAppliance"] = new TypeMap("EAP")
                .OverrideWith("RADIANTHEATER", "ERP")
                .OverrideWith("WATERHEATER", "(WHR|LWH)"),
            ["ElectricDistributionPoint"] = new TypeMap("EDP")
                .OverrideWith("ALARMPANEL", "EPB")
                .OverrideWith("CONTROLPANEL", "MCP")
                .OverrideWith("DISTRIBUTIONBOARD", "(EDB|ELB|EPB)")
                .OverrideWith("GASDETECTORPANEL", "EPB")
                .OverrideWith("MIMICPANEL", "EPB"),
            ["ElectricFlowStorageDevice"] = new TypeMap("EFS")
                .OverrideWith("BATTERY", "EHB")
                .OverrideWith("UPS", "UPS"),
            ["ElectricGenerator"] = new TypeMap("G"),
            ["ElectricHeater"] = new TypeMap("EHT")
                .OverrideWith("USERDEFINED", "THR"),
            ["ElectricMotor"] = new TypeMap("EMT"),
            ["ElectricTimeControl"] = new TypeMap("ETC"),
            ["EvaporativeCooler"] = new TypeMap("ECL"),
            ["Evaporator"] = new TypeMap("EVP"),
            ["Fan"] = new TypeMap("(EFN|SFN)"),
            ["Filter"] = new TypeMap("FLT")
                .OverrideWith("AIRPARTICLEFILTER", "APF")
                .OverrideWith("WATERFILTER", "WFT"),
            ["FireSuppressionTerminal"] = new TypeMap("FST"),
            ["FlowInstrument"] = new TypeMap("FIN"),
            ["FlowMeter"] = new TypeMap("FMT")
                .OverrideWith("GASMETER", "GMT")
                .OverrideWith("USERDEFINED", "HMT")
                .OverrideWith("WATERMETER", "WMT"),
            ["Furniture"] = new TypeMap("FRN"),
            ["FurnishingElement"] = new TypeMap("FRN"),
            ["GasTerminal"] = new TypeMap("GTM")
                .OverrideWith("GASBOOSTER", "BSR")
                .OverrideWith("USERDEFINED", "GRH"),
            ["HeatExchanger"] = new TypeMap("HEX")
                .OverrideWith("PLATE", "HEP"),
            ["Humidifier"] = new TypeMap("HUM"),
            ["LightFixture"] = new TypeMap("LFT"),
            //["Lamp"] = new TypeMap("LFT"),
            ["MotorConnection"] = new TypeMap("MCN"),
            ["Outlet"] = new TypeMap("OUT"),
            ["ProtectiveDevice"] = new TypeMap("PDV"),
            ["Pump"] = new TypeMap("PMP"),
            ["SanitaryTerminal"] = new TypeMap("SAN"),
            ["Sensor"] = new TypeMap("SNS")
                .OverrideWith("USERDEFINED", "LDS"),
            ["SpaceHeater"] = new TypeMap("SPH")
                .OverrideWith("CONVECTOR", "SCH")
                .OverrideWith("PANELRADIATOR", "SRH")
                .OverrideWith("SECTIONALRADIATOR", "SRH")
                .OverrideWith("TUBULARRADIATOR", "SRH")
                .OverrideWith("UNITHEATER", "(SUH|SGH)"),
            ["StackTerminal"] = new TypeMap("STM")
                .OverrideWith("COWL", "RCW"),
            ["SwitchingDevice"] = new TypeMap("SWD"),
            ["SystemFurnitureElement"] = new TypeMap("SFE"),
            ["Tank"] = new TypeMap("(EVL|CWT|PUT|BVL|TSR)")
                .OverrideWith("EXPANSION", "EVL")
                .OverrideWith("PREFORMED", "CWT")
                .OverrideWith("PRESSUREVESSEL", "PUT")
                .OverrideWith("SECTIONAL", "CWT")
                .OverrideWith("USERDEFINED", "(BVL|TSR)"),
            ["Transformer"] = new TypeMap("TRF"),
            ["TransportElement"] = new TypeMap("TRE")
                .OverrideWith("ELEVATOR", "ELE")
                .OverrideWith("ESCALATOR", "ESC")
                .OverrideWith("MOVINGWALKWAY", "MOV"),
            ["TubeBundle"] = new TypeMap("TBN"),
            ["UnitaryEquipment"] = new TypeMap("UEQ")
                .OverrideWith("AIRCONDITIONINGUNIT", "(IAC|OAC)")
                .OverrideWith("AIRHANDLER", "AHU"),
            ["Valve"] = new TypeMap("VLV")
                .OverrideWith("GASCOCK", "GSV")
                .OverrideWith("GASTAP", "GSV"),
            ["VibrationIsolator"] = new TypeMap("VIB"),
            ["WasteTerminal"] = new TypeMap("WTM"),
            ["Window"] = new TypeMap("W").SpaceNaming(),
        };


        
        
        private static readonly string[] RootTypes = [ 
            "IfcBuildingElementType", 
            "IfcFurnishingElementType", 
            "IfcCivilElementType", 
            "IfcDistributionElementType", 
            "IfcTransportElementType",
            "IfcDoorStyle", "IfcWindowStyle"
            ];


        /// <summary>
        /// Types that don't have PredefinedTypes / we don't care about the pre-defined types for naming
        /// </summary>
        static HashSet<string> enumTypeExceptions = new HashSet<string>
        {
            "Door",
            "DiscreteAccessory",
            "Fastener",
            "Furniture",
            "MechanicalFastener",
            "ReinforcingMesh",
            "SystemFurnitureElement",
            "TendonAnchor",
            "Window",

            "DoorStyle",
            "DoorType",
            "WindowStyle",
            "WindowType"
        };

        /// <summary>
        /// Builds and publishes the DfE IDS files
        /// </summary>
        /// <returns></returns>
        public override Task PublishIDS()
        {
            var config = new DfeConfig();       // initialise project specific config / or tokens

            var generations = new[] { GenerationPass.Core, GenerationPass.Complex, GenerationPass.All };

            foreach (var targetGeneration in generations)
            {

                var stages = new List<RibaStages>();
                if (_includeStage1) stages.Add(RibaStages.Stage1);
                if (_includeStage2) stages.Add(RibaStages.Stage2);
                stages.AddRange(new[] { RibaStages.Stage3, RibaStages.Stage4, RibaStages.Stage5 });
                if (_includeStage6) stages.Add(RibaStages.Stage6);
                foreach (var targetStage in stages)
                {
                    config.ProjectPhase = StageNames[targetStage];
                    var status = _status;
                    var revision = _revision;

                    int stageNum = targetStage switch
                    {
                        RibaStages.Stage1 => 1,
                        RibaStages.Stage2 => 2,
                        RibaStages.Stage3 => 3,
                        RibaStages.Stage4 => 4,
                        RibaStages.Stage5 => 5,
                        RibaStages.Stage6 => 6,
                        _ => throw new NotImplementedException($"Stage {targetStage} not mapped to a version number")
                    };
                    int passOffset = targetGeneration switch
                    {
                        GenerationPass.All => 0,
                        GenerationPass.Core => 1,
                        GenerationPass.Complex => 2,
                        _ => throw new NotImplementedException()
                    };
                    var version = stageNum * 10 + passOffset;

                    var ids = new Xids
                    {
                        // Note: not part of IDS standard - only in json export. Main public meta data on SpecificationGroup items
                        Guid = Guid.NewGuid().ToString(),
                        Name = $"{targetGeneration} DfE {_version} EIR model checks for {config.ProjectName} at {targetStage}",
                        Project = new Project   
                        {
                            Guid = Guid.NewGuid().ToString(),
                            Name = config.ProjectName,
                            Description = config.ProjectDescription
                        },
                        Stages = new List<string> { config.ProjectPhase },
                        SpecificationsGroups = new List<SpecificationsGroup>()
                    };


                    // Per-pass suffixes for titles, descriptions, and filenames
                    var titleSuffix = targetGeneration switch
                    {
                        GenerationPass.All     => "",
                        GenerationPass.Core    => ": Core Only",
                        GenerationPass.Complex => ": Nomenclature and Classification Only",
                        _ => throw new NotImplementedException(),
                    };
                    var descSuffix = targetGeneration switch
                    {
                        GenerationPass.All     => "",
                        GenerationPass.Core    => " - Core Only",
                        GenerationPass.Complex => " - Nomenclature and Classification Only",
                        _ => throw new NotImplementedException(),
                    };
                    var fileNameSuffix = targetGeneration switch
                    {
                        GenerationPass.All     => "",
                        GenerationPass.Core    => " Core Only",
                        GenerationPass.Complex => " Nomenclature and Classification Only",
                        _ => throw new NotImplementedException(),
                    };

                    var specLogger = provider.GetRequiredService<ILogger<SpecContext>>();
                    using var ctx = specLogger.BeginScope(targetStage.ToString());
                    using var ctx2 = specLogger.BeginScope(targetGeneration.ToString());
                    // Initialise a Spec context to help organise / number specs.
                    using var context = new SpecContext(targetStage, ids, targetGeneration, specLogger);
                    context.SetApplicableStages(RibaStages.All);
                    context.SetApplicableToGeneration(GenerationPass.Core);      // Determines whether to separate complex (e.g. naming) rules out from 'core' vs a single file ('All')
                    context.BasePath = Path.Combine(_outputRoot, _version.ToString(), "IDS");
                    context.SaveOneFilePerSpec = true;        // Output individual files
                    context.SaveOneFilePerScope = true;       // Use Context structure to group into smaller Spec Groups (produces IDS zip)
                    // Prepend stage number to all spec identifiers: 3_01_01, 4_02_03, etc.
                    context.StageId = stageNum.ToString();
                    // Individual IDS file metadata
                    context.IndividualAuthor = "DfE.BIM@Education.gov.uk";
                    context.IndividualCopyright = "CC BY 4.0";
                    var utcNow = DateTime.UtcNow;
                    context.IndividualVersion = revision == "Pnn" ? $"{revision}.{utcNow.Year}.{utcNow.DayOfYear}" : revision;
                    context.IndividualPurpose = "Information Model Assurance";
                    context.IndividualDescription = $"Assurance of IFC-SPF deliverables against DfE's {_version} Information Requirements - Individual{BuildClassificationVersionNote()}";
                    context.IndividualMilestone = StageNames[targetStage];

                    CleanPriorFiles(context, targetStage);

                    SpecificationsGroup rootGroup = InitialiseSpecGroup(context, config, revision, _version, stageNum, titleSuffix, descSuffix, BuildClassificationVersionNote());
                    context.InitialiseSpecGroup(rootGroup);

                    CreateProjectSpecifications(context, config);
                    CreateSiteSpecifications(context, config);
                    CreateBuildingSpecifications(context, config);
                    CreateBuildingStoreySpecifications(context, config);
                    CreateSpaceSpecifications(context);
                    CreateZoneSpecifications(context);

                    context.SetApplicableStages(RibaStages.Stage4Plus);
                    CreateObjectTypeSpecifications(context);
                    CreateObjectOccurrenceSpecifications(context);
                    CreateSystemSpecifications(context);

                    context.CloseScope();   // Closing will clear out any empty SpecificationGroup we didn't use - including any rootScope


                    Directory.CreateDirectory(context.BasePath);
                    var stageDesc = targetStage.ToDescription();   // e.g. "Stage 3"
                    var fileName = Path.Combine(context.BasePath, $"ER-DFE-XX-XX-L-X-{version:D4}-Information Model Assurance {stageDesc}{fileNameSuffix}-{status}-{revision}.ids");

                    var totalSpecs = ids.AllSpecifications().Count();
                    // Core-only single file excludes optional-applicability (SHOULD) specs; All and Complex keep everything
                    var singleFileSpecs = ids.AllSpecifications()
                        .Where(s => targetGeneration != GenerationPass.Core ||
                                    !(s.Cardinality is SimpleCardinality sc && sc.ApplicabilityCardinality == CardinalityEnum.Optional))
                        .OrderBy(s => s.Guid)
                        .ToList();
                    if (context.SaveOneFilePerScope)
                    {
                        if (ids.SpecificationsGroups.Count > 1)
                        {
                            // Save the Normal/Core/N&C single file with individual (ungrouped) rules first
                            foreach (var spec in singleFileSpecs)
                                rootGroup.Specifications.Add(spec);
                            var singleFileIds = new Xids
                            {
                                Guid = ids.Guid,
                                Name = ids.Name,
                                Project = ids.Project,
                                Stages = ids.Stages,
                                SpecificationsGroups = new List<SpecificationsGroup> { rootGroup }
                            };
                            singleFileIds.ExportBuildingSmartIDS(fileName, specLogger);
                            specLogger.LogInformation("Created single IDS file {fileName} with {specs} specifications", fileName, singleFileSpecs.Count);
                            rootGroup.Specifications.Clear();

                            // Now apply grouping and save the Grouped zip (scope-based groups with compound specs)
                            if (GroupCommonApplicableRequirements)
                                GroupRequirementsByApplicability(ids);

                            var zipFileName = Path.ChangeExtension(fileName, "zip");
                            ids.ExportBuildingSmartIDS(zipFileName, specLogger);
                            specLogger.LogInformation("Created group IDS file {fileName} with {specs} specifications in {groups} groups", zipFileName, totalSpecs, ids.SpecificationsGroups.Count);
                            // Unpack the grouped files
                            var stageFolderName = stageDesc.Replace(" ", "_");   // e.g. "Stage_3"
                            var unpackFolder = Path.Combine(context.BasePath, "Grouped", stageFolderName);
                            if (Directory.Exists(unpackFolder))
                                Directory.Delete(unpackFolder, true);
                            Directory.CreateDirectory(unpackFolder);
                            ZipFile.ExtractToDirectory(zipFileName, unpackFolder);
                            File.Delete(zipFileName);
                        }
                        else
                        {
                            specLogger.LogWarning("Only a single spec group found. Producing single ids file only");
                            foreach (var spec in singleFileSpecs)
                                rootGroup.Specifications.Add(spec);
                            ids.SpecificationsGroups.Clear();
                            ids.SpecificationsGroups.Add(rootGroup);
                            ids.ExportBuildingSmartIDS(fileName, specLogger);
                            specLogger.LogInformation("Created single IDS file {fileName} with {specs} specifications", fileName, singleFileSpecs.Count);
                        }
                    }
                    else
                    {
                        ids.ExportBuildingSmartIDS(fileName, specLogger);
                        specLogger.LogInformation("Created single IDS file {fileName} with {specs} specifications", fileName, totalSpecs);
                    }

                    if (ValidateIDSOutputs)
                        ValidateStage(context);
                }
            }
            return Task.CompletedTask;
        }


        private void GroupRequirementsByApplicability(Xids ids)
        {

            foreach(var specGroup in ids.SpecificationsGroups)
            {
                var groupedApplicability = specGroup.Specifications
                    .GroupBy(sp => sp.Applicability.Decode())
                    .OrderBy(sp => sp.First().Guid).ThenBy(sp => sp.Key)
                    .ToList();// TODO Consider all facets and equality

                // Capture the scope label from the original group name ("{prefix}-{Tag} Grouped")
                // before the inner loop overwrites specGroup.Name. Used for the filename so that
                // per-applicability labels (e.g. "Level 02") don't leak into the file label.
                var scopeTag = ExtractScopeTag(specGroup.Name);

                foreach (var groupedSpecs in groupedApplicability)
                {
                    if(groupedSpecs.Count() == 1)
                    {
                        continue;   // don't re-write single groups of specs
                    }
                    var firstSpec = groupedSpecs.First();
                    var lastSpec = groupedSpecs.Last();
                    var applicable = firstSpec.Applicability;
                    var spec = ids.PrepareSpecification(specGroup, firstSpec.IfcVersion!, applicable);
                    spec.Cardinality = firstSpec.Cardinality;

                    //spec.Applicability.RequirementOptions = new System.Collections.ObjectModel.ObservableCollection<RequirementCardinalityOptions>();
                    spec.Requirement!.RequirementOptions = new System.Collections.ObjectModel.ObservableCollection<RequirementCardinalityOptions>();
                    foreach (var groupedSpec in groupedSpecs)
                    {
                        if (groupedSpec.Requirement?.Facets.Any() != true)
                            continue;   //

                        // Add the requirements to the single spec
                        foreach(var req in groupedSpec.Requirement.Facets)
                        {
                            spec.Requirement!.Facets.Add(req);
                        }

                        // remove single version
                        specGroup.Specifications.Remove(groupedSpec);

                    }
                    foreach (var cardinality in groupedSpecs.SelectMany(a => a.Requirement!.RequirementOptions!))
                    {
                        // Copy cardinalities over
                        spec.Requirement!.RequirementOptions.Add(cardinality);
                    }
                    var groupName = firstSpec.Applicability.Name;
                    var shortLast = ShortenEndGuid(firstSpec.Guid, lastSpec.Guid);
                    // Omit the end-range suffix when it crosses nesting levels (contains '_'),
                    // which would produce an unreadable filename like "5_04_08_03-09_03_03".
                    var rangeSuffix = shortLast.Contains('_') ? "" : $"-{shortLast}";
                    spec.Name = $"{firstSpec.Guid}{rangeSuffix}-{groupName} ({groupedSpecs.Count()} requirements)";
                    spec.Guid = groupedSpecs.Aggregate(new StringBuilder(),
                        (curr, next) => curr.Append(curr.Length == 0 ? "" : ",").Append(next.Guid)).ToString();
                    spec.Description = groupedSpecs.Aggregate(new StringBuilder(),
                        (curr, next) => curr.Append(curr.Length == 0 ? $"{groupName} " : ", and ").Append(next.Description?.Replace($"{groupName} ", ""))).ToString();
                    spec.Instructions = groupedSpecs.Aggregate(new StringBuilder(),
                        (curr, next) => string.IsNullOrEmpty(next.Instructions) ? curr : curr.Append(curr.Length == 0 ? "" : ". ").Append(next.Guid).Append(": ").Append(next.Instructions)).ToString();

                    specGroup.Name = $"{firstSpec.Guid}{rangeSuffix}-{scopeTag}";
                }
            }
        }

        /// <summary>
        /// Returns the trailing portion of <paramref name="last"/> after the common underscore-delimited prefix shared with <paramref name="first"/>.
        /// E.g. "3_01_01" and "3_01_08" ? "08"; "3_03_01" and "3_03_12" ? "12".
        /// </summary>
        /// <summary>
        /// Extracts the scope tag from a spec group name in the form "{prefix}-{Tag} Grouped".
        /// Returns the raw name if it doesn't match that pattern.
        /// </summary>
        private static string ExtractScopeTag(string? groupName)
        {
            if (string.IsNullOrEmpty(groupName)) return "";
            var dash = groupName.IndexOf('-');
            var grouped = groupName.IndexOf(" Grouped", StringComparison.Ordinal);
            if (dash >= 0 && grouped > dash)
                return groupName[(dash + 1)..grouped];
            return groupName;
        }

        private static string ShortenEndGuid(string first, string last)
        {
            int common = 0;
            for (int i = 0; i < Math.Min(first.Length, last.Length); i++)
            {
                if (first[i] == last[i]) common = i + 1;
                else break;
            }
            var boundary = first[..common].LastIndexOf('_');
            return boundary >= 0 ? last[(boundary + 1)..] : last;
        }

        private void CleanPriorFiles(SpecContext context, RibaStages stage)
        {
            // Clean folders in case we renamed / deleted files
            var stageFolderName = stage.ToDescription().Replace(" ", "_");   // e.g. "Stage_3"
            var path = Path.Combine(context.BasePath, "Individual", stageFolderName);
            if (!Directory.Exists(path)) return;

            // Clear read-only flags first — OneDrive/antivirus can mark files read-only during sync
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* best-effort */ }
            }

            // Retry a few times — OneDrive/antivirus can briefly lock files during sync
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.Delete(path, true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt == 2) break;
                    System.Threading.Thread.Sleep(2000 * (attempt + 1));
                }
            }

            throw new IOException(
                $"Could not delete '{path}'. A file inside may be locked by OneDrive sync or another process. " +
                "Close any open files in that folder and try again.");
        }

        private void ValidateStage(SpecContext context)
        {
            var stageFolderName = context.TargetStage.ToDescription().Replace(" ", "_");
            var path = Path.Combine(context.BasePath, "Individual", stageFolderName);
            ValidateFolder(path);
        }

        private void ValidateFolder(string folder)
        {
            var logger = provider.GetRequiredService<ILogger<IIdsValidator>>();
            var validator = provider.GetRequiredService<IIdsValidator>();
            var files = Directory.GetFiles(folder, "*.ids", new EnumerationOptions { RecurseSubdirectories = true });

            var errs = 0;
            foreach(var file in files)
            {
                using var ctx = logger.BeginScope(Path.GetFileName(file));
                var status = validator.ValidateIdsFolder(file, logger);
                if(status != IdsLib.Audit.Status.Ok)
                {
                    errs++;
                }
            }
            if(errs  > 0)
            {
                logger.LogWarning("Founds {errCount} files with warnings", errs);
            }
            else
            {
                logger.LogInformation("IDS file validated successfully");
            }

        }

        private static SpecificationsGroup InitialiseSpecGroup(SpecContext context, DfeConfig config, string revision, ImrVersion version, int stageNum, string titleSuffix, string descSuffix, string classVersionNote = "")
        {
            var now = DateTime.UtcNow;
            var targetStage = context.TargetStage;

            var specGroup = new SpecificationsGroup(context.Ids)
            {
                Date = now,
                Guid = Guid.NewGuid().ToString(),
                Name = $"Information Model Assurance Stage {stageNum}{titleSuffix}",
                Specifications = new List<Specification>(),
                Milestone = (version == ImrVersion.S25 ? ribaStagesS25 : ribaStagesS21)[targetStage],
                Author = "DfE.BIM@Education.gov.uk",
                Description = $"Assurance of IFC-SPF deliverables against DfE's {version} Information Requirements{descSuffix}{classVersionNote}",
                Version = revision == "Pnn" ? $"{revision}.{now.Year}.{now.DayOfYear}" : revision,
                Purpose = "Information Model Assurance",
                Copyright = "CC BY 4.0",
            };
            return specGroup;
        }

        // 01
        private void CreateProjectSpecifications(SpecContext context, DfeConfig config)
        {
            using var subContext = context.BeginSubscope().AddTag("Project");
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Project", "IfcProject");
            var projModalVerb = _version == ImrVersion.S25 ? "Shall" : "Should";
            CreateCommonRequirements(ids, applicability, config.ProjectName, config.ProjectDescription, subContext,
                _version == ImrVersion.S25 ? "Project Shall Have GlobalId Defined" : null,
                projModalVerb);

            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcProject.Phase), subContext,
                title: _version == ImrVersion.S25 ? "Project Shall Have Phase Defined" : null);
            AsNomenclature(subContext, () =>
                CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcProject.Phase), StageNames.Values, subContext,
                    title: _version == ImrVersion.S25 ? "Project Shall Have Phase Matching The Projects Information Standard" : null));
            AsNomenclature(subContext, () =>
                CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcProject.Phase), new[] { config.ProjectPhase }, subContext,
                    title: _version == ImrVersion.S25 ? "Project Shall Have Phase Matching The Current Project Stage" : "Project Should Have Phase Correct For Project Stage"));
        }


        // 02
        private void CreateSiteSpecifications(SpecContext context, DfeConfig config)
        {
            using var subContext = context.BeginSubscope().AddTag("Site");
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Site", "IfcSite");
            CreateCommonRequirements(ids, applicability, config.SiteName, config.SiteDescription, subContext,
                _version == ImrVersion.S25 ? "Site Shall Have GlobalId Defined" : null,
                _version == ImrVersion.S25 ? "Shall" : "Should");
        }

        // 03
        private void CreateBuildingSpecifications(SpecContext context, DfeConfig config)
        {
            using var subContext = context.BeginSubscope().AddTag("Building"); ;
            var group = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Building", "IfcBuilding");
            CreateCommonRequirements(ids, applicability, config.BuildingName, config.BuildingDescription, subContext,
                _version == ImrVersion.S25 ? "Building Shall Have GlobalId Defined" : null,
                _version == ImrVersion.S25 ? "Shall" : "Should");
            CreateClassificationPatternSpecification(group, applicability, ids, UniclassSystemLabel, "En.*", subContext,
                title: _version == ImrVersion.S25 ? "Building Shall Have Uniclass Classification Defined" : null);
            AsNomenclature(subContext, () =>
                CreateClassificationCodeValueSpecification(group, applicability, ids, "Uniclass En", ValueConstraint.CreatePattern(UniclassSystemLabel), config.BuildingCategory, subContext,
                    title: _version == ImrVersion.S25 ? "Building Shall Have Uniclass Classification Matching The Projects Information Standard" : null));
            CreatePropertyNonEmptySpecification(group, applicability, ids, "BlockConstructionType", "Additional_Pset_BuildingCommon", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Building Shall Have BlockConstructionType Defined" : null);
            if (_version == ImrVersion.S25)
            {
                AsNomenclature(subContext, () =>
                    CreatePropertyWithValueSpecification(group, applicability, ids, "BlockConstructionType", "Additional_Pset_BuildingCommon", config.BuildingBlockConstructionType, subContext, dataType: "IFCTEXT",
                        title: "Building Shall Have BlockConstructionType Matching The Projects Information Standard"));
            }
            CreatePropertyWithValueInRangeSpecification(group, applicability, ids, "MaximumBlockHeight", "Additional_Pset_BuildingCommon", subContext, 0, false, null, false, "IFCLENGTHMEASURE",
                title: _version == ImrVersion.S25 ? "Building Shall Have MaximumBlockHeight Defined" : null);
            CreatePropertyDefinedSpecification(group, applicability, ids, "NumberOfStoreys", "Pset_BuildingCommon", subContext, "IFCINTEGER",
                title: _version == ImrVersion.S25 ? "Building Shall Have NumberOfStoreys Defined" : null);
            CreatePropertyNonEmptySpecification(group, applicability, ids, "UPRN", "COBie_BuildingCommon_UK", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Building Shall Have UPRN Defined" : null);
            AsNomenclature(subContext, () =>
                CreatePropertyWithValueSpecification(group, applicability, ids, "UPRN", "COBie_BuildingCommon_UK", config.BuildingUPRN, subContext, "IFCTEXT",
                    title: _version == ImrVersion.S25 ? "Building Shall Have UPRN Matching The Projects Information Standard" : null));
        }

        // 04
        private void CreateBuildingStoreySpecifications(SpecContext context, DfeConfig config)
        {
            using var subContext = context.BeginSubscope().AddTag("Levels");
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Building Storey", "IfcBuildingStorey");
            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.GlobalId), subContext,
                _version == ImrVersion.S25 ? "Building Storey Shall Have GlobalId Defined" : null);

            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Name), subContext,
                title: _version == ImrVersion.S25 ? "Building Storey Shall Have Name Defined" : null);
            var floors = floorDict.Values.Where(n => n.Name != null);
            AsNomenclature(subContext, () =>
                CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Name), floors.Select(f => f.Name)!, subContext,
                    title: _version == ImrVersion.S25 ? "Building Storey Shall Have Name Matching The Projects Information Standard" : null));
            subContext.Skip("Unique Storey Name");
            // TODO: Building Storey Should Have Unique Name
            // TODO: Should corelate Floor Descr/Category to Floor Name
            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Description), subContext,
                title: _version == ImrVersion.S25 ? "Building Storey Shall Have Description Defined" : null);
            AsNomenclature(subContext, () =>
                CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Description), floors.Select(f => f.Description), subContext,
                    title: _version == ImrVersion.S25 ? "Building Storey Shall Have Description Matching The Projects Information Standard" : null));

            // Building Storey Should Have Category(Floor Classification) Matching The Projects Information Standard
            var floorClassLabel = _version == ImrVersion.S21 ? "COBie Floor Classification" : "Floor Classification";
            var floorClassSystem = _version == ImrVersion.S21 ? ValueConstraint.CreatePattern(".*Floor.*") : new ValueConstraint("Floor Classification");
            var floorClassTitle = _version == ImrVersion.S25 ? "Building Storey Shall Have Floor Classification Matching The Projects Information Standard" : null;
            AsNomenclature(subContext, () =>
                CreateClassificationFromListSpecification(specs, applicability, ids, floorClassLabel, floorClassSystem, new string[] { "Site", "Floor", "Roof" }, subContext, floorClassTitle));

            // Building Storey Should Have Elevation Matching The Projects Information Standard
            var storeyCount = _buildingStoreys ?? config.NumberOfStoreys;
            var standardFloors = new[] { "00", "01", "02", "03", "04" }
                .Take(storeyCount)
                .Select(code => floorDict[code]);
            using (var elevationContext = subContext.BeginSubscope())
            {
                foreach (var floor in standardFloors)
                {
                    var storeyApplicability = GetEntityApplicabilityWithAttribute(
                        ids, floor.Name!, "IfcBuildingStorey", nameof(IIfcBuildingStorey.Name), floor.Name!);
                    var elevationToken = $@"{{{{IfcBuildingStorey.Level {floor.Code}.Elevation}}}}";
                    var elevationTitle = _version == ImrVersion.S25
                        ? "Building Storey Shall Have Elevation Matching The Projects Information Standard"
                        : $"{floor.Name} Should Have Elevation Matching The Projects Information Standard";
                    AsNomenclature(elevationContext, () =>
                        CreateAttributeValueSpecification(specs, storeyApplicability, ids,
                            nameof(IIfcBuildingStorey.Elevation), elevationToken, elevationContext,
                            title: elevationTitle));
                }
            }

            // 04.09 - Three routes to compliance per storey (mirrors 04.08 elevation pattern):
            // Route 1: NominalHeight in BaseQuantities (IFC 2x3 TC1 standard)
            // Route 2: Height in BaseQuantities (Revit/ArchiCAD actual output)
            // Route 3: NetHeight in Additional_Pset_BuildingStoreyCommon (user-recorded fallback)
            using var heightContext = subContext.BeginSubscope();
            foreach (var (floor, i) in standardFloors.Select((f, i) => (f, i + 1)))
            {
                var storeyApplicability = GetEntityApplicabilityWithAttribute(
                    ids, floor.Name!, "IfcBuildingStorey", nameof(IIfcBuildingStorey.Name), floor.Name!);
                var heightToken = HeightToken(floor.Code);
                var idx = $"{i:D2}";

                AsNomenclature(heightContext, () =>
                    CreatePropertyWithValueSpecification(specs, storeyApplicability, ids, "NominalHeight", "BaseQuantities", heightToken, heightContext, "IfcLengthMeasure",
                        title: _version == ImrVersion.S25 ? $"{floor.Name} Shall Have NominalHeight In BaseQuantities Matching Height Set Out In The Projects Information Standard" : null,
                        ruleId: RuleId($"4_04_09_01_{idx}", heightContext)));
                heightContext.SetMatches(CardinalityEnum.Optional);
                AsNomenclature(heightContext, () =>
                    CreatePropertyWithValueSpecification(specs, storeyApplicability, ids, "Height", "BaseQuantities", heightToken, heightContext, "IfcLengthMeasure",
                        title: _version == ImrVersion.S25 ? $"{floor.Name} Should Have Height In BaseQuantities Matching Height Set Out In The Projects Information Standard" : null,
                        ruleId: RuleId($"4_04_09_02_{idx}", heightContext)));
                AsNomenclature(heightContext, () =>
                    CreatePropertyWithValueSpecification(specs, storeyApplicability, ids, "NetHeight", "Additional_Pset_BuildingStoreyCommon", heightToken, heightContext, "IfcLengthMeasure",
                        title: _version == ImrVersion.S25 ? $"{floor.Name} Should Have NetHeight Matching Height Set Out In The Projects Information Standard" : null,
                        ruleId: RuleId($"4_04_09_03_{idx}", heightContext)));
                heightContext.ResetMatches();
            }
            
        }

        // 05
        private void CreateSpaceSpecifications(SpecContext context)
        {
            using var subContext = context.BeginSubscope().AddTag("Space"); ;
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Space", "IfcSpace");

            // Space Shall Have GlobalId Defined
            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcSpace.GlobalId), subContext,
                _version == ImrVersion.S25 ? "Space Shall Have GlobalId Defined" : null);
            // Space Shall Have Name Defined
            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcSpace.Name), subContext,
                title: _version == ImrVersion.S25 ? "Space Shall Have Name Defined" : null);
            // Space Shall Have Name Matching Format Set Out In The Projects Information Standard
            AsNomenclature(subContext, () =>
                CreateAttributePatternSpecification(specs, applicability, ids, nameof(IIfcSpace.Name), spaceNameExpression.ToString(), subContext,
                    title: _version == ImrVersion.S25 ? "Space Shall Have Name Matching Format Set Out In The Projects Information Standard" : null));
            // TODO: Space Should Have Name That Is Unique
            subContext.Skip("05.04: Unique name not supported");
            // 05.05: Space Shall Have Name Related Correctly To Each Floor
            subContext.Skip("05.05: IDS 1.0 cannot traverse spatial containment relationships - if applied, every space would be checked against every floor's naming rule, producing incorrect failures");
            //CreateSpaceNameSpecifications(specs, applicability, subContext);
            // Space Shall Have Description Defined
            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcSpace.Description), subContext,
                title: _version == ImrVersion.S25 ? "Space Shall Have Description Defined" : null);

            // Space Shall Have RoomTag Defined (Stage 3-4) / Shall Have RoomTag That Is Not 'n/a' (Stage 5+)
            var original = subContext.ApplicableToStages;
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "RoomTag", "COBie_Space", subContext.SetApplicableStages(RibaStages.Stage3 | RibaStages.Stage4), "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Space Shall Have RoomTag Defined" : null);
            using (var roomTagStage5 = subContext.BeginSubscope().SetApplicableStages(RibaStages.Stage5Plus))
            {
                CreatePropertyNonEmptySpecification(specs, applicability, ids, "RoomTag", "COBie_Space", roomTagStage5, "IFCTEXT",
                    title: _version == ImrVersion.S25 ? "Space Shall Not Have RoomTag That Is 'n/a' or Empty" : null);
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "RoomTag", "COBie_Space",
                    notNaExpression.ToString(), "not n/a", roomTagStage5, "IFCTEXT",
                    title: _version == ImrVersion.S25 ? "Space Shall Not Have RoomTag That Is 'n/a' or Empty" : null);
            }
            subContext.SetApplicableStages(original);   // reset default

            // Space Shall Have DfE Space Classification Defined � label and code list differ between S21 (ADS) and S25 (Space)
            var spaceClassConstraint = _version == ImrVersion.S21
                ? ValueConstraint.CreatePattern(adsNameExpression.ToString())
                : new ValueConstraint("DfE Space Classification");
            var spaceClassLabel = _version == ImrVersion.S21 ? "ADS Classification" : "DfE Space Classification";
            CreateClassificationDefinedSpecification(specs, applicability, ids, spaceClassLabel, spaceClassConstraint, subContext,
                title: _version == ImrVersion.S25 ? "Space Shall Have DfE Space Classification Defined" : null);
            AsNomenclature(subContext, () =>
                CreateClassificationFromListSpecification(specs, applicability, ids, spaceClassLabel, spaceClassConstraint, GetSpaceCodes(), subContext,
                    title: _version == ImrVersion.S25 ? "Space Shall Have DfE Space Classification Matching The Employers Requirements" : null));

            // S21: Height/GrossArea/NetArea  |  S25: ClearHeight (05.11.01) + Height fallback (05.11.02), GrossFloorArea/NetFloorArea  (05.11-05.13)
            var grossAreaProp    = _version == ImrVersion.S21 ? "GrossArea"     : "GrossFloorArea";
            var netAreaProp      = _version == ImrVersion.S21 ? "NetArea"       : "NetFloorArea";
            if (_version == ImrVersion.S21)
            {
                CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "Height", "BaseQuantities", subContext, 0, false, null, false, "IFCLENGTHMEASURE");
            }
            else
            {
                // 05.11.01: ClearHeight � schema-correct BaseQuantity name per IFC specification
                using (var clearHeightScope = subContext.BeginSubscope())
                {
                CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "ClearHeight", "BaseQuantities", clearHeightScope, 0, false, null, false, "IFCLENGTHMEASURE",
                    title: "Space Shall Have ClearHeight Defined In BaseQuantities",
                    ruleId: RuleId("4_05_11_01", clearHeightScope));
                // 05.11.02: Height � fallback for software that incorrectly exports ClearHeight as Height in BaseQuantities
                clearHeightScope.SetMatches(CardinalityEnum.Optional);
                CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "Height", "BaseQuantities", clearHeightScope, 0, false, null, false, "IFCLENGTHMEASURE",
                    title: "Space Should Have Height Defined In BaseQuantities",
                    ruleId: RuleId("4_05_11_02", clearHeightScope));
                // 05.11.03: FinishCeilingHeight � optional user-recorded accurate value
                CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "FinishCeilingHeight", "Additional_Pset_SpaceCommon", clearHeightScope, 0, false, null, false, "IFCLENGTHMEASURE",
                    title: "Space Should Have FinishCeilingHeight Defined In Additional_Pset_SpaceCommon",
                    ruleId: RuleId("4_05_11_03", clearHeightScope));
                }
            }
            CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, grossAreaProp,   "BaseQuantities", subContext, 0, false, null, false, "IFCAREAMEASURE",
                title: _version == ImrVersion.S25 ? "Space Shall Have GrossFloorArea Defined" : null);
            CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, netAreaProp,     "BaseQuantities", subContext, 0, false, null, false, "IFCAREAMEASURE",
                title: _version == ImrVersion.S25 ? "Space Shall Have NetFloorArea Defined" : null);


            // Space Shall Have Uniclass Classification Defined
            var uniclassSystemLabel = _version == ImrVersion.S21 ? "Uniclass 2015" : UniclassSystemLabel;
            var uniclassSystemConstraint = _version == ImrVersion.S25
                ? new ValueConstraint(UniclassSystemLabel)
                : ValueConstraint.CreatePattern(UniclassSystemLabel);
            CreateClassificationFromListSpecification(specs, applicability, ids, uniclassSystemLabel, uniclassSystemConstraint, GetUniclassSLCodes(), subContext,
                title: _version == ImrVersion.S25 ? "Space Shall Have Uniclass Classification Defined" : null);

            // Space Should Have UniclassClassification That Corresponds Correctly To The Category(DfE ADS Classification)
            // Creates 100+ specs due to permutations
            CreateADSToUniclassSpecifications(subContext);

            CreatePartOfSpecification(specs, applicability, ids, PartOfFacet.PartOfRelation.IfcRelAssignsToGroup, "IfcZone", subContext,
                title: _version == ImrVersion.S25 ? "Space Shall Be Allocated To A Zone" : null);

        }

        // 05.15
        private void CreateADSToUniclassSpecifications(SpecContext subContext)
        {
            using (var adsScope = subContext.BeginSubscope()
                .SetApplicableToGeneration(GenerationPass.Complex)
                .SetMatches(CardinalityEnum.Optional))
            {
                var specs = adsScope.CurrentSpecGroup;
                var ids = subContext.Ids;
                var spaceMap = GetUniclassSpaceMap();
                var classFilter = _version == ImrVersion.S21 ? ".*ADS.*" : "DfE Space Classification";
                var uniclassConstraint = _version == ImrVersion.S25
                    ? new ValueConstraint(UniclassSystemLabel)
                    : ValueConstraint.CreatePattern(UniclassSystemLabel);
                const int trimAt = 3;
                foreach (var item in spaceMap)
                {
                    var label = String.Join(", ", item.Value.Take(trimAt));
                    if(item.Value.Count() > trimAt)
                    {
                        label += $",+{item.Value.Count()- trimAt} more";
                    }
                    var name = $"Spaces with classification '{label}'";
                    adsScope.SetName(item.Key);

                    var applicab = GetEntityApplicabilityWithClassifications(ids, name, "IfcSpace", classFilter, item.Value, false);

                    CreateClassificationCodeValueSpecification(specs, applicab, ids, UniclassSystemLabel, uniclassConstraint, item.Key, adsScope);
                }

            }
        }

        private void CreateSpaceNameSpecifications(FacetGroup applicability, SpecContext subContext)
        {
            // Blocked: See https://github.com/buildingSMART/IDS/discussions/341
            // Space Should Have Name Related Correctly To Each Floor
            // For each spaces in a Level, check the pattern matches

            //foreach (var level in floorDict.Values)
            //{

            //}
        }

        // 06
        private void CreateZoneSpecifications(SpecContext context)
        {
            using var subContext = context.BeginSubscope()
                .AddTag("Zone");
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Zone", "IfcZone");
            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcZone.GlobalId), subContext,
                _version == ImrVersion.S25 ? "Zone Shall Have GlobalId Defined" : null);

            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcZone.Name), subContext,
                title: _version == ImrVersion.S25 ? "Zone Shall Have Name Defined" : null);
            AsNomenclature(subContext, () =>
                CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcZone.Name), GetZoneCodes(), subContext,
                    title: _version == ImrVersion.S25 ? "Zone Shall Have Name Matching The Projects Information Standard" : null));

            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcZone.Description), subContext,
                title: _version == ImrVersion.S25 ? "Zone Shall Have Description Defined" : null);
            AsNomenclature(subContext, () =>
                CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcZone.Description), GetZoneDescriptions(), subContext,
                    title: _version == ImrVersion.S25 ? "Zone Shall Have Description Matching The Projects Information Standard" : null));

            var zoneClassLabel = _version == ImrVersion.S21 ? "COBie Zone Classification" : "Zone Classification";
            var zoneClassification = _version == ImrVersion.S21 ? ValueConstraint.CreatePattern(".*Zone.*") : new ValueConstraint("Zone Classification");
            CreateClassificationDefinedSpecification(specs, applicability, ids, zoneClassLabel, zoneClassification, subContext,
                title: _version == ImrVersion.S25 ? "Zone Shall Have Zone Classification Defined" : null);
            AsNomenclature(subContext, () =>
                CreateClassificationFromListSpecification(specs, applicability, ids, zoneClassLabel, zoneClassification, GetZoneCategories(), subContext,
                    title: _version == ImrVersion.S25 ? "Zone Shall Have Zone Classification Matching The Projects Information Standard" : null));
            subContext.Skip("06:08: Zone Shall Have Spaces Allocated To It - not expressible in IDS 1.0 (partOf only works from the Space side; 05.16 covers the inverse)");

        }

        // 07
        private void CreateObjectTypeSpecifications(SpecContext context)
        {
            using var subContext = context.BeginSubscope()
                .AddTag("Object Type");
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            //var applicability = GetEntityApplicability(ids, "Object Type", "IfcTypeObject");
            var applicability = GetEntityApplicability(ids, "Object Type", RootTypes);



            // 07.01 - Object Type Shall Be Entity In Schema
            // Temporarily disabled: triggers AttributeError in Bonsai ifctester (upstream bug � xs:restriction entity name in requirements not handled)
            // var entityApplicability = GetEntityApplicability(ids, "Object Type", "IfcTypeObject", includeSubTypes: true);
            // var validEntityTypes = GetSubTypes(RootTypes).ToArray();
            // CreateEntityInSchemaSpecification(specs, entityApplicability, ids, validEntityTypes, subContext,
            //     title: _version == ImrVersion.S25 ? "Object Type Shall Be Entity In Schema" : null);
            subContext.Skip("07.01 temporarily disabled: IfcOpenShell/Bonsai upstream bug (xs:restriction entity name in requirements not handled)");
            // Object Type Should Have Enumeration(PredefinedType) Defined
            var pdtTypes = Schema.GetAttributeClasses("PredefinedType")
                    .Where(c => c.EndsWith("TYPE"))
                .Where(c=> !c.StartsWith("IFCSPACE")).ToArray();
            var pdtApplicablity = GetEntityApplicability(ids, "Object Type", pdtTypes);
            CreateAttributeDefinedSpecification(specs, pdtApplicablity, ids, nameof(IIfcWallType.PredefinedType), subContext,
                _version == ImrVersion.S25 ? "Object Type Shall Have Enumeration (PredefinedType) Defined" : null);
            // Object Type Shall Have Enumeration(PredefinedType) That Is Not NOTDEFINED
            // TODO: see above re DoorStyle etc
            CreateAttributeValueSpecification(specs, pdtApplicablity, ids, "PredefinedType", "NOTDEFINED", subContext.SetRule(Cardinality.Prohibited),
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Enumeration (PredefinedType) That Is Not NOTDEFINED" : null);
            subContext
                .ResetRule()
                .ResetMatches();
            // Object Type Shall Have Name Defined
            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcTypeObject.Name), subContext,
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Name Defined" : null);

            // Object Type Shall Have Name Matching The Projects Information Standard
            // This creates hundreds of Specs as per 5.2.7/5.2.8 of the PIS as we need to test per IFC Type
            CreateObjectTypeNamingSpecifications(subContext);

            // TODO: Object Type Shall Have Name That Is Unique
            subContext.Skip("07.06 Unique support not in IDS");
            // Object Type Shall Have Description Defined
            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcTypeObject.Description), subContext,
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Description Defined" : null);
            // Object Type Shall Have Uniclass Classification Defined
            CreateClassificationPatternSpecification(specs, applicability, ids, UniclassSystemLabel, "Pr_.*", subContext,
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Uniclass Classification Defined" : null);

            //  !! Applicable to COBie Types only here on!!
            applicability = GetEntityApplicability(ids, "Object Type", DomainExtensions.CobieTypes);
            subContext.SetMatches(CardinalityEnum.Optional);

            // 07.09: Object Type Shall Have AssetType Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "AssetType", "COBie_Asset", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have AssetType Defined" : null,
                ruleId: RuleId("4_07_09", subContext));
            // 07.10: Object Type Shall Have AssetType Matching The Projects Information Standard
            AsNomenclature(subContext, () =>
                CreatePropertyFromListSpecification(specs, applicability, ids, "AssetType", "COBie_Asset", new string[] { "Fixed", "Movable" }, subContext, "IFCTEXT",
                    title: _version == ImrVersion.S25 ? "Object Type Shall Have AssetType Matching The Projects Information Standard" : null,
                    ruleId: RuleId("4_07_10", subContext)));

            // 07.11: Object Type Shall Have Manufacturer That Is Defined (Stage4+)
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "Manufacturer", "Pset_ManufacturerTypeInformation", subContext, dataType: "IFCLABEL",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Manufacturer That Is Defined" : null,
                ruleId: RuleId("4_07_11", subContext));
            // 07.12: Object Type Should Not Have Manufacturer That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            if (_version == ImrVersion.S21)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "Manufacturer", "Pset_ManufacturerTypeInformation", emailOrNaExpression.ToString(), "n/a or Email Address", subContext, "IFCLABEL");
            }
            else
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "Manufacturer", "Pset_ManufacturerTypeInformation", notNaExpression.ToString(), "not n/a", subContext, "IFCLABEL",
                    title: "Object Type Should Not Have Manufacturer That Is 'n/a'",
                    ruleId: RuleId("5_07_12", subContext));
            }
            // 07.13: Object Type Should Have Manufacturer That Is An Email Address
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "Manufacturer", "Pset_ManufacturerTypeInformation", emailExpression.ToString(), "Email Address", subContext, "IFCLABEL",
                title: _version == ImrVersion.S25 ? "Object Type Should Have Manufacturer That Is An Email Address" : null,
                ruleId: RuleId("5_07_13", subContext));

            // 07.14/07.15: S25 only: Object Type Should Have ModelLabel That Is Defined / Should Not Be 'n/a'
            if (_version == ImrVersion.S25)
            {
                subContext.SetApplicableStages(RibaStages.Stage4Plus);
                CreatePropertyNonEmptySpecification(specs, applicability, ids, "ModelLabel", "Pset_ManufacturerTypeInformation", subContext, dataType: "IFCLABEL",
                    title: "Object Type Shall Have ModelLabel That Is Defined",
                    ruleId: RuleId("4_07_14", subContext));
                subContext.SetApplicableStages(RibaStages.Stage5Plus);
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "ModelLabel", "Pset_ManufacturerTypeInformation",
                    notNaExpression.ToString(), "not n/a", subContext, "IFCLABEL",
                    title: "Object Type Shall Not Have ModelLabel That Is 'n/a'",
                    ruleId: RuleId("5_07_15", subContext));
            }

            // 07.16: Object Type Should Have WarrantyGuarantorParts That Is Defined (Stage4+)
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "WarrantyGuarantorParts", "COBie_Warranty", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have WarrantyGuarantorParts That Is Defined" : null,
                ruleId: RuleId("4_07_16", subContext));
            // Object Type Should Have WarrantyGuarantorParts That Is 'n/a' Or An Email Address (S21 only)
            if (_version == ImrVersion.S21)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyGuarantorParts", "COBie_Warranty", emailOrNaExpression.ToString(), "n/a or Email Address", subContext, "IFCTEXT");
            }
            // 07.17: Should Not Have WarrantyGuarantorParts That Is 'n/a' Or Empty (Stage5+)
            if (_version == ImrVersion.S25)
            {
                subContext.SetApplicableStages(RibaStages.Stage5Plus);
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyGuarantorParts", "COBie_Warranty",
                    notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                    title: "Object Type Should Not Have WarrantyGuarantorParts That Is 'n/a' Or Empty",
                    ruleId: RuleId("4_07_17", subContext));
                subContext.SetApplicableStages(RibaStages.Stage4Plus);
            }
            // 07.18: Object Type Should Have WarrantyGuarantorParts That Is An Email Address
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyGuarantorParts", "COBie_Warranty", emailExpression.ToString(), "Email Address", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Have WarrantyGuarantorParts That Is An Email Address" : null,
                ruleId: RuleId("5_07_18", subContext));

            // 07.19: Object Type Should Have WarrantyDurationParts That Is Defined (Stage4+)
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "WarrantyDurationParts", "COBie_Warranty", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have WarrantyDurationParts That Is Defined" : null,
                ruleId: RuleId("4_07_19", subContext));
            // Object Type Should Have WarrantyDurationParts That Is '0.0' Or Is A Valid Duration (Stage4+, extra check not in CSV)
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyDurationParts", "COBie_Warranty", numericExpression.ToString(), "Valid duration", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Have WarrantyDurationParts That Is '0.0' Or Is A Valid Duration" : null,
                ruleId: RuleId("4_07_19_02", subContext));
            // 07.20: WarrantyDurationParts must be > 0 at Stage5+ (S21: range check; S25: pattern-based)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            if (_version == ImrVersion.S21)
            {
                CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "WarrantyDurationParts", "COBie_Warranty", subContext, 0, minInclusive: false, null, default, "IFCTEXT");
            }
            else
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyDurationParts", "COBie_Warranty", nonZeroDurationExpression.ToString(), "Non-zero duration", subContext, "IFCTEXT",
                    title: "Object Type Should Have WarrantyDurationParts That Is A Valid Non-Zero Duration",
                    ruleId: RuleId("5_07_20", subContext));
            }

            // 07.21: Object Type Should Have WarrantyGuarantorLabor That Is Defined (Stage4+)
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "WarrantyGuarantorLabor", "COBie_Warranty", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have WarrantyGuarantorLabor That Is Defined" : null,
                ruleId: RuleId("4_07_21", subContext));
            // Object Type Should Have WarrantyGuarantorLabor That Is 'n/a' Or An Email Address (S21) / Should Not Have It That Is 'n/a' (S25 07.22, Stage5+)
            if (_version == ImrVersion.S21)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyGuarantorLabor", "COBie_Warranty", emailOrNaExpression.ToString(), "n/a or Email Address", subContext, "IFCTEXT");
            }
            else
            {
                subContext.SetApplicableStages(RibaStages.Stage5Plus);
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyGuarantorLabor", "COBie_Warranty",
                    notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                    title: "Object Type Should Not Have WarrantyGuarantorLabor That Is 'n/a' Or Empty",
                    ruleId: RuleId("4_07_22", subContext));
                subContext.SetApplicableStages(RibaStages.Stage4Plus);
            }
            // 07.23: Object Type Should Have WarrantyGuarantorLabor That Is An Email Address
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyGuarantorLabor", "COBie_Warranty", emailExpression.ToString(), "Email Address", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Have WarrantyGuarantorLabor That Is An Email Address" : null,
                ruleId: RuleId("5_07_23", subContext));


            // 07.24: Object Type Should Have WarrantyDurationLabor That Is Defined (Stage4+)
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "WarrantyDurationLabor", "COBie_Warranty", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have WarrantyDurationLabor That Is Defined" : null,
                ruleId: RuleId("4_07_24", subContext));
            // Object Type Should Have WarrantyDurationLabor That Is '0.0' Or Is A Valid Duration (Stage4+, extra check not in CSV)
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyDurationLabor", "COBie_Warranty", numericExpression.ToString(), "Valid duration", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Have WarrantyDurationLabor That Is '0.0' Or Is A Valid Duration" : null,
                ruleId: RuleId("4_07_24_02", subContext));
            // 07.25: WarrantyDurationLabor must be > 0 at Stage5+ (S21: range check; S25: pattern-based)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            if (_version == ImrVersion.S21)
            {
                CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "WarrantyDurationLabor", "COBie_Warranty", subContext, 0, minInclusive: false, null, default, "IFCTEXT");
            }
            else
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyDurationLabor", "COBie_Warranty", nonZeroDurationExpression.ToString(), "Non-zero duration", subContext, "IFCTEXT",
                    title: "Object Type Should Have WarrantyDurationLabor That Is A Valid Non-Zero Duration",
                    ruleId: RuleId("5_07_25", subContext));
            }

            subContext.SetApplicableStages(RibaStages.Stage4Plus); // For remainder of specs

            // 07.26: Object Type Should Have ReplacementCost That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "ReplacementCost", "COBie_EconomicImpactValues", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have ReplacementCost That Is Defined" : null,
                ruleId: RuleId("4_07_26", subContext));
            // Object Type Should Have ReplacementCost That Is 'n/a' Or Is A Replacement Cost For The Product Type (Stage4+, extra check not in CSV)
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "ReplacementCost", "COBie_EconomicImpactValues", monetaryOrNaExpression.ToString(), "Replacement Cost", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Have ReplacementCost That Is 'n/a' Or Is A Replacement Cost For The Product Type" : null,
                ruleId: RuleId("4_07_26_02", subContext));
            // 07.27: Object Type Should Not Have ReplacementCost That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "ReplacementCost", "COBie_EconomicImpactValues",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have ReplacementCost That Is 'n/a'" : null,
                ruleId: RuleId("5_07_27", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.28: Object Type Should Have ExpectedLife That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "ServiceLifeDuration", "COBie_ServiceLife", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have ExpectedLife That Is Defined" : null,
                ruleId: RuleId("4_07_28", subContext));
            // Object Type Should Have ExpectedLife That Is 'n/a' Or Is A Valid Expected Life For The Product Type (Stage4+, extra check not in CSV)
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "ServiceLifeDuration", "COBie_ServiceLife", numericOrNaExpression.ToString(), "Expected Life", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Have ExpectedLife That Is 'n/a' Or Is A Valid Expected Life For The Product Type" : null,
                ruleId: RuleId("4_07_28_02", subContext));
            // 07.29: Object Type Should Not Have ExpectedLife That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "ServiceLifeDuration", "COBie_ServiceLife",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have ExpectedLife That Is 'n/a'" : null,
                ruleId: RuleId("5_07_29", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.30: Object Type Should Have WarrantyDescription That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "WarrantyDescription", "COBie_Warranty", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have WarrantyDescription That Is Defined" : null,
                ruleId: RuleId("4_07_30", subContext));
            // Object Type Should Have WarrantyDescription That Is 'n/a' Or A Description (Stage4+, extra check not in CSV)
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyDescription", "COBie_Warranty", textOrNaExpression.ToString(), "Warranty", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Have WarrantyDescription That Is 'n/a' Or A Description Of The Warranty For The Product Type" : null,
                ruleId: RuleId("4_07_30_02", subContext));
            // 07.31: Object Type Should Not Have WarrantyDescription That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyDescription", "COBie_Warranty",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have WarrantyDescription That Is 'n/a'" : null,
                ruleId: RuleId("5_07_31", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
            // 07.32: Object Type Should Have NominalLength That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "NominalLength", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have NominalLength That Is Defined" : null,
                ruleId: RuleId("4_07_32", subContext));
            // 07.33: Should Have NominalLength That Is A Valid Number (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            if (_version == ImrVersion.S25)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "NominalLength", "COBie_Specification",
                    numericExpression.ToString(), "valid number", subContext, "IFCTEXT",
                    title: "Object Type Should Have NominalLength That Is A Valid Number",
                    ruleId: RuleId("4_07_33", subContext));
            }
            else
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "NominalLength", "COBie_Specification",
                    notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT");
            }
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.34: Object Type Should Have NominalWidth That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "NominalWidth", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have NominalWidth That Is Defined" : null,
                ruleId: RuleId("4_07_34", subContext));
            // 07.35: Should Have NominalWidth That Is A Valid Number (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            if (_version == ImrVersion.S25)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "NominalWidth", "COBie_Specification",
                    numericExpression.ToString(), "valid number", subContext, "IFCTEXT",
                    title: "Object Type Should Have NominalWidth That Is A Valid Number",
                    ruleId: RuleId("4_07_35", subContext));
            }
            else
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "NominalWidth", "COBie_Specification",
                    notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT");
            }
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.36: Object Type Should Have NominalHeight That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "NominalHeight", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have NominalHeight That Is Defined" : null,
                ruleId: RuleId("4_07_36", subContext));
            // 07.37: Should Have NominalHeight That Is A Valid Number (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            if (_version == ImrVersion.S25)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "NominalHeight", "COBie_Specification",
                    numericExpression.ToString(), "valid number", subContext, "IFCTEXT",
                    title: "Object Type Should Have NominalHeight That Is A Valid Number",
                    ruleId: RuleId("4_07_37", subContext));
            }
            else
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "NominalHeight", "COBie_Specification",
                    notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT");
            }
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.38: Object Type Should Have ModelReference That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "ModelReference", "Pset_ManufacturerTypeInformation", subContext, dataType: "IFCLABEL",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have ModelReference That Is Defined" : null,
                ruleId: RuleId("4_07_38", subContext));
            // 07.39: Object Type Should Not Have ModelReference That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "ModelReference", "Pset_ManufacturerTypeInformation",
                notNaExpression.ToString(), "not n/a", subContext, "IFCLABEL",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have ModelReference That Is 'n/a'" : null,
                ruleId: RuleId("5_07_39", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.40: Object Type Shall Have Shape That Is Defined (Stage4+)
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "Shape", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Shape That Is Defined" : null,
                ruleId: RuleId("4_07_40", subContext));
            // 07.41: Object Type Should Have Shape That Is Not 'n/a' Or Empty (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus).SetMatches(CardinalityEnum.Optional);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "Shape", "COBie_Specification",
                notNaExpression.ToString(), "Non-empty, non-n/a text", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Have Shape That Is Not 'n/a' Or Empty" : null,
                ruleId: RuleId("5_07_41", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.42: Object Type Should Have Size That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "Size", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Size That Is Defined" : null,
                ruleId: RuleId("4_07_42", subContext));
            // 07.43: Object Type Should Not Have Size That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "Size", "COBie_Specification",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have Size That Is 'n/a'" : null,
                ruleId: RuleId("5_07_43", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.44: Object Type Should Have Color That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "Color", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Color That Is Defined" : null,
                ruleId: RuleId("4_07_44", subContext));
            // 07.45: Object Type Should Not Have Color That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "Color", "COBie_Specification",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have Color That Is 'n/a'" : null,
                ruleId: RuleId("5_07_45", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.46: Object Type Should Have Finish That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "Finish", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Finish That Is Defined" : null,
                ruleId: RuleId("4_07_46", subContext));
            // 07.47: Object Type Should Not Have Finish That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "Finish", "COBie_Specification",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have Finish That Is 'n/a'" : null,
                ruleId: RuleId("5_07_47", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.48: Object Type Should Have Grade That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "Grade", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Grade That Is Defined" : null,
                ruleId: RuleId("4_07_48", subContext));
            // 07.49: Object Type Should Not Have Grade That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "Grade", "COBie_Specification",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have Grade That Is 'n/a'" : null,
                ruleId: RuleId("5_07_49", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.50: Object Type Should Have Material That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "Material", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Material That Is Defined" : null,
                ruleId: RuleId("4_07_50", subContext));
            // 07.51: Object Type Should Not Have Material That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "Material", "COBie_Specification",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have Material That Is 'n/a'" : null,
                ruleId: RuleId("5_07_51", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.52: Object Type Should Have Constituents That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "Constituents", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Constituents That Is Defined" : null,
                ruleId: RuleId("4_07_52", subContext));
            // 07.53: Object Type Should Not Have Constituents That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "Constituents", "COBie_Specification",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have Constituents That Is 'n/a'" : null,
                ruleId: RuleId("5_07_53", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.54: Object Type Should Have Features That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "Features", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have Features That Is Defined" : null,
                ruleId: RuleId("4_07_54", subContext));
            // 07.55: Object Type Should Not Have Features That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "Features", "COBie_Specification",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have Features That Is 'n/a'" : null,
                ruleId: RuleId("5_07_55", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.56: Object Type Should Have AccessibilityPerformance That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "AccessibilityPerformance", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have AccessibilityPerformance That Is Defined" : null,
                ruleId: RuleId("4_07_56", subContext));
            // 07.57: Object Type Should Not Have AccessibilityPerformance That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "AccessibilityPerformance", "COBie_Specification",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have AccessibilityPerformance That Is 'n/a'" : null,
                ruleId: RuleId("5_07_57", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.58: Object Type Should Have CodePerformance That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "CodePerformance", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have CodePerformance That Is Defined" : null,
                ruleId: RuleId("4_07_58", subContext));
            // 07.59: Object Type Should Not Have CodePerformance That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "CodePerformance", "COBie_Specification",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have CodePerformance That Is 'n/a'" : null,
                ruleId: RuleId("5_07_59", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 07.60: Object Type Should Have SustainabilityPerformance That Is Defined
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "SustainabilityPerformance", "COBie_Specification", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Shall Have SustainabilityPerformance That Is Defined" : null,
                ruleId: RuleId("4_07_60", subContext));
            // 07.61: Object Type Should Not Have SustainabilityPerformance That Is 'n/a' (Stage5+)
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "SustainabilityPerformance", "COBie_Specification",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Type Should Not Have SustainabilityPerformance That Is 'n/a'" : null,
                ruleId: RuleId("5_07_61", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
        }

        // 08
        private void CreateObjectOccurrenceSpecifications(SpecContext context)
        {
            using var subContext = context.BeginSubscope()
                .AddTag("Object")
                .SetMatches(CardinalityEnum.Optional);
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;

            var applicability = GetEntityApplicability(ids, "Object Occurrence (COBie Component)", DomainExtensions.CobieComponents);

            // Object Occurrence(COBie Component) Shall Have Name Defined
            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcTypeObject.Name), subContext,
                title: _version == ImrVersion.S25 ? "Object Occurrence Shall Have Name Defined" : null);

            // Object Occurrence(COBie Component) Shall Have Name Matching The Projects Information Standard
            // This creates hundreds of Specs as per 5.2.8 of the PIS as we need to test per IFC Type
            CreateObjectOccurrenceNamingSpecifications(subContext);

            // TODO: Object Occurrence(COBie Component) Should Have Unique Name
            subContext.Skip("08.03 Unique support not in IDS");

            // Object Occurrence(COBie Component) Shall Have Description Defined
            CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcProduct.Description), subContext,
                title: _version == ImrVersion.S25 ? "Object Occurrence Shall Have Description Defined" : null,
                ruleId: RuleId("4_08_04", subContext));
            // Object Occurrence Shall Have SerialNumber That Is Defined
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
            subContext.ResetMatches();
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "SerialNumber", "Pset_ManufacturerOccurrence", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Occurrence Shall Have SerialNumber That Is Defined" : null,
                ruleId: RuleId("4_08_05", subContext));
            subContext.SetMatches(CardinalityEnum.Optional);
            // Object Occurrence Should Have SerialNumber That Is 'n/a' Or Valid SerialNumber
            if (_version != ImrVersion.S25)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "SerialNumber", "Pset_ManufacturerOccurrence", numberOrNaExpression.ToString(), "Serial number", subContext, "IFCTEXT");
            }
            // Object Occurrence Should Not Have SerialNumber That Is 'n/a'
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "SerialNumber", "Pset_ManufacturerOccurrence",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Occurrence Should Not Have SerialNumber That Is 'n/a'" : null,
                ruleId: RuleId("4_08_06", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // Object Occurrence Shall Have InstallationDate That Is Defined
            subContext.ResetMatches();
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "InstallationDate", "COBie_Component", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Occurrence Shall Have InstallationDate That Is Defined" : null,
                ruleId: RuleId("4_08_07", subContext));
            subContext.SetMatches(CardinalityEnum.Optional);
            // Object Occurrence Should Have InstallationDate That Is '1900-12-31T23:59:59' Or Actual InstallationDate
            if (_version != ImrVersion.S25)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "InstallationDate", "COBie_Component", dateOrDefaultExpression.ToString(), "Valid date", subContext, "IFCTEXT");
            }
            // Object Occurrence Should Have InstallationDate That Is An Actual Date
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "InstallationDate", "COBie_Component", actualDateExpression.ToString(), "Actual date", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Occurrence Should Have InstallationDate That Is An Actual Date" : null,
                ruleId: RuleId("4_08_08", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // Object Occurrence Shall Have WarrantyStartDate That Is Defined
            subContext.ResetMatches();
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "WarrantyStartDate", "COBie_Component", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Occurrence Shall Have WarrantyStartDate That Is Defined" : null,
                ruleId: RuleId("4_08_09", subContext));
            subContext.SetMatches(CardinalityEnum.Optional);
            // Object Occurrence Should Have WarrantyStartDate That Is '1900-12-31T23:59:59' Or Actual WarrantyStartDate
            if (_version != ImrVersion.S25)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyStartDate", "COBie_Component", dateOrDefaultExpression.ToString(), "Valid date", subContext, "IFCTEXT");
            }
            // Object Occurrence Should Have WarrantyStartDate That Is An Actual Date
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyStartDate", "COBie_Component", actualDateExpression.ToString(), "Actual date", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Occurrence Should Have WarrantyStartDate That Is An Actual Date" : null,
                ruleId: RuleId("4_08_10", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 08.11 S25: TagNumber NotEmpty (Stage4+) − new S25 rule; S21 has no Stage4 TagNumber check
            if (_version == ImrVersion.S25)
            {
                subContext.ResetMatches();
                CreatePropertyNonEmptySpecification(specs, applicability, ids, "TagNumber", "COBie_Component", subContext, dataType: "IFCTEXT",
                    title: "Object Occurrence Shall Have TagNumber That Is Defined",
                    ruleId: RuleId("4_08_11", subContext));
                subContext.SetMatches(CardinalityEnum.Optional);
            }
            // 08.12 S25 / 08.08 S21: TagNumber rule at Stage5+ only
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            if (_version == ImrVersion.S25)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "TagNumber", "COBie_Component",
                    notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                    title: "Object Occurrence Should Not Have TagNumber That Is 'n/a'",
                    ruleId: RuleId("4_08_12", subContext));
            }
            else
            {
                CreatePropertyWithValueSpecification(specs, applicability, ids, "TagNumber", "COBie_Component", "n/a", subContext, dataType: "IFCTEXT",
                    title: null);
            }
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // Object Occurrence Shall Have BarCode That Is Defined
            subContext.ResetMatches();
            CreatePropertyNonEmptySpecification(specs, applicability, ids, "BarCode", "Pset_ManufacturerOccurrence", subContext, dataType: "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Occurrence Shall Have BarCode That Is Defined" : null,
                ruleId: RuleId("4_08_13", subContext));
            subContext.SetMatches(CardinalityEnum.Optional);
            // Object Occurrence Should Have BarCode That Is 'n/a' Or Actual BarCode
            if (_version != ImrVersion.S25)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "BarCode", "Pset_ManufacturerOccurrence", numberOrNaExpression.ToString(), "Bar code", subContext, "IFCTEXT");
            }
            // Object Occurrence Should Not Have BarCode That Is 'n/a'
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "BarCode", "Pset_ManufacturerOccurrence",
                notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                title: _version == ImrVersion.S25 ? "Object Occurrence Should Not Have BarCode That Is 'n/a'" : null,
                ruleId: RuleId("4_08_14", subContext));
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // 08.15 S25: AssetIdentifier NotEmpty (Stage4+) − new S25 rule; S21 has no Stage4 AssetIdentifier check
            if (_version == ImrVersion.S25)
            {
                subContext.ResetMatches();
                CreatePropertyNonEmptySpecification(specs, applicability, ids, "AssetIdentifier", "COBie_Component", subContext, dataType: "IFCTEXT",
                    title: "Object Occurrence Shall Have AssetIdentifier That Is Defined",
                    ruleId: RuleId("4_08_15", subContext));
                subContext.SetMatches(CardinalityEnum.Optional);
            }
            // 08.16 S25 / 08.10 S21: AssetIdentifier rule at Stage5+ only
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            if (_version == ImrVersion.S25)
            {
                CreatePropertyWithPatternSpecification(specs, applicability, ids, "AssetIdentifier", "COBie_Component",
                    notNaExpression.ToString(), "not n/a", subContext, "IFCTEXT",
                    title: "Object Occurrence Should Not Have AssetIdentifier That Is 'n/a'",
                    ruleId: RuleId("4_08_16", subContext));
            }
            else
            {
                CreatePropertyWithValueSpecification(specs, applicability, ids, "AssetIdentifier", "COBie_Component", "n/a", subContext, dataType: "IFCTEXT",
                    title: null);
            }
            subContext.SetApplicableStages(RibaStages.Stage4Plus);

            // Any Door Should Have FireRating That Is Defined
            subContext.ResetMatches();
            var doorApplicability = GetEntityApplicability(ids, "Door", "IfcDoor");
            CreatePropertyNonEmptySpecification(specs, doorApplicability, ids, "FireRating", "Pset_DoorCommon", subContext, dataType: "IFCLABEL",
                title: _version == ImrVersion.S25 ? "Object Occurrence (Door) Shall Have FireRating That Is Defined" : null,
                ruleId: RuleId("4_08_17", subContext));
            // Object Occurrence (Door) Shall Have FireRating That Is From PickList Provided In The Projects Information Standard
            CreatePropertyFromListSpecification(specs, doorApplicability, ids, "FireRating", "Pset_DoorCommon", new[] { "Undefined", "n/a", "20", "30", "60", "90", "120" }, subContext, "IFCLABEL",
                title: _version == ImrVersion.S25 ? "Object Occurrence (Door) Shall Have FireRating That Is From PickList Provided In The Projects Information Standard" : null,
                ruleId: RuleId("4_08_18", subContext));
            subContext.ResetMatches();
            // TODO: S25 08.19 - Object Occurrence Shall Not Contain Duplicate Names
            subContext.Skip("08:19: Duplicates not supported in IDS 1.0");
            // TODO: S25 08.20 - Object Occurrence Shall Have Layer Correctly Defined
            // IfcPresentationLayerAssignment relates to Representations - so not directly linked to Occurrences
            subContext.Skip("08:20: PresentationLayers need further info");

        }

        // 09
        private void CreateSystemSpecifications(SpecContext context)
        {
            using var subContext = context.BeginSubscope()
                .AddTag("System");
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "System", "IfcSystem", false);
            // See PIS 5.2.9 & table
            // 09.01 S25: System Name Defined � IfcSystem.Name is available in IFC2X3
            if (_version == ImrVersion.S25)
            {
                CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcTypeObject.Name), subContext,
                    title: "System Shall Have Name Defined");
            }
            // 09.02 S25 / 09.01 S21: System Name Matching PIS — needs PIS 5.2.9 system name list (not yet in config)
            AsNomenclature(subContext, () =>
                CreateAttributePatternSpecification(specs, applicability, ids, nameof(IIfcTypeObject.Name),
                    GetSystemNamePattern(), subContext,
                    title: "System Shall Have Name Matching The Projects Information Standard"));
            // 09.03 S25: System Description Defined � IfcSystem.Description is available in IFC2X3
            if (_version == ImrVersion.S25)
            {
                CreateAttributeNonEmptySpecification(specs, applicability, ids, nameof(IIfcTypeObject.Description), subContext,
                    title: "System Shall Have Description Defined");
            }
            // 09.04 S25 / 09.02 S21: System Description Matching PIS — needs PIS 5.2.9 description list (not yet in config)
            AsNomenclature(subContext, () =>
                CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcTypeObject.Description),
                    GetSystemDescriptions(), subContext,
                    title: "System Shall Have Description Matching The Projects Information Standard"));
            // 09.05_01 S25: System Uniclass Classification Defined (primary: IFC classification)
            if (_version == ImrVersion.S25)
            {
                CreateClassificationDefinedSpecification(specs, applicability, ids, UniclassSystemLabel,
                    new ValueConstraint(UniclassSystemLabel), subContext,
                    title: "System Shall Have Uniclass Classification Defined",
                    ruleId: RuleId("4_09_05_01", subContext));
            }
            // 09.05_02 S25: System Uniclass Classification Defined (secondary: property fallback for tools that cannot attach IFC classification to IfcSystem)
            if (_version == ImrVersion.S25)
            {
                subContext.SetMatches(CardinalityEnum.Optional);
                CreatePropertyNonEmptySpecification(specs, applicability, ids, "SystemCategory", "Additional_Pset_SystemCommon", subContext, dataType: "IFCTEXT",
                    title: "System Should Have Uniclass Classification Defined",
                    ruleId: RuleId("4_09_05_02", subContext));
                subContext.ResetMatches();
            }
            // 09.06_01 S25 / 09.03 S21: System Uniclass Matching PIS (primary: IFC classification)
            AsNomenclature(subContext, () =>
                CreateClassificationPatternSpecification(specs, applicability, ids, UniclassSystemLabel, "Ss_.*", subContext,
                    title: _version == ImrVersion.S25 ? "System Shall Have Uniclass Classification Matching The Projects Information Standard" : null,
                    ruleId: RuleId("4_09_06_01", subContext)));
            // 09.06_02 S25: System Uniclass Matching PIS (secondary: property fallback)
            if (_version == ImrVersion.S25)
            {
                subContext.SetMatches(CardinalityEnum.Optional);
                AsNomenclature(subContext, () =>
                    CreatePropertyWithPatternSpecification(specs, applicability, ids, "SystemCategory", "Additional_Pset_SystemCommon", "Ss_.*", "Ss Systems", subContext, "IFCTEXT",
                        title: "System Should Have Uniclass Classification Matching The Projects Information Standard",
                        ruleId: RuleId("4_09_06_02", subContext)));
                subContext.ResetMatches();
            }
        }

        // 07.05
        /// <summary>
        /// Builds a rule checking the Name is appropriate to the entity Type.
        /// </summary>
        /// <remarks>Supports IfcTypeObjects and Cobie Components</remarks>
        /// <param name="ids"></param>
        /// <param name="specs"></param>
        /// <param name="context"></param>
        /// <param name="baseType"></param>
        private void CreateObjectTypeNamingSpecifications(SpecContext context)
        {
            var baseTypes = new string[] { "IfcTypeObject" };
            var objectLabel = "Type";
            var dfeDict = GetDfeTypes();    // Maps Uppercase PredefinedTypes to Proper-case

            // Start new context as we build at least one spec per applicable type as part of a single Rule. e.g. 5.7.BeamType
            using var subContext = context.BeginSubscope()
                .SetApplicableToGeneration(GenerationPass.Complex)
                .SetMatches(CardinalityEnum.Optional);  // Optional since models won't have every single elementType/PDT we specify the naming rules on
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;

            var roots = Schema.Where(e => baseTypes.Contains(e.Name));
            var ifcTypes = roots.SelectMany(r => r.MatchingConcreteClasses).Distinct();

            foreach (var ifcType in ifcTypes)
            {
                if (ifcType.Name == "IfcTypeObject" || ifcType.Name == "IfcTypeProduct")    // Should be abstract
                    continue;
                // remove 'Ifc' prefix
                var typeName = ifcType.Name.Substring(3);
                // remove any 'Type' suffixes - Types follow same base rules as occurences
                var entityName = RemoveSuffix("Style", RemoveSuffix("Type", typeName));
                if (typeName.Contains("BuildingElementProxy"))   // BEP is special case for Field1 (EntityName)
                {
                    // Any proper-case entity name allowed for Proxies
                    entityName = "([A-Z][a-z]+)+";
                }

                var isApplicable = enumTypeExceptions.Contains(typeName) == false;    // Item classed as applicable for PredefinedType
                if (isApplicable && ifcType.PredefinedTypeValues.Any())
                {
                    // Create another scope as we're building a spec per Entity + PDT enumeration type to check names e.g. 5.7.BeamType.Joist
                    using var pdtContext = subContext.BeginSubscope(typeName);
                    foreach (var pdt in ifcType.PredefinedTypeValues)
                    {
                        if (pdt == "NOTDEFINED")
                        {
                            // Types with NOTDEFINED PDT are prohibited by 07.03. So no sense defining a naming rule
                            continue;
                        }
                        var pureIfc2x3TypeClass = SchemaInfo.SchemaIfc2x3[ifcType.Name];
                        if (pureIfc2x3TypeClass == null || !pureIfc2x3TypeClass.PredefinedTypeValues.Contains(pdt, StringComparer.OrdinalIgnoreCase))
                            continue;

                        if(!dfeDict.TryGetValue(pdt, out string? enumerationName))
                        {
                            Console.Error.WriteLine($"Missing Enum: {entityName} : {pdt}");
                            enumerationName = pdt;
                        }
                        var applicability = GetEntityApplicabilityWithPredefinedType(ids, $"{objectLabel}", ifcType.Name, pdt, includeSubTypes: false);
                        if (pdt == "USERDEFINED")
                        {
                            // Any Proper-cased Enum will do
                            // EntityName_CustomEnumerationField_TypeNN
                            var pattern = $"{entityName}_(([A-Z][a-z]+)+_)Type[A-Za-z]{{0,3}}\\d{{2,4}}";
                            CreateAttributePatternSpecification(specs, applicability, ids, "Name", pattern, pdtContext.SetName(pdt));
                        }
                        else
                        {
                            //if(pdt.Length > 35)
                            //{
                            //    Console.WriteLine($"{entityName}.{enumerationName}");
                            //}
                            // PDTs must use the proper case PDT in field 2 of the name
                            // EntityName_EnumerationField_TypeNN
                            var pattern = $"{entityName}_{enumerationName}_Type[A-Za-z]{{0,3}}\\d{{2,4}}";
                            CreateAttributePatternSpecification(specs, applicability, ids, "Name", pattern, pdtContext.SetName(pdt));
                        }

                    }

                }
                else
                {
                    // We don't have a PDT, or it's not applicable to enumerate (e.g. Doors, Furniture)
                    var applicability = GetEntityApplicability(ids, $"{objectLabel}", ifcType.Name, includeSubTypes: false);
                    // EntityName_<OptionalEnumField>_TypeNN
                    var pattern = $"{entityName}_(([A-Z][a-z]+)+_)?Type[A-Za-z]{{0,3}}\\d{{2,4}}";
                    CreateAttributePatternSpecification(specs, applicability, ids, "Name", pattern, subContext.SetName(typeName));
                }

            }
        }

        // 08.02
        private void CreateObjectOccurrenceNamingSpecifications(SpecContext context)
        {
            var baseTypes = DomainExtensions.CobieComponents;
            var objectLabel = "Object Occurrence [COBie]";
            var dfeDict = GetDfeTypes();    // Maps Uppercase PredefinedTypes to Proper-case

            IEnumerable<IdsLib.IfcSchema.ClassInfo> roots;
            if (UseIfc4TypesIn2x3)
            {
                var schema = new HybridSchemaIfc2x3();
                roots = schema.Where(e => baseTypes.Contains(e.Name));
            }
            else
            {
                roots = Schema.Where(e => baseTypes.Contains(e.Name));
            }
            var ifcTypes = roots.SelectMany(r => r.MatchingConcreteClasses).Distinct().ToList();

            // 08.02.01 Shall — format check for normal occurrences: ([A-Z]-)?[A-Z]{1,5}-\d{2,5}
            using var shallNormalContext = context.BeginSubscope()
                .AddTag("Object Naming Shall")
                .SetApplicableToGeneration(GenerationPass.All)
                .SetMatches(CardinalityEnum.Optional);  // Optional since models won't have every single elementType/PDT we specify the naming rules for
            var shallNormalSpecs = shallNormalContext.CurrentSpecGroup;
            var shallNormalIds = shallNormalContext.Ids;

            foreach (var ifcType in ifcTypes)
            {
                var typeName = ifcType.Name.Substring(3);
                if (!typeCodeDict.TryGetValue(typeName, out TypeMap? typeCode)) continue;
                if (typeCode.UsesSpaceNaming) continue;

                using var typeContext = shallNormalContext.BeginSubscope(typeName);
                var applicability = GetEntityApplicability(shallNormalIds, objectLabel, ifcType.Name, includeSubTypes: false);
                CreateAttributePatternSpecification(shallNormalSpecs, applicability, shallNormalIds, "Name", @"([A-Z]-)?[A-Z]{1,5}-\d{2,5}",
                    typeContext.SetName(typeName),
                    title: "Object Occurrence Shall Have Name Matching Format Set Out In The Projects Information Standard",
                    ruleId: RuleId("4_08_02_01", shallNormalContext));
            }

            // 08.02.02 Shall — format check for space-named occurrences (Door, Window): ([A-Z]-)?{spaceNameRegex}-[A-Z]{1,5}\d{2,3}
            using var shallSpaceContext = context.BeginSubscope()
                .AddTag("Object Naming Shall Space")
                .SetApplicableToGeneration(GenerationPass.All)
                .SetMatches(CardinalityEnum.Optional);      // Not all models (e.g. MEP) will contain space-named elements, so optional to avoid failures where not applicable
            var shallSpaceSpecs = shallSpaceContext.CurrentSpecGroup;
            var shallSpaceIds = shallSpaceContext.Ids;

            foreach (var ifcType in ifcTypes)
            {
                var typeName = ifcType.Name.Substring(3);
                if (!typeCodeDict.TryGetValue(typeName, out TypeMap? typeCode)) continue;
                if (!typeCode.UsesSpaceNaming) continue;

                using var typeContext = shallSpaceContext.BeginSubscope(typeName);
                var applicability = GetEntityApplicability(shallSpaceIds, objectLabel, ifcType.Name, includeSubTypes: false);
                CreateAttributePatternSpecification(shallSpaceSpecs, applicability, shallSpaceIds, "Name", @$"([A-Z]-)?{spaceNameRegex}-[A-Z]{{1,5}}\d{{2,3}}",
                    typeContext.SetName(typeName),
                    title: "Object Occurrence Shall Have Name Matching Format Set Out In The Projects Information Standard",
                    ruleId: RuleId("4_08_02_02", shallSpaceContext));
            }

            // 08.02.03 Should — specific PIS code check for normal occurrences per entity+PDT
            using var shouldNormalContext = context.BeginSubscope()
                .AddTag("Object Naming Should")
                .SetApplicableToGeneration(GenerationPass.Complex)
                .SetMatches(CardinalityEnum.Optional);
            var shouldNormalSpecs = shouldNormalContext.CurrentSpecGroup;
            var shouldNormalIds = shouldNormalContext.Ids;

            foreach (var ifcType in ifcTypes)
            {
                var typeName = ifcType.Name.Substring(3);
                if (!typeCodeDict.TryGetValue(typeName, out TypeMap? typeCode)) continue;
                if (typeCode.UsesSpaceNaming) continue;

                const string suffix = "\\d{2,5}";

                if (!typeCode.HasOverides)
                {
                    using var typeContext = shouldNormalContext.BeginSubscope(typeName);
                    var applicability = GetEntityApplicability(shouldNormalIds, objectLabel, ifcType.Name, includeSubTypes: false);
                    var code = typeCode.GetCode();
                    CreateAttributePatternSpecification(shouldNormalSpecs, applicability, shouldNormalIds, "Name", $"([A-Z]-)?{code}-{suffix}",
                        typeContext.SetName(typeName),
                        ruleId: RuleId("4_08_02_03", shouldNormalContext));
                }
                else
                {
                    // One spec per PDT
                    // Propagate PDTs from Type, e.g. Vibration Isolators are DiscreteAccessories in IFC2x3 defined by VibrationIsolatorType
                    var definingTypes = ifcType.RelationTypeClasses!;
                    var pdts = definingTypes.Select(t => Schema[t]).Where(c => c is not null).SelectMany(c => c!.PredefinedTypeValues);

                    foreach (var pdt in pdts)
                    {
                        using var pdtContext = shouldNormalContext.BeginSubscope(typeName);
                        if (pdt == "NOTDEFINED")
                            continue;
                        var pureIfc2x3OccClass = SchemaInfo.SchemaIfc2x3[ifcType.Name];
                        if (pureIfc2x3OccClass == null || !pureIfc2x3OccClass.PredefinedTypeValues.Contains(pdt, StringComparer.OrdinalIgnoreCase))
                            continue;

                        var applicability = GetEntityApplicabilityWithPredefinedType(shouldNormalIds, objectLabel, ifcType.Name, pdt, includeSubTypes: false);
                        var code = typeCode.GetCode(pdt);
                        CreateAttributePatternSpecification(shouldNormalSpecs, applicability, shouldNormalIds, "Name", $"([A-Z]-)?{code}-{suffix}",
                            pdtContext.SetName(pdt),
                            ruleId: RuleId("4_08_02_03", shouldNormalContext));
                    }
                    if (!pdts.Any())
                        Console.WriteLine($"WARNING: Type {ifcType.Name} has no predefined Types");
                }
            }

            // 08.02.04 Should — specific PIS code check for space-named occurrences (Door, Window)
            using var shouldSpaceContext = context.BeginSubscope()
                .AddTag("Object Naming Should Space")
                .SetApplicableToGeneration(GenerationPass.Complex)
                .SetMatches(CardinalityEnum.Optional);
            var shouldSpaceSpecs = shouldSpaceContext.CurrentSpecGroup;
            var shouldSpaceIds = shouldSpaceContext.Ids;

            foreach (var ifcType in ifcTypes)
            {
                var typeName = ifcType.Name.Substring(3);
                if (!typeCodeDict.TryGetValue(typeName, out TypeMap? typeCode)) continue;
                if (!typeCode.UsesSpaceNaming) continue;

                using var typeContext = shouldSpaceContext.BeginSubscope(typeName);
                var applicability = GetEntityApplicability(shouldSpaceIds, objectLabel, ifcType.Name, includeSubTypes: false);
                var code = typeCode.GetCode();
                CreateAttributePatternSpecification(shouldSpaceSpecs, applicability, shouldSpaceIds, "Name", @$"([A-Z]-)?{spaceNameRegex}-{code}\d{{2,3}}",
                    typeContext.SetName(typeName),
                    ruleId: RuleId("4_08_02_04", shouldSpaceContext));
            }
        }

        private string VersionedContentPath(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Dfe", "Content", $"{_version}_{name}.txt");

        private IEnumerable<string[]> ReadVersionedContent(string name) =>
            File.ReadAllLines(VersionedContentPath(name))
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"))
                .Select(l => l.Split(':').Select(p => p.Trim()).ToArray());

        /// <summary>
        /// Warns to stderr if the active TypeCodes file declares a Uniclass SL version in its header
        /// that does not match the requested --uniclass-version. Non-fatal; generation continues.
        /// </summary>
        private void WarnIfUniclassVersionMismatch(DfeOptions options)
        {
            if (string.IsNullOrEmpty(options.UniclassVersion)) return;

            var path = VersionedContentPath("TypeCodes");
            if (!File.Exists(path)) return;

            var headerLine = File.ReadLines(path)
                .TakeWhile(l => l.TrimStart().StartsWith("#"))
                .FirstOrDefault(l => l.Contains("uniclass-sl-version:"));

            if (headerLine == null) return;

            var declared = headerLine.Split(':').LastOrDefault()?.Trim().Replace(".", "_");
            if (!string.Equals(declared, options.UniclassVersion, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"WARNING: {Path.GetFileName(path)} was authored against Uniclass SL v{declared?.Replace("_", ".")} " +
                    $"but --uniclass-version={options.UniclassVersion.Replace("_", ".")} was requested. " +
                    "SL code cross-references may be invalid.");
            }
        }

        /// <summary>Returns a version annotation string for IDS descriptions, e.g. " [Classification versions: Uniclass SL/EN v1.32]", or empty when no versions are pinned.</summary>
        private string BuildClassificationVersionNote()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(_classificationVersions.UniclassVersion))
                parts.Add($"Uniclass SL/EN v{_classificationVersions.UniclassVersion.Replace("_", ".")}");
            if (!string.IsNullOrEmpty(_classificationVersions.NrmVersion))
                parts.Add($"NRM {_classificationVersions.NrmVersion}");
            if (!string.IsNullOrEmpty(_classificationVersions.Sfg20Version))
                parts.Add($"SFG20 {_classificationVersions.Sfg20Version}");
            return parts.Count > 0 ? $" [Classification versions: {string.Join(", ", parts)}]" : "";
        }

        /// <summary>Returns valid space classification codes for the active IMR version (ADS for S21, Space codes for S25).</summary>
        private IEnumerable<string> GetSpaceCodes() =>
            ReadVersionedContent("TypeCodes").Select(r => r[0]);

        /// <summary>Groups space classification codes by their Uniclass SL code.</summary>
        public IDictionary<string, IEnumerable<string>> GetUniclassSpaceMap() =>
            ReadVersionedContent("TypeCodes")
                .GroupBy(r => r[2])
                .ToDictionary(g => g.Key, g => g.Select(r => r[0]));

        /// <summary>
        /// Returns the valid Uniclass SL codes for the active IMR version, derived from the TypeCodes file.
        /// Update the S21_TypeCodes.txt / S25_TypeCodes.txt file when Uniclass tables are revised.
        /// </summary>
        private new IEnumerable<string> GetUniclassSLCodes() =>
            ReadVersionedContent("TypeCodes").Select(r => r[2]).Distinct();

        private IEnumerable<string> GetZoneCodes() =>
            ReadVersionedContent("Zones").Select(r => r[0]);

        private IEnumerable<string> GetZoneCategories() =>
            ReadVersionedContent("Zones").Select(r => r[1]).Distinct();

        private IEnumerable<string> GetZoneDescriptions() =>
            ReadVersionedContent("Zones").Select(r => r[2]);

        private IEnumerable<string[]> GetZoneData() =>
            ReadVersionedContent("Zones");

        private string GetSystemNamePattern()
        {
            var escaped = ReadVersionedContent("Systems").Select(r => Regex.Escape(r[0]));
            return $"({string.Join("|", escaped)})_System\\d{{2,3}}";
        }

        private IEnumerable<string> GetSystemDescriptions() =>
            ReadVersionedContent("Systems").Select(r => r[1]);

        private IDictionary<string, string> GetDfeTypes() =>
            ReadVersionedContent("Naming")
                .Select(r => new { Key = r[1], ProperCase = r[2] })
                .DistinctBy(r => r.Key)
                .ToDictionary(r => r.Key, r => r.ProperCase);

        


        /// <summary>
        /// Common requirements across Projects, Sites, Buildings etc
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="group"></param>
        /// <param name="entity"></param>
        /// <param name="name"></param>
        /// <param name="description"></param>
        /// <param name="context"></param>
        /// <summary>
        /// Temporarily overrides ApplicableToGeneration to Complex so that CrossReference /
        /// "Matching PIS" specs appear in the Nomenclature-and-Classification output only
        /// (and in the Full Combined output via the All pass), but not in the Core Only output.
        /// </summary>
        private static void AsNomenclature(SpecContext context, Action action)
        {
            var orig = context.ApplicableToGeneration;
            context.SetApplicableToGeneration(GenerationPass.Complex);
            action();
            context.SetApplicableToGeneration(orig);
        }

        private void CreateCommonRequirements(Xids ids, FacetGroup entity, string name, string description, SpecContext context, string? globalIdTitle = null, string modalVerb = "Should")
        {
            var group = context.CurrentSpecGroup;
            CreateAttributeNonEmptySpecification(group, entity, ids, nameof(IIfcRoot.GlobalId), context, globalIdTitle);

            CreateAttributeNonEmptySpecification(group, entity, ids, nameof(IIfcRoot.Name), context,
                title: $"{entity.Name} {modalVerb} Have Name Defined");
            AsNomenclature(context, () =>
                CreateAttributeValueSpecification(group, entity, ids, nameof(IIfcRoot.Name), name, context,
                    $"{entity.Name} {modalVerb} Have Name Matching The Projects Information Standard"));

            CreateAttributeNonEmptySpecification(group, entity, ids, nameof(IIfcRoot.Description), context,
                title: $"{entity.Name} {modalVerb} Have Description Defined");
            AsNomenclature(context, () =>
                CreateAttributeValueSpecification(group, entity, ids, nameof(IIfcRoot.Description), description, context,
                    $"{entity.Name} {modalVerb} Have Description Matching The Projects Information Standard"));
        }


    }
}
