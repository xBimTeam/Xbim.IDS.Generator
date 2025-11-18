using IdsLib.IfcSchema;
using System.Text.RegularExpressions;
using Xbim.IDS.Generator.Common;
using Xbim.Ifc4.Interfaces;
using Xbim.InformationSpecifications;
using Xbim.InformationSpecifications.Cardinality;

namespace Xbim.IDS.Generator.Sample
{
    /// <summary>
    /// A demonstration IDS generator loosely based on rules for some UK public sector orgs.
    /// </summary>
    internal class SampleIdsGenerator : BaseGenerator
    {
        static readonly IDictionary<RibaStages, string> stageDescriptions = new Dictionary<RibaStages, string>()
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
            ["B1"] = new Floor("B1", "Level B1", "Basement level 1", "Floor"),
            ["00"] = new Floor("00", "Level 00", "Base level of building", "Floor"),
            ["M0"] = new Floor("M0", "Level M0", "Mezzanine above base level", "Floor"),
            ["01"] = new Floor("01", "Level 01", "First floor", "Floor"),
            ["02"] = new Floor("02", "Level 02", "Second floor", "Floor"),
            ["03"] = new Floor("03", "Level 03", "Third floor", "Floor"),
            ["RF"] = new Floor("RF", "Level RF", "Main roof", "Roof"),
            ["R2"] = new Floor("R2", "Level R2", "Additional roof above main roof", "Roof"),

        };

        internal const string spaceNameRegex = "((00|01|02|03|04|05|06|RF|R1|R2|R3|ZZ|M0|M1|B1|B2)-)?[0-9]+[A-Za-z]?";
        internal static readonly Regex spaceNameExpression = new($"^{spaceNameRegex}$");
        // Not perfect but likely good enough for our purposes: https://www.regular-expressions.info/email.html
        internal const string emailRegex = @"(^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$)";
        internal static readonly Regex emailOrNaExpression = new($"n/a|{emailRegex}");
        internal static readonly Regex emailExpression = new(emailRegex);
        internal static readonly Regex numericOrNaExpression = new($@"n/a|^\d+(\.\d+)?$");
        internal static readonly Regex textOrNaExpression = new($@"n/a|^(\w.*)+$");
        internal static readonly Regex numberOrNaExpression = new($@"n/a|^(\d|-| |_)+$");
        internal static readonly Regex dateOrDefaultExpression = new(@"1900-12-31T23:59:59|^20\d{2}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12][0-9]|3[01])(?:T(?:[01][0-9]|2[0-3]):(?:[0-5][0-9]):(?:[0-5][0-9])(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?)?$");


        // TODO: Should be smarter with difference schemas.
        /// <summary>
        /// The set of Types considered applicable to COBie Components. 
        /// </summary>
        private static readonly string[] CobieComponents = [
           "IfcBuildingElementProxy",
            //"IfcChimney",
            //"IfcCovering",
            "IfcDoor",
            "IfcShadingDevice",
            "IfcWindow",
            "IfcFlowMovingDevice",
            "IfcDistributionControlElement",
            "IfcDistributionChamberElement",
            "IfcEnergyConversionDevice",
            "IfcFlowController",
            "IfcFlowMovingDevice",
            "IfcFlowStorageDevice",
            "IfcFlowTerminal",
            "IfcFlowTreatmentDevice",
            "IfcDiscreteAccessory",
            "IfcTendon",
            "IfcFurnishingElement",
            "IfcTransportElement"
       ];

