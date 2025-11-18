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
        internal static readonly Regex uniclassExpression = new(@".*[Uu]niclass.*");
        // Not perfect but likely good enough for our purposes: https://www.regular-expressions.info/email.html
        internal const string emailRegex = @"([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,})";
        internal static readonly Regex emailOrNaExpression = new($@"n\/a|{emailRegex}");
        internal static readonly Regex emailExpression = new(emailRegex);
        internal static readonly Regex numericExpression = new($@"\d+(\.\d+)?");
        internal static readonly Regex numericOrNaExpression = new($@"n\/a|\d+(\.\d+)?");
        internal static readonly Regex monetaryOrNaExpression = new($@"n\/a|£?\d+(\.\d{{2}})?");
        internal static readonly Regex textOrNaExpression = new($@"n\/a|(\w.*)+");
        internal static readonly Regex numberOrNaExpression = new($@"n\/a|(\d|-| |_)+");
        internal static readonly Regex dateOrDefaultExpression = new(@"1900-12-31T23:59:59|20\d{2}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12][0-9]|3[01])(?:T(?:[01][0-9]|2[0-3]):(?:[0-5][0-9]):(?:[0-5][0-9])(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?)?");
        private readonly IServiceProvider provider;

        static readonly IDictionary<RibaStages, string> ribaStagesDict = new Dictionary<RibaStages, string>()
        {
            [RibaStages.Stage1] = "RIBA Stage 1: Preparation and Brief",
            [RibaStages.Stage2] = "RIBA Stage 2: Concept Design",
            [RibaStages.Stage3] = "RIBA Stage 3: Spatial Coordination",
            [RibaStages.Stage4] = "RIBA Stage 4: Technical Design",
            [RibaStages.Stage5] = "RIBA Stage 5: Construction and Manufacturing",
            [RibaStages.Stage6] = "RIBA Stage 6: Handover and Close Out",
            [RibaStages.Stage7] = "RIBA Stage 7: Use",
        };

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
            ["AirTerminal"] = new TypeMap("AIR"),
            ["AirTerminalBox"] = new TypeMap("ATB"),
            ["AirToAirHeatRecovery"] = new TypeMap("ATA"),
            ["Alarm"] = new TypeMap("ALR"),
            ["Boiler"] = new TypeMap("BLR"),
            ["BuildingElementProxy"] = new TypeMap("OTH"),
            ["Chiller"] = new TypeMap("CHL"),
            ["Coil"] = new TypeMap("COIL"),
            ["Compressor"] = new TypeMap("CMP"),
            ["Condenser"] = new TypeMap("CND"),
            ["Controller"] = new TypeMap("CRL"),
            ["CooledBeam"] = new TypeMap("CBM"),
            ["CoolingTower"] = new TypeMap("CTR"),
            ["Damper"] = new TypeMap("DMP"),
            ["DiscreteAccessory"] = new TypeMap("DAC"),
            ["DistributionChamberElement"] = new TypeMap("DCE"),
            ["Door"] = new TypeMap("D").SpaceNaming(),
            ["ElectricAppliance"] = new TypeMap("EAP"),
            ["ElectricDistributionPoint"] = new TypeMap("EDP"),
            ["ElectricFlowStorageDevice"] = new TypeMap("EFS"),
            ["ElectricGenerator"] = new TypeMap("EGN"),
            ["ElectricHeater"] = new TypeMap("EHT"),
            ["ElectricMotor"] = new TypeMap("EMT"),
            ["ElectricTimeControl"] = new TypeMap("ETC"),
            ["EvaporativeCooler"] = new TypeMap("ECL"),
            ["Evaporator"] = new TypeMap("EVP"),
            ["Fan"] = new TypeMap("FAN"),
            ["Filter"] = new TypeMap("FLT"),
            ["FireSuppressionTerminal"] = new TypeMap("FST"),
            ["FlowInstrument"] = new TypeMap("FIN"),
            ["FlowMeter"] = new TypeMap("FMT"),
            ["Furniture"] = new TypeMap("FRN"),
            ["FurnishingElement"] = new TypeMap("FRN"),// Added
            ["GasTerminal"] = new TypeMap("GTM"),
            ["HeatExchanger"] = new TypeMap("HEX"),
            ["Humidifier"] = new TypeMap("HUM"),
            ["LightFixture"] = new TypeMap("LFT"),
            //["Lamp"] = new TypeMap("LFT"),  // Added
            ["MotorConnection"] = new TypeMap("MCN"),
            ["Outlet"] = new TypeMap("OUT"),
            ["ProtectiveDevice"] = new TypeMap("PDV"),
            ["Pump"] = new TypeMap("PMP"),
            ["SanitaryTerminal"] = new TypeMap("SAN"),
            ["Sensor"] = new TypeMap("SNS"),
            ["SpaceHeater"] = new TypeMap("SPH")
                .OverrideWith("RADIATOR", "RAD")
                .OverrideWith("PANELRADIATOR", "RAD")
                .OverrideWith("SECTIONALRADIATOR", "RAD")
                .OverrideWith("TUBULARRADIATOR", "RAD"),
            ["StackTerminal"] = new TypeMap("STM"),
            ["SwitchingDevice"] = new TypeMap("SWD"),
            ["SystemFurnitureElement"] = new TypeMap("SFE"),
            ["Tank"] = new TypeMap("TNK"),
            ["Transformer"] = new TypeMap("TRF"),
            ["TransportElement"] = new TypeMap("TRE")
                .OverrideWith("ELEVATOR", "ELE")
                .OverrideWith("ESCALATOR", "ESC")
                .OverrideWith("MOVINGWALKWAY", "MOV")
            ,
            ["TubeBundle"] = new TypeMap("TBN"),
            ["UnitaryEquipment"] = new TypeMap("UEQ")
                .OverrideWith("AIRCONDITIONINGUNIT", "ACU")
                .OverrideWith("AIRHANDLER", "AHU"),
            ["Valve"] = new TypeMap("VLV"),
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
            var version = 45;

            var generations = new[] { GenerationPass.Core, GenerationPass.Complex, GenerationPass.All };

            foreach (var targetGeneration in generations)
            {

                var stages = new[] { RibaStages.Stage3, RibaStages.Stage4, RibaStages.Stage5 }; //  RibaStages.Stage6
                foreach (var targetStage in stages)
                {
                    config.ProjectPhase = ribaStagesDict[targetStage];
                    var status = "S2";
                    var revision = "P01";

                    var ids = new Xids
                    {
                        // Note: not part of IDS standard - only in json export. Main public meta data on SpecificationGroup items
                        Guid = Guid.NewGuid().ToString(),
                        Name = $"{targetGeneration} DfE EIR model checks for {config.ProjectName} at {targetStage}",
                        Project = new Project   
                        {
                            Guid = Guid.NewGuid().ToString(),
                            Name = config.ProjectName,
                            Description = config.ProjectDescription
                        },
                        Stages = new List<string> { config.ProjectPhase },
                        SpecificationsGroups = new List<SpecificationsGroup>()
                    };


                    var specLogger = provider.GetRequiredService<ILogger<SpecContext>>();
                    using var ctx = specLogger.BeginScope(targetStage.ToString());
                    using var ctx2 = specLogger.BeginScope(targetGeneration.ToString());
                    // Initialise a Spec context to help organise / number specs.
                    using var context = new SpecContext(targetStage, ids, targetGeneration, specLogger);
                    context.SetApplicableStages(RibaStages.All);
                    context.SetApplicableToGeneration(GenerationPass.Core);      // Determines whether to separate complex (e.g. naming) rules out from 'core' vs a single file ('All')
                    context.BasePath = "DFE-ER";
                    context.SaveOneFilePerSpec = true;        // Output individual files
                    context.SaveOneFilePerScope = true;       // Use Context structure to group into smaller Spec Groups (produces IDS zip)


                    CleanPriorFiles(context, targetStage);

                    SpecificationsGroup rootGroup = InitialiseSpecGroup(context, config, revision);
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
                    var suffix = context.TargetGenerationPass switch
                    {
                        GenerationPass.Core => "-Core",
                        GenerationPass.Complex => "-Naming",
                        GenerationPass.All => "",
                        _ => throw new NotImplementedException(),
                    };
                    var grouped = GroupCommonApplicableRequirements ? " Grouped" : ""; 
                    
                    var fileName = $"{context.BasePath}/ER-DFE-XX-XX-L-X-{version:D4}-Information Model {targetStage} Assurance{grouped}-{status}-{revision}{suffix}.ids";

                    var totalSpecs = ids.AllSpecifications().Count();
                    if (context.SaveOneFilePerScope)
                    {
                        if (ids.SpecificationsGroups.Count > 1)
                        {
                            if (GroupCommonApplicableRequirements)
                            {
                                GroupRequirementsByApplicability(ids);
                            }

                            var zipFileName = Path.ChangeExtension(fileName, "zip");
                            ids.ExportBuildingSmartIDS(zipFileName, specLogger);
                            specLogger.LogInformation("Created group IDS file {fileName} with {specs} specifications in {groups} groups", zipFileName, totalSpecs, ids.SpecificationsGroups.Count);
                            // Unpack the grouped files
                            var unpackFolder = Path.Combine(context.BasePath, Path.Combine("Group", context.TargetStage.ToString()));
                            if (Directory.Exists(unpackFolder))
                                Directory.Delete(unpackFolder, true);
                            Directory.CreateDirectory(unpackFolder);
                            ZipFile.ExtractToDirectory(zipFileName, unpackFolder);
                            File.Delete(zipFileName);   // Redundant
                        }
                        else
                        {
                            specLogger.LogWarning("Only a single spec group found. Producing single ids file only");
                        }
                        // Consolidate spec groups to single group so we can save a single file
                        foreach (var spec in ids.AllSpecifications().OrderBy(s => s.Guid))
                        {
                            rootGroup.Specifications.Add(spec);
                        }
                        ids.SpecificationsGroups.Clear();
                        ids.SpecificationsGroups.Add(rootGroup);
                    }

                    ids.ExportBuildingSmartIDS(fileName, specLogger);
                    specLogger.LogInformation("Created single IDS file {fileName} with {specs} specifications", fileName, totalSpecs);

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
                    spec.Name = $"{firstSpec.Guid}-{lastSpec.Guid}: {groupName} ({groupedSpecs.Count()} requirements)";
                    spec.Guid = groupedSpecs.Aggregate(new StringBuilder(),
                        (curr, next) => curr.Append(curr.Length == 0 ? "" : ",").Append(next.Guid)).ToString();
                    spec.Description = groupedSpecs.Aggregate(new StringBuilder(), 
                        (curr, next) => curr.Append(curr.Length == 0 ? $"{groupName} " : ", and ").Append(next.Description?.Replace($"{groupName} ", ""))).ToString();
                    spec.Instructions = groupedSpecs.Aggregate(new StringBuilder(),
                        (curr, next) => string.IsNullOrEmpty(next.Instructions) ? curr : curr.Append(curr.Length == 0 ? "" : ". ").Append(next.Guid).Append(": ").Append(next.Instructions)).ToString();
                        

                }
            }
        }

        private void CleanPriorFiles(SpecContext context, RibaStages stage)
        {
            // Clean folders in case we renamed / deleted files
            var path = Path.Combine(context.BasePath, stage.ToString());
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }

        private void ValidateStage(SpecContext context)
        {
            var path = Path.Combine(context.BasePath, context.TargetStage.ToString());
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

        private static SpecificationsGroup InitialiseSpecGroup(SpecContext context, DfeConfig config, string revision)
        {
            var now = DateTime.UtcNow;
            var targetStage = context.TargetStage;
            var targetGeneration = context.TargetGenerationPass;

            var specGroup = new SpecificationsGroup(context.Ids)
            {
                Date = now,
                Guid = Guid.NewGuid().ToString(),
                Name = $"Information Model RIBA {targetStage} Assurance for {config.ProjectName}",
                Specifications = new List<Specification>(),
                Milestone = ribaStagesDict[targetStage],
                Author = "DfE.BIM@Education.gov.uk",
                Description = $"Assurance of IFC-SPF deliverables against {targetGeneration} DfE's information requirements",
                Version = $"{revision}.{now.Year}.{now.DayOfYear}",
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
            CreateCommonRequirements(ids, applicability, config.ProjectName, config.ProjectDescription, subContext);

            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcProject.Phase), subContext);
            CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcProject.Phase), ribaStagesDict.Values, subContext);
            CreateAttributeValueSpecification(specs, applicability, ids, nameof(IIfcProject.Phase), config.ProjectPhase, subContext, "Project Should Have Phase Correct For Project Stage");
        }


        // 02
        private void CreateSiteSpecifications(SpecContext context, DfeConfig config)
        {
            using var subContext = context.BeginSubscope().AddTag("Site");
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Site", "IfcSite");
            CreateCommonRequirements(ids, applicability, config.SiteName, config.SiteDescription, subContext);
        }

        // 03
        private void CreateBuildingSpecifications(SpecContext context, DfeConfig config)
        {
            using var subContext = context.BeginSubscope().AddTag("Building"); ;
            var group = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Building", "IfcBuilding");
            CreateCommonRequirements(ids, applicability, config.BuildingName, config.BuildingDescription, subContext);
            CreateClassificationPatternSpecification(group, applicability, ids, uniclassExpression.ToString(), "En.*", subContext);
            CreateClassificationCodeValueSpecification(group, applicability, ids, "Uniclass En", ValueConstraint.CreatePattern(uniclassExpression.ToString()), config.BuildingCategory, subContext);
            // If testing the value
            // CreatePropertyWithValueSpecification(group, entity, ids, "BlockConstructionType", "Additional_Pset_BuildingCommon", config.BuildingBlockConstructionType, subContext);
            CreatePropertyDefinedSpecification(group, applicability, ids, "BlockConstructionType", "Additional_Pset_BuildingCommon", subContext);
            CreatePropertyDefinedSpecification(group, applicability, ids, "MaximumBlockHeight", "Additional_Pset_BuildingCommon", subContext);
            CreatePropertyDefinedSpecification(group, applicability, ids, "NumberOfStoreys", "Pset_BuildingCommon", subContext);
            CreatePropertyDefinedSpecification(group, applicability, ids, "UPRN", "COBie_BuildingCommon_UK", subContext);
            CreatePropertyWithValueSpecification(group, applicability, ids, "UPRN", "COBie_BuildingCommon_UK", config.BuildingUPRN, subContext, "IFCTEXT");
        }

        // 04
        private void CreateBuildingStoreySpecifications(SpecContext context, DfeConfig config)
        {
            using var subContext = context.BeginSubscope().AddTag("BuildingStorey");
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Building Storey", "IfcBuildingStorey");
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.GlobalId), subContext);

            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Name), subContext);
            var floors = floorDict.Values.Where(n => n.Name != null);
            CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Name), floors.Select(f => f.Name)!, subContext);
            subContext.Skip("Unique Storey Name");
            // TODO: Building Storey Should Have Unique Name
            // TODO: Should corelate Floor Descr/Category to Floor Name
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Description), subContext);
            CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Description), floors.Select(f => f.Description), subContext);

            // Building Storey Should Have Category(COBie Floor Classification) Matching The Projects Information Standard - Which classification?

            CreateClassificationFromListSpecification(specs, applicability, ids, "COBie Floor Classification", ValueConstraint.CreatePattern(".*Floor.*"), new string[] { "Site", "Floor", "Roof" }, subContext);

            // Building Storey Should Have Elevation Matching The Projects Information Standard
            var elevations = Enumerable.Range(0, config.NumberOfStoreys).Select(i => $@"{{{{IfcBuildingStorey.Level {i:00}.Elevation}}}}");
            CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Elevation), elevations, subContext, NetTypeName.Double);

            // TODO: Review with BenS what his version intends (NetHeight)
            CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "NetHeight", "Additional_Pset_BuildingStoreyCommon", subContext, 0, false, null, false, "IfcLengthMeasure");
            
        }

        // 05
        private void CreateSpaceSpecifications(SpecContext context)
        {
            using var subContext = context.BeginSubscope().AddTag("Space"); ;
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Space", "IfcSpace");

            // Space Should Have GlobalId(COBie ExtIdentifier) Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcSpace.GlobalId), subContext);
            // Space Should Have Name Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcSpace.Name), subContext);
            // Space Should Have Name Matching Format Set Out In The Projects Information Standard
            CreateAttributePatternSpecification(specs, applicability, ids, nameof(IIfcSpace.Name), spaceNameExpression.ToString(), subContext);
            // TODO: Space Should Have Name That Is Unique
            subContext.Skip("05.04: Unique name not supported");
            // TODO: Space Should Have Name Related Correctly To Each Floor
            // TODO: Sept See examples
            subContext.Skip("05.05: Name related to Floor TBC");
            //CreateSpaceNameSpecifications(specs, applicability, subContext);
            // Space Should Have Description Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcSpace.Description), subContext);

            // Space Should Have RoomTag(Final Room Signage) Of 'n/a'
            var original = subContext.ApplicableToStages;
            CreatePropertyWithValueSpecification(specs, applicability, ids, "Roomtag", "COBie_Space", "n/a", subContext.SetApplicableStages(RibaStages.Stage3 | RibaStages.Stage4), "IFCTEXT");
            // TODO: Space Should Have RoomTag(Final Room Signage) Of Final Agreed Signage
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Roomtag", "COBie_Space", subContext.SetApplicableStages(RibaStages.Stage5Plus));
            subContext.SetApplicableStages(original);   // reset default

            // Space Should Have Category(DfE ADS Classification) Defined
            var adsSystemConstraint = ValueConstraint.CreatePattern(adsNameExpression.ToString());
            CreateClassificationDefinedSpecification(specs, applicability, ids, "ADS Classification", adsSystemConstraint, subContext);
            // Space Should Have Category(DfE ADS Classification) From Value List
            CreateClassificationFromListSpecification(specs, applicability, ids, "ADS Classification", adsSystemConstraint, GetADSCodes(), subContext);

            // Space Should Have Height Defined
            CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "Height", "BaseQuantities", subContext, 0, false, null, false, "IFCLENGTHMEASURE");
            // Space Should Have GrossArea Defined
            CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "GrossFloorArea", "BaseQuantities", subContext, 0, false, null, false, "IFCAREAMEASURE");
            // Space Should Have NetArea Defined
            CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "NetFloorArea", "BaseQuantities", subContext, 0, false, null, false, "IFCAREAMEASURE");


            // Space Should Have UniclassClassification From Agreed Value List
            CreateClassificationFromListSpecification(specs, applicability, ids, "Uniclass 2015", ValueConstraint.CreatePattern(uniclassExpression.ToString()), GetUniclassSLCodes(), subContext);

            // Space Should Have UniclassClassification That Corresponds Correctly To The Category(DfE ADS Classification)
            // Creates 100+ specs due to permutations
            CreateADSToUniclassSpecifications(subContext);

            // todo: 
            //CreatePartOfSpecification(specs, applicability, ids, PartOfFacet.PartOfRelation.IfcRelAssignsToGroup, "IfcZone", subContext.SetName("ZoneRequired"));

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
                var adsMap = GetUniclassADSMap();
                const int trimAt = 3;
                foreach (var item in adsMap)
                {
                    var label = String.Join(", ", item.Value.Take(trimAt));
                    if(item.Value.Count() > trimAt)
                    {
                        label += $",+{item.Value.Count()- trimAt} more";
                    }
                    var name = $"Spaces with ADS '{label}'";
                    adsScope.SetName(item.Key);

                    var applicab = GetEntityApplicabilityWithClassifications(ids, name, "IfcSpace", ".*ADS.*", item.Value, false);

                    CreateClassificationCodeValueSpecification(specs, applicab, ids, "Uniclass", ValueConstraint.CreatePattern(uniclassExpression.ToString()), item.Key, adsScope);
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
                .AddTag("Zone")
                .SetMatches(CardinalityEnum.Optional);
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Zone", "IfcZone");
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcZone.GlobalId), subContext);

            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcZone.Name), subContext);
            CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcZone.Name), GetZoneCodes(), subContext);

            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcZone.Description), subContext);
            CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcZone.Description), GetZoneDescriptions(), subContext);

            var zoneClassification = ValueConstraint.CreatePattern(".*Zone.*");
            CreateClassificationDefinedSpecification(specs, applicability, ids, "Category", zoneClassification, subContext);
            CreateClassificationFromListSpecification(specs, applicability, ids, "Category", zoneClassification, GetZoneCategories(), subContext);
            subContext.Skip("Can't checks spaces allocated to single zone");
            // TODO: Zone Should Have Each Spaces Allocated To A Single Zone.
            // Could test from Space. All spaces must be Part of a Zone (Partof: RelAssignToProup)
            CreatePartOfSpecification(specs, applicability, ids, PartOfFacet.PartOfRelation.IfcRelAssignsToGroup, "IfcSpace", subContext);

        }

        // 07
        private void CreateObjectTypeSpecifications(SpecContext context)
        {
            using var subContext = context.BeginSubscope()
                .AddTag("Type");
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            //var applicability = GetEntityApplicability(ids, "Object Type", "IfcTypeObject");
            var applicability = GetEntityApplicability(ids, "Object Type", RootTypes);



            // TODO: Object Type Should Have Entity Correctly Defined
            // ?? Check that instances are of correct type. Not possible in IDS1? E.g. Is a WallType only defining IfcWall[StandardCase]s
            subContext.Skip("07.01 Entity Correctly defined");
            // Object Type Should Have Enumeration(PredefinedType) Defined
            var pdtTypes = Schema.GetAttributeClasses("PredefinedType")
                    .Where(c => c.EndsWith("TYPE"))
                .Where(c=> !c.StartsWith("IFCSPACE")).ToArray();
            var pdtApplicablity = GetEntityApplicability(ids, "Object Type", pdtTypes);
            CreateAttributeDefinedSpecification(specs, pdtApplicablity, ids, nameof(IIfcWallType.PredefinedType), subContext.SetMatches(CardinalityEnum.Optional));
            // Object Type Should Have Enumeration(PredefinedType) That Is Not NOTDEFINED
            // TODO: see above re DoorStyle etc
            CreateAttributeValueSpecification(specs, pdtApplicablity, ids, "PredefinedType", "NOTDEFINED", subContext.SetRule(Cardinality.Prohibited));
            subContext
                .ResetRule()
                .ResetMatches();
            // Object Type Should Have Name Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcTypeObject.Name), subContext);

            // Object Type Should Have Name Matching The Projects Information Standard
            // This creates hundreds of Specs as per 5.2.7/5.2.8 of the PIS as we need to test per IFC Type
            CreateObjectTypeNamingSpecifications(subContext);

            // TODO: Object Type Should Have Name That Is Unique
            subContext.Skip("07.06 Unique support not in IDS");
            // Object Type Should Have Description Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcTypeObject.Description), subContext);
            // Object Type Should Have Uniclass Pr Products Classification(COBie Category) Defined
            CreateClassificationPatternSpecification(specs, applicability, ids, uniclassExpression.ToString(), "Pr_.*", subContext);

            //  !! Applicable to COBie Types only here on!!
            applicability = GetEntityApplicability(ids, "COBie Object Type", DomainExtensions.CobieTypes);

            // Object Type Should Have AssetType Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "AssetType", "COBie_Asset", subContext);
            // Object Type Should Have AssetType That Is 'Fixed' or 'Movable'
            CreatePropertyFromListSpecification(specs, applicability, ids, "AssetType", "COBie_Asset", new string[] { "Fixed", "Movable" }, subContext, "IFCTEXT");
            // Object Type Should Have Manufacturer That Is Defined
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Manufacturer", "Pset_ManufacturerTypeInformation", subContext);
            // Object Type Should Have Manufacturer That Is 'n/a' Or An Email Address
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "Manufacturer", "Pset_ManufacturerTypeInformation", emailOrNaExpression.ToString(), "n/a or Email Address", subContext, "IFCLABEL");
            // Object Type Should Have Manufacturer That Is An Email Address
            // TODO: Verify Stage as this is redundant with previous spec
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "Manufacturer", "Pset_ManufacturerTypeInformation", emailExpression.ToString(), "Email Address", subContext, "IFCLABEL");

            // Object Type Should Have WarrantyGuarantorParts That Is Defined
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
            CreatePropertyDefinedSpecification(specs, applicability, ids, "WarrantyGuarantorParts", "COBie_Warranty", subContext);
            // Object Type Should Have WarrantyGuarantorParts That Is 'n/a' Or An Email Address
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyGuarantorParts", "COBie_Warranty", emailOrNaExpression.ToString(), "n/a or Email Address", subContext, "IFCTEXT");
            // Object Type Should Have WarrantyGuarantorParts That Is An Email Address
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyGuarantorParts", "COBie_Warranty", emailExpression.ToString(), "Email Address", subContext, "IFCTEXT");

            // Object Type Should Have WarrantyDurationParts That Is Defined
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
            CreatePropertyDefinedSpecification(specs, applicability, ids, "WarrantyDurationParts", "COBie_Warranty", subContext);
            // Object Type Should Have WarrantyDurationParts That Is '0.0' Or Is A Valid Duration
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyDurationParts", "COBie_Warranty", numericExpression.ToString(),"Valid duration", subContext, "IFCTEXT");
            // TODO: Object Type Should Have WarrantyDurationParts That Is An Email Address /// > 0
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "WarrantyDurationParts", "COBie_Warranty", subContext, 0, minInclusive: false, null, default, "IFCTEXT");

            // Object Type Should Have WarrantyGuarantorLabor That Is Defined
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
            CreatePropertyDefinedSpecification(specs, applicability, ids, "WarrantyGuarantorLabor", "COBie_Warranty", subContext);
            // Object Type Should Have WarrantyGuarantorLabor That Is 'n/a' Or An Email Address
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyGuarantorLabor", "COBie_Warranty", emailOrNaExpression.ToString(), "n/a or Email Address", subContext, "IFCTEXT");
            // Object Type Should Have WarrantyGuarantorLabor That Is An Email Address
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyGuarantorLabor", "COBie_Warranty", emailExpression.ToString(), "Email Address", subContext, "IFCTEXT");


            // Object Type Should Have WarrantyDurationLabor That Is Defined
            subContext.SetApplicableStages(RibaStages.Stage4Plus);
            CreatePropertyDefinedSpecification(specs, applicability, ids, "WarrantyDurationLabor", "COBie_Warranty", subContext);
            // Object Type Should Have WarrantyDurationLabor That Is '0.0' Or Is A Valid Duration
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyDurationLabor", "COBie_Warranty", numericExpression.ToString(), "Valid duration", subContext, "IFCTEXT");
            // TODO: Object Type Should Have WarrantyDurationLabor That Is Email Address
            subContext.SetApplicableStages(RibaStages.Stage5Plus);
            CreatePropertyWithValueInRangeSpecification(specs, applicability, ids, "WarrantyDurationLabor", "COBie_Warranty", subContext, 0, minInclusive: false, null, default, "IFCTEXT");

            subContext.SetApplicableStages(RibaStages.Stage4Plus); // For remainder of specs

            // Object Type Should Have ReplacementCost That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "ReplacementCost", "COBie_EconomicImpactValues", subContext);
            // Object Type Should Have ReplacementCost That Is 'n/a' Or Is A Replacement Cost For The Product Type
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "ReplacementCost", "COBie_EconomicImpactValues", monetaryOrNaExpression.ToString(), "Replacement Cost", subContext, "IFCTEXT");

            // Object Type Should Have ExpectedLife That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "ExpectedLife", "COBie_ServiceLife", subContext);
            // Object Type Should Have ExpectedLife That Is 'n/a' Or Is A Valid Expected Life For The Product Type
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "ExpectedLife", "COBie_ServiceLife", numericOrNaExpression.ToString(), "Expected Life", subContext, "IFCTEXT");
            // Object Type Should Have WarrantyDescription That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "WarrantyDescription", "COBie_Warranty", subContext);
            // Object Type Should Have WarrantyDescription That Is 'n/a' Or A Description Of The Warranty For The Product Type
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyDescription", "COBie_Warranty", textOrNaExpression.ToString(), "Warranty", subContext, "IFCTEXT");
            // Object Type Should Have NominalLength That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "NominalLength", "COBie_Specification", subContext);
            // Object Type Should Have NominalWidth That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "NominalWidth", "COBie_Specification", subContext);
            // Object Type Should Have NominalHeight That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "NominalHeight", "COBie_Specification", subContext);
            // Object Type Should Have ModelReference That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "ModelReference", "Pset_ManufacturerTypeInformation", subContext);
            // Object Type Should Have Shape That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Shape", "COBie_Specification", subContext);
            // Object Type Should Have Size That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Size", "COBie_Specification", subContext);
            // Object Type Should Have Color That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Color", "COBie_Specification", subContext);
            // Object Type Should Have Finish That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Finish", "COBie_Specification", subContext);
            // Object Type Should Have Grade That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Grade", "COBie_Specification", subContext);
            // Object Type Should Have Material That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Material", "COBie_Specification", subContext);
            // Object Type Should Have Constituents That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Constituents", "COBie_Specification", subContext);
            // Object Type Should Have Features That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Features", "COBie_Specification", subContext);
            // Object Type Should Have AccessibilityPerformance That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "AccessibilityPerformance", "COBie_Specification", subContext);
            // Object Type Should Have CodePerformance That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "CodePerformance", "COBie_Specification", subContext);
            // Object Type Should Have SustainabilityPerformance That Is Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "SustainabilityPerformance", "COBie_Specification", subContext);
        }

        // 08
        private void CreateObjectOccurrenceSpecifications(SpecContext context)
        {
            using var subContext = context.BeginSubscope()
                .AddTag("Object");
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;

            var applicability = GetEntityApplicability(ids, "Object Occurrence (COBie Component)", DomainExtensions.CobieComponents);

            // Object Occurrence(COBie Component) Should Have Name Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcTypeObject.Name), subContext);

            // Object Occurrence(COBie Component) Should Have Name Matching The Projects Information Standard
            // This creates hundreds of Specs as per 5.2.8 of the PIS as we need to test per IFC Type
            CreateObjectOccurrenceNamingSpecifications(subContext);

            // TODO: Object Occurrence(COBie Component) Should Have Unique Name
            subContext.Skip("08.03 Unique support not in IDS");

            // Object Occurrence(COBie Component) Should Have Description Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcProduct.Description), subContext);
            // Object Occurrence(COBie Component) Should Have SerialNumber That Is 'n/a' Or Valid SerialNumber
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "SerialNumber", "Pset_ManufacturerOccurrence", numberOrNaExpression.ToString(), "Serial number", subContext);
            // Object Occurrence(COBie Component) Should Have InstallationDate That Is '1900-12-31T23:59:59' Or Actual InstallationDate
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "InstallationDate", "COBie_Component", dateOrDefaultExpression.ToString(), "Valid date", subContext);
            // Object Occurrence(COBie Component) Should Have WarrantyStartDate That Is '1900-12-31T23:59:59' Or Actual WarrantyStartDate
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "WarrantyStartDate", "COBie_Component", dateOrDefaultExpression.ToString(), "Valid date", subContext);
            // Object Occurrence(COBie Component) Should Have TagNumber That Is 'n/a'
            CreatePropertyWithValueSpecification(specs, applicability, ids, "TagNumber", "COBie_Component", "n/a", subContext);
            // Object Occurrence(COBie Component) Should Have BarCode That Is 'n/a' Or Actual BarCode
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "BarCode", "Pset_ManufacturerOccurrence", numberOrNaExpression.ToString(), "Bar code", subContext);
            // Object Occurrence(COBie Component) Should Have AssetIdentifier That Is 'n/a'
            CreatePropertyWithValueSpecification(specs, applicability, ids, "AssetIdentifier", "COBie_Component", "n/a", subContext);

            // Any Door Should Have FireRating That Is Defined
            subContext.SetMatches(CardinalityEnum.Optional);
            var doorApplicability = GetEntityApplicability(ids, "Door", "IfcDoor");
            CreatePropertyDefinedSpecification(specs, doorApplicability, ids, "FireRating", "Pset_DoorCommon", subContext);
            // Door Should Have FireRating That Is From PickList Provided In The Projects Information Standard
            CreatePropertyFromListSpecification(specs, doorApplicability, ids, "FireRating", "Pset_DoorCommon", new[] { "Undefined", "n/a", "20", "30", "60", "90", "120" }, subContext);
            subContext.ResetMatches();
            // TODO: Object Occurrences Must Not Contain Duplicate Entities
            subContext.Skip("08:13: Duplicates not supported");
            // TODO: Object Occurrence Should Have Layer Correctly Defined
            // IfcPresentationLayerAssignment relates to Representations - so not directly linked to Occurrences
            subContext.Skip("08:14: PresentationLayers need further info");

        }

        // 09
        private void CreateSystemSpecifications(SpecContext context)
        {
            using var subContext = context.BeginSubscope().AddTag("System");
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;
            var applicability = GetEntityApplicability(ids, "Ifc System", "IfcSystem", false);
            // See PIS 5.2.9 & table
            // TODO: System Should Have Name Matching The Projects Information Standard (Name or SystemName Prop)
            subContext.Skip("Requires IFC4 to make use of IfcDistributionSystem PDT");
            // TODO: System Should Have Description Matching The Projects Information Standard (Description or SystemDescription Prop)
            subContext.Skip("Requires IFC4 to make use of IfcDistributionSystem PDT");
            // TODO: System Should Have Category(Uniclass Ss Systems) Matching The Projects Information Standard (Classification or SystemCategory Prop)
            // TODO: Update to match 5.2.9 Catgorys
            CreateClassificationPatternSpecification(specs, applicability, ids, uniclassExpression.ToString(), "Ss_.*", subContext);
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
                .SetMatches(CardinalityEnum.Optional);
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
                            var pattern = $"{entityName}_(([A-Z][a-z]+)+_)Type\\d{{2,4}}";
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
                            var pattern = $"{entityName}_{enumerationName}_Type\\d{{2,4}}";
                            CreateAttributePatternSpecification(specs, applicability, ids, "Name", pattern, pdtContext.SetName(pdt));
                        }

                    }

                }
                else
                {
                    // We don't have a PDT, or it's not applicable to enumerate (e.g. Doors, Furniture)
                    var applicability = GetEntityApplicability(ids, $"{objectLabel}", ifcType.Name, includeSubTypes: false);
                    // EntityName_<OptionalEnumField>_TypeNN
                    var pattern = $"{entityName}_(([A-Z][a-z]+)+_)?Type\\d{{2,4}}";
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

            // Start new context as we build at least one spec per applicable type as part of a single Rule. e.g. 5.7.BeamType
            using var subContext = context.BeginSubscope()
                .SetApplicableToGeneration(GenerationPass.Complex)
                .SetMatches(CardinalityEnum.Optional);
            var specs = subContext.CurrentSpecGroup;
            var ids = subContext.Ids;

            IEnumerable<IdsLib.IfcSchema.ClassInfo> roots;
            if(UseIfc4TypesIn2x3)
            {
                var schema = new HybridSchemaIfc2x3();
                roots = schema.Where(e => baseTypes.Contains(e.Name));
            }
            else
            {
                roots = Schema.Where(e => baseTypes.Contains(e.Name));
            }
            var ifcTypes = roots.SelectMany(r => r.MatchingConcreteClasses).Distinct();

            foreach (var ifcType in ifcTypes)
            {
                // remove 'Ifc' prefix for labeling
                var typeName = ifcType.Name.Substring(3);
                var entityName = typeName;

                if (typeCodeDict.TryGetValue(typeName, out TypeMap? typeCode))
                {
                    const string suffix = "\\d{2,5}";

                    if (!typeCode.HasOverides)
                    {
                        // Simple case - apply to whole IFC class
                        using var typeContext = subContext.BeginSubscope(typeName);
                        var applicability = GetEntityApplicability(ids, $"{objectLabel}", ifcType.Name, includeSubTypes: false);

                        var code = typeCode.GetCode();
                        var pattern = "";
                        if (typeCode.UsesSpaceNaming)
                        {
                            pattern = @$"{spaceNameRegex}-{code}\d{{2,3}}";
                        }
                        else
                        {
                            pattern = $"{code}-{suffix}";
                        }


                        CreateAttributePatternSpecification(specs, applicability, ids, "Name", pattern, subContext.SetName(typeName));
                    }
                    else
                    {
                        // One spec per PDT
                        // Propogate PDTs from Type., E.g Vibration Isolators are DiscreteAccessories in IFC2x3 defined by VibrationIsolatorType
                        var definingTypes = ifcType.RelationTypeClasses!;// !.Where(r => r.StartsWith(ifcType.Name, StringComparison.OrdinalIgnoreCase));
                        var pdts = definingTypes.Select(t => Schema[t]).Where(c => c is not null).SelectMany(c => c!.PredefinedTypeValues);
                        //var pdts = ifcType.PredefinedTypeValues.Union(typePdts);

                        foreach (var pdt in pdts)
                        {
                            using var pdtContext = subContext.BeginSubscope(typeName);
                            if (pdt == "NOTDEFINED")
                            {
                                // TODO: Should check this - still possible for an untyped Occurrence to have Notdefined PDT and thereby avoid name checks
                                continue;
                            }

                            var enumerationName = dfeDict.ContainsKey(pdt) ? dfeDict[pdt] : pdt;
                            var applicability = GetEntityApplicabilityWithPredefinedType(ids, $"{objectLabel}", ifcType.Name, pdt, includeSubTypes: false);

                            var code = typeCode.GetCode(pdt);

                            var pattern = $"{code}-{suffix}";
                            CreateAttributePatternSpecification(specs, applicability, ids, "Name", pattern, pdtContext.SetName(pdt));
                        }
                        if(!pdts.Any())
                        {
                            Console.WriteLine($"WARNING: Type {ifcType.Name} has no predefined Types");
                        }

                    }

                }

            }
        }

        private static IEnumerable<string> GetADSCodes()
        {
            return File.ReadAllLines(@"Dfe\Content\ADS_Codes.txt")
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(":")[0]);
        }

        /// <summary>
        /// Groups ADS classification codes by their appropriate Uniclass classification
        /// </summary>
        /// <returns></returns>
        public static IDictionary<string, IEnumerable<string>> GetUniclassADSMap()
        {
            var res = File.ReadAllLines(@"Dfe\Content\ADS_Codes.txt")
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(":"))
                .Select(r => new { Uniclass = r[2], ADS = r[0] })
                .GroupBy(r=> r.Uniclass)
                .ToDictionary(g => g.Key.Trim(), g => g.ToList().Select(v => v.ADS.Trim()))
                ;

            return res;
        }

        private static IEnumerable<string> GetZoneCodes()
        {
            return File.ReadAllLines(@"Dfe\Content\Dfe_Zones.txt")
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(":")[0]);
        }

        private static IEnumerable<string> GetZoneCategories()
        {
            return File.ReadAllLines(@"Dfe\Content\Dfe_Zones.txt")
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(":")[1])
                .Distinct();
        }

        private static IEnumerable<string> GetZoneDescriptions()
        {
            return File.ReadAllLines(@"Dfe\Content\Dfe_Zones.txt")
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(":")[2]);
        }

        private static IEnumerable<string[]> GetZoneData()
        {
            return File.ReadAllLines(@"Dfe\Content\Dfe_Zones.txt")
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(":"));
        }

        private static IDictionary<string, string> GetDfeTypes()
        {
            var results = File.ReadAllLines(@"Dfe\Content\Dfe_Naming.txt")
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(":"))
                .Select(l => new
                {
                    IfcType = l[0],
                    Key = l[1],
                    ProperCase = l[2],
                    Description = l[3]
                })
                .DistinctBy(l => l.Key)
                .ToDictionary(v => v.Key.Trim(), v => v.ProperCase.Trim())
                ;
            return results;

        }

        


        /// <summary>
        /// Common requirements across Projects, Sites, Buildings etc
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="group"></param>
        /// <param name="entity"></param>
        /// <param name="name"></param>
        /// <param name="description"></param>
        /// <param name="context"></param>
        private void CreateCommonRequirements(Xids ids, FacetGroup entity, string name, string description, SpecContext context)
        {
            var group = context.CurrentSpecGroup;
            CreateAttributeDefinedSpecification(group, entity, ids, nameof(IIfcRoot.GlobalId), context);

            CreateAttributeDefinedSpecification(group, entity, ids, nameof(IIfcRoot.Name), context);
            CreateAttributeValueSpecification(group, entity, ids, nameof(IIfcRoot.Name), name, context, $"{entity.Name} Should Have Name Matching The Projects Information Standard");

            CreateAttributeDefinedSpecification(group, entity, ids, nameof(IIfcRoot.Description), context);
            CreateAttributeValueSpecification(group, entity, ids, nameof(IIfcRoot.Description), description, context, $"{entity.Name} Should Have Description Matching The Projects Information Standard");
        }


    }
}