        /// <summary>
        /// Maps IFC Types to a naming shortcode, supporting exceptions by Predefined Type
        /// </summary>
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
            ["Door"] = new TypeMap("D").SpaceNaming(),
            ["DiscreteAccessory"] = new TypeMap("DCE"),
            ["ElectricAppliance"] = new TypeMap("EAP"),
            ["ElectricDistributionPoint"] = new TypeMap("EDP"),
            ["ElectricFlowStorage"] = new TypeMap("EFS"),
            ["ElectricGenerator"] = new TypeMap("EGN"),
            ["ElectricHeater"] = new TypeMap("EHT"),
            ["ElectricMotor"] = new TypeMap("EMT"),
            ["ElectricTimeControl"] = new TypeMap("ETC"),
            ["EvaporativeCooler"] = new TypeMap("ECL"),
            ["Evaporator"] = new TypeMap("EVP"),
            ["Fan"] = new TypeMap("FAN"),
            ["Filter"] = new TypeMap("FILT"),
            ["FireSuppressionTerminal"] = new TypeMap("FST"),
            ["FlowInstrument"] = new TypeMap("FIN"),
            ["Furniture"] = new TypeMap("FRN"),
            ["GasTerminal"] = new TypeMap("GTM"),
            ["Heat Exchanger"] = new TypeMap("HEX"),
            ["Humidifier"] = new TypeMap("HUM"),
            ["LightFixture"] = new TypeMap("LFT"),
            ["Motor Connection"] = new TypeMap("MCN"),
            ["Outlet"] = new TypeMap("OUT"),
            ["Protective Device"] = new TypeMap("PDV"),
            ["Pump"] = new TypeMap("PMP"),
            ["Sanitary Terminal"] = new TypeMap("SAN"),
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

        public override Task PublishIDS()
        {
            var newIds = new Xids();
            var header = new SpecificationsGroup(newIds)
            {
                Name = "IDS Tests",
                Author = "ids-editor@acme.com"
            };
            newIds.SpecificationsGroups.Add(header);
            

            using var myContext = new SpecContext(RibaStages.Stage3, newIds);


            var applicability = GetEntityApplicability(newIds, "Building", "IfcBuilding");

            // Building.Name should be "My Building Name"
            CreateAttributeValueSpecification(header, applicability, newIds, nameof(IIfcRoot.Name), "My Building Name", myContext);

            // The Pset_BuildingCommon.NumberOfStoreys property should be specified
            CreatePropertyDefinedSpecification(header, applicability, newIds, "NumberOfStoreys", "Pset_BuildingCommon", myContext);

            // Building should be classified with Uniclass with a valid EN value
            CreateClassificationFromListSpecification(header, applicability, newIds, "Uniclass 2015", 
                ValueConstraint.CreatePattern(".*Uniclass.*"), GetUniclassEnCodes(), myContext.SetApplicableStages(RibaStages.Stage3Plus));

            
            newIds.ExportBuildingSmartIDS("test.ids");


            ////

            SampleConfig config = new SampleConfig()
            {
                // In our example we're using templated configuration {token}s which can be replaced at runtime with project specific values
                // Alternately you could over-ride the values here:
                // ProjectName = "1234"
                // BuildingCategory = "En_20_20_40"
            };


            var stages = new[] { RibaStages.Stage3, RibaStages.Stage4, RibaStages.Stage5, RibaStages.Stage6 };
            // Create a ids customised per stage. Typically later stages include more stringent requirements 
            foreach (var targetStage in stages)
            {
                config.ProjectPhase = stageDescriptions[targetStage];

                // Create the IDS file
                var ids = new Xids
                {
                    Guid = Guid.NewGuid().ToString(),
                    Name = $"DfE EIR model checks for {config.ProjectName} at {targetStage}",
                    Stages = new List<string> { config.ProjectPhase },
                    SpecificationsGroups = new List<SpecificationsGroup>()
                };

                // Set the Header
                var now = DateTime.UtcNow;
                var specList = new SpecificationsGroup(ids)
                {
                    Date = DateTime.UtcNow,
                    Guid = Guid.NewGuid().ToString(),
                    Name = $"Sample EIR model checks for {config.ProjectName} at {targetStage}",
                    Specifications = new List<Specification>(),
                    Milestone = stageDescriptions[targetStage],
                    Author = "info@xbim.net",
                    Description = $"Verification of IFC deliverables according to the [Sample EIR] at {targetStage} of {config.ProjectName} project",
                    Version = $"0.{now.Year}.{now.DayOfYear}",
                    Purpose = "IFC-SPF model verification",
                    Copyright = "xbim Ltd",

                };
                ids.SpecificationsGroups.Add(specList);

                // A SpecContext helps organise / number specs (identifiers) and provides scoped based filtering
                // so sets of specs can be applied to certain stages only
                using var context = new SpecContext(targetStage, newIds)
                {
                    PrefixSpecNameWithId = false
                };
                context.SetApplicableStages(RibaStages.All);    // All specs apply to all stages

                CreateProjectSpecifications(ids, specList, config, context);
                CreateSiteSpecifications(ids, specList, config, context);
                CreateBuildingSpecifications(ids, specList, config, context);
                CreateBuildingStoreySpecifications(ids, specList, context);
                CreateSpaceSpecifications(ids, specList, context);
                //CreateZoneSpecifications(ids, specList, context);

                context.SetApplicableStages(RibaStages.Stage4Plus); // Following only apply to Stage 4+
                CreateObjectOccurrenceSpecifications(ids, specList, context);
                //CreateObjectTypeSpecifications(ids, specList, context);
                //CreateSystemSpecifications(ids, specList, context);

                var version = 1;
                var fileName = $"SAMPLE12-XBIM-XX-XX-T-X-{version:D4}-EIR-{targetStage}-Template.ids";
                ids.ExportBuildingSmartIDS(fileName);
                Console.WriteLine("Generated IDS for stage {0}: '{1}'", targetStage, fileName);
                
                // We could also export a single XIDS as a zip containing multiple IDS files if we created and saved XIDS outside the loop
            }
            
            return Task.CompletedTask;
        }
        private static void CreateProjectSpecifications(Xids ids, SpecificationsGroup specs, SampleConfig config, SpecContext context)
        {
            using var subContext = context.BeginSubscope();
            var applicability = GetEntityApplicability(ids, "Project", "IfcProject");
            CreateCommonRequirements(ids, specs, applicability, config.ProjectName, config.ProjectDescription, subContext);

            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcProject.Phase), subContext);
            CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcProject.Phase), stageDescriptions.Values, subContext);
            CreateAttributeValueSpecification(specs, applicability, ids, nameof(IIfcProject.Phase), config.ProjectPhase, subContext);

        }


        private static void CreateSiteSpecifications(Xids ids, SpecificationsGroup spec, SampleConfig config, SpecContext context)
        {
            using var subContext = context.BeginSubscope();
            var applicability = GetEntityApplicability(ids, "Site", "IfcSite");
            CreateCommonRequirements(ids, spec, applicability, config.SiteName, config.SiteDescription, subContext);
        }

        private static void CreateBuildingSpecifications(Xids ids, SpecificationsGroup group, SampleConfig config, SpecContext context)
        {
            using var subContext = context.BeginSubscope();
            var applicability = GetEntityApplicability(ids, "Building", "IfcBuilding");
            CreateCommonRequirements(ids, group, applicability, config.BuildingName, config.BuildingDescription, subContext);
            // Space Should Have UniclassClassification From Uniclass table
            CreateClassificationFromListSpecification(group, applicability, ids, "Uniclass 2015", ValueConstraint.CreatePattern(".*Uniclass.*"), GetCustomUniclassEnCodes(), subContext);
            CreateClassificationCodeValueSpecification(group, applicability, ids, "Uniclass 2015", ValueConstraint.CreatePattern(".*Uniclass.*"), config.BuildingCategory, subContext);
            CreatePropertyDefinedSpecification(group, applicability, ids, "NumberOfStoreys", "Pset_BuildingCommon", subContext);
        }

        protected static void ForFloor(FacetGroup applicability, Floor floor)
        {
            var floorNameConstraint = new AttributeFacet
            {
                AttributeName = "Name",
                AttributeValue = floor.Name!
            };

            applicability.Facets.Add(floorNameConstraint);
        }

        private static void CreateBuildingStoreySpecifications(Xids ids, SpecificationsGroup specs, SpecContext context)
        {
            using var subContext = context.BeginSubscope();
            var applicability = GetEntityApplicability(ids, "All Building Storeys", "IfcBuildingStorey");

            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.GlobalId), subContext);

            var floors = floorDict.Values.Where(n => n.Name != null);
            // Storey has a name and is from the approved naming conventions
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Name), subContext);
            CreateAttributeFromListSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Name), floors.Select(f => f.Name!), subContext);
            using (var floorContext = subContext.BeginSubscope())
            {
                foreach(var floor in floorDict.Values.Where(f => !string.IsNullOrEmpty(f.Name)))
                {
                    var floorApplicability = GetEntityApplicability(ids, $"Building Storey {floor.Code}", "IfcBuildingStorey"); 
                    ForFloor(floorApplicability, floor);
                    // Description meets conventions
                    CreateAttributeValueSpecification(specs, floorApplicability, ids, nameof(IIfcBuildingStorey.Description), floor.Description, floorContext);
                    // Classification meets conventions
                    CreateClassificationCodeValueSpecification(specs, floorApplicability, ids, "Floor Classificaton",
                        new ValueConstraint("COBie Floor Classification"), floor.Category, floorContext); 
                }
             }
            // All floors have Elevation
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcBuildingStorey.Elevation), subContext);
            // All floors have Net Height
            CreatePropertyDefinedSpecification(specs, applicability, ids, "NetHeight", "Qto_BuildingStoreyBaseQuantities", subContext);
        }

        private static void CreateSpaceSpecifications(Xids ids, SpecificationsGroup specs, SpecContext context)
        {
            using var subContext = context.BeginSubscope();
            var applicability = GetEntityApplicability(ids, "Space", "IfcSpace");
            var originalStages = subContext.ApplicableToStages;

            // Space Should Have GlobalId(COBie ExtIdentifier) Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcSpace.GlobalId), subContext);
            // Space Should Have Name Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcSpace.Name), subContext);
            // Space Should Have Name Matching Format Set Out In The Project Standards
            CreateAttributePatternSpecification(specs, applicability, ids, nameof(IIfcSpace.Name), spaceNameExpression.ToString(), subContext);
            
            // Space Should Have Description Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcSpace.Description), subContext);

            
            
            // Space Should Have Height Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Height", "Qto_SpaceBaseQuantities", subContext);
            // Space Should Have GrossArea Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "GrossFloorArea", "Qto_SpaceBaseQuantities", subContext);
            // Space Should Have NetArea Defined
            CreatePropertyDefinedSpecification(specs, applicability, ids, "NetFloorArea", "Qto_SpaceBaseQuantities", subContext);

            // Space Should Have RoomTag(Final Room Signage) Of Final Agreed Signage (Stage 4+)
            CreatePropertyDefinedSpecification(specs, applicability, ids, "Roomtag", "COBie_Space",
                subContext.SetApplicableStages(RibaStages.Stage4Plus));     // Stage 4+

            // Space Should Have UniclassClassification From Uniclass table
            CreateClassificationFromListSpecification(specs, applicability, ids, "Uniclass 2015", ValueConstraint.CreatePattern(".*Uniclass.*"), GetUniclassSLCodes(), 
                subContext);

        }


        private static void CreateObjectOccurrenceSpecifications(Xids ids, SpecificationsGroup specs, SpecContext context)
        {
            using var subContext = context.BeginSubscope().SetMatches(CardinalityEnum.Optional);

            var applicability = GetEntityApplicability(ids, "COBie Component", CobieComponents);

            // Object Occurrence(COBie Component) Should Have Name Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcTypeObject.Name), subContext);

            // Object Occurrence(COBie Component) Should Have Name Matching The project Standards
            // This creates hundreds of Specs as we generate at least one test per concrete IFC Type under IfcProduct
            CreateObjectOccurrenceNamingSpecifications(ids, specs, subContext);

            // Object Occurrence(COBie Component) Should Have Description Defined
            CreateAttributeDefinedSpecification(specs, applicability, ids, nameof(IIfcProduct.Description), subContext);

            // For Stage 5+
            var originalStage = subContext.ApplicableToStages;
            // Object Occurrence(COBie Component) Should Have SerialNumber That Is 'n/a' Or Valid SerialNumber
            CreatePropertyWithPatternSpecification(specs, applicability, ids, "SerialNumber", "Pset_ManufacturerOccurrence", numberOrNaExpression.ToString(), "Serial number", 
                subContext.SetApplicableStages(RibaStages.Stage5Plus));
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

            subContext.SetApplicableStages(originalStage);  // Reset stage

            // Door Should Have FireRating That Is Defined
            var doorApplicability = GetEntityApplicability(ids, "Door", "IfcDoor");
            CreatePropertyDefinedSpecification(specs, doorApplicability, ids, "FireRating", "Pset_DoorCommon", subContext);
            // Door Should Have FireRating That Is From PickList Provided In The Projects Information Standard
            CreatePropertyFromListSpecification(specs, doorApplicability, ids, "FireRating", "Pset_DoorCommon", new[] { "Undefined", "n/a", "20", "30", "60", "90", "120" }, subContext);


        }

        /// <summary>
        /// Ensures <see cref="CobieComponents"/> are named according to a convention
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="specs"></param>
        /// <param name="context"></param>
        private static void CreateObjectOccurrenceNamingSpecifications(Xids ids, SpecificationsGroup specs, SpecContext context)
        {
            var baseTypes = CobieComponents;
            var objectLabel = "CObieComponent";

            // Start new context as we build at least one spec per applicable type as part of a single Rule. e.g. 5.7.Door
            using var subContext = context.BeginSubscope();
            // TODO: Make scheme configurable
            var roots = SchemaInfo.SchemaIfc4.Where(e => baseTypes.Contains(e.Name));
            var ifcTypes = roots.SelectMany(r => r.MatchingConcreteClasses).Distinct();

            foreach (var ifcType in ifcTypes)
            {
                // remove 'Ifc' prefix
                var typeName = ifcType.Name.Substring(3);
                var entityName = typeName;

                if (typeCodeDict.ContainsKey(typeName))
                {
                    var typeCode = typeCodeDict[typeName];

                    const string suffix = "\\d{2,5}";

                    if (!typeCode.HasOverides)
                    {
                        // Simple case - apply to whole IFC class
                        using var typeContext = subContext.BeginSubscope(typeName);
                        var applicability = GetEntityApplicability(ids, $"{objectLabel}: {typeName}", ifcType.Name, includeSubTypes: false);

                        var code = typeCode.GetCode();
                        var namingConvention = typeCode.UsesSpaceNaming ?
                            @$"^{spaceNameRegex}-{code}\d{{2,3}}$" : 
                            @$"^{code}-{suffix}$";

                        CreateAttributePatternSpecification(specs, applicability, ids, "Name", namingConvention, subContext.SetName(typeName));
                    }
                    else
                    {
                        // Create a spec per PDT for the over-rides
                        foreach (var pdt in ifcType.PredefinedTypeValues)
                        {
                            using var pdtContext = subContext.BeginSubscope(typeName);
                            if (pdt == "NOTDEFINED")
                                continue;

                            var applicability = GetEntityApplicabilityWithPredefinedType(ids, $"{objectLabel}: {typeName} ({pdt})", ifcType.Name, pdt, includeSubTypes: false);
                            var code = typeCode.GetCode(pdt);

                            var namingConvention = $"^{code}-{suffix}$";
                            CreateAttributePatternSpecification(specs, applicability, ids, "Name", namingConvention, pdtContext.SetName(pdt));
                        }
                    }

                }

            }
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
        private static void CreateCommonRequirements(Xids ids, SpecificationsGroup group, FacetGroup entity, string name, string description, SpecContext context)
        {
            CreateAttributeDefinedSpecification(group, entity, ids, nameof(IIfcRoot.GlobalId), context);

            CreateAttributeDefinedSpecification(group, entity, ids, nameof(IIfcRoot.Name), context);
            CreateAttributeValueSpecification(group, entity, ids, nameof(IIfcRoot.Name), name, context);

            CreateAttributeDefinedSpecification(group, entity, ids, nameof(IIfcRoot.Description), context);
            CreateAttributeValueSpecification(group, entity, ids, nameof(IIfcRoot.Description), description, context);
        }

        
        /// <summary>
        /// Gets specific version of Uniclass EnCodes
        /// </summary>
        /// <returns></returns>
        private static IEnumerable<string> GetCustomUniclassEnCodes() => GetClassificationFile("Sample", "Uniclass_EN.txt", 0);

    }
}
