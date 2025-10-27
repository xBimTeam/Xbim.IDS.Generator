using IdsLib.IfcSchema;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Common.Step21;
using Xbim.IDS.Generator.Common;
using Xbim.IDS.Generator.Common.Internal;
using Xbim.Ifc;
using Xbim.Ifc.Fluent;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;
using Xbim.IO;

namespace Xbim.IDS.Generator.Dfe
{
 
    public partial class DfeGenerator : IModelGenerator
    {
        static readonly Guid BaseGuidStage3 = new Guid("F763341D-8498-4454-8DF8-092E39225175");

        static readonly Guid BaseGuidStage5 = new Guid("066916CA-0E25-49E5-B461-3E737BE3FF99");

        public GeometryData GeometryDefaults { get; set; }

        /// <summary>
        /// Generates Test Ifc Models based on DfE conventions
        /// </summary>
        /// <returns></returns>
        public Task GenerateTestModels()
        {
            var config = InitConfig();       // initialise project specific config / or tokens

            //GenerationSchema = XbimSchemaVersion.Ifc2X3;

            var targetStage = RibaStages.Stage3;
            config.ProjectPhase = ribaStagesDict[targetStage];
            var status = "S2";
            var revision = "P01";
            var version = 44;
            _typeSeqenceDict.Clear();

            var fileName = @$"DFE-ER\ER-DFE-XX-XX-M3-X-{version:D4}-Information Model {targetStage} Assurance-{status}-{revision}-Spatial.ifc";

            BuildSpatialModel(config, fileName);

            targetStage = RibaStages.Stage5;
            config.ProjectPhase = ribaStagesDict[targetStage];

            fileName = @$"DFE-ER\ER-DFE-XX-XX-M3-X-{version:D4}-Information Model {targetStage} Assurance-{status}-{revision}-MetaData.ifc";
            BuildTypeModel(config, fileName);

            return Task.CompletedTask;
        }

        private DfeConfig InitConfig()
        {
            return new DfeConfig
            {
                ProjectName = "Foxmere Primary School Redevelopment",
                ProjectDescription = "A new two-story primary school designed to accommodate 420 students, featuring modern learning spaces, sustainable construction, and enhanced accessibility.",
                SiteName = "Foxmere Academy Campus",
                SiteDescription = "A 2.5-hectare site in the outskirts of Newcastle upon Tyne, selected for its excellent transport links and green surroundings, providing an ideal environment for education.",
                BuildingName = "Foxmere Primary Block A",
                BuildingDescription = "The main teaching block housing 14 classrooms, a library, a multi-use hall, and staff facilities, designed to meet DfE sustainability standards.",
                BuildingCategory = "En_25_10_40",
                BuildingUPRN = "4510724634",

            };
        }

        private void BuildSpatialModel(DfeConfig config, string fileName)
        {
            var builder = new FluentModelBuilder();
            XbimEditorCredentials editor = GetEditor();
            builder.AssignEditor(editor)
                .UseStableGuids(BaseGuidStage3)
                .UseStableDateTime(new DateTime(2025, 9, 1));

            var res = builder
                .CreateModel(GenerationSchema)
                .SetHeaders()
                .SetOwnerHistory()
                .CreateEntities((factory, instanceBuilder) =>
                {
                    var project = CreateProject(factory, config);
                    GeometryDefaults = GetContext(factory, instanceBuilder.Model);
                    var site = CreateSite(factory, config, project);
                    var building = CreateBuilding(instanceBuilder, config, site);
                    var storeys = CreateBuildingStoreys(instanceBuilder, config, building, ["00", "M0", "01", "02", "RF", "BAD"]);

                    var spaces = CreateSpaces(instanceBuilder, storeys.Take(4), ["00", "01", "02a", "02b", "03", "B4D"]);
                    var roofSpaces = CreateSpaces(instanceBuilder, storeys.Skip(4), ["04", "05", "06"]);

                    IEnumerable<IIfcZone> zones = CreateZones(instanceBuilder, ["Basic teaching", "Learning resources", "Halls and dining", "Non-net", "Staff and admin"]);

                    int i = 0;
                    foreach(var sp in spaces.Union(roofSpaces))
                    {
                        var skip = i % 5;
                        sp.AddZone(instanceBuilder, zones.Skip(skip).First());
                        i++;
                    }

                    foreach (var space in spaces.Where(s => s.Decomposes.First()!.RelatingObject.Name!.ToString()!.Contains("00")))
                    {
                        CreateObject<IIfcDoor>(instanceBuilder, space, $"{space.Name}-D01", $"Door to {space.Description}", "")
                            .WithRepresentation(instanceBuilder, GeometryDefaults, 2100, 1000, 40)
                            .WithRelativePlacement(instanceBuilder, GeometryDefaults, space,
                                new XbimPoint3D(-2500, 1000, 0))
                            ;
                        CreateObject<IIfcProduct>("IfcWindow", instanceBuilder, space, $"{space.Name}-W01", $"Window to {space.Description}", "")
                            .WithRepresentation(instanceBuilder, GeometryDefaults, 1300, 1800, 40)
                            .WithRelativePlacement(instanceBuilder, GeometryDefaults, space,
                                new XbimPoint3D(-2500, -600, 800))
                            ;
                    }

                    // Now take the wrecking ball and create some invalid data for 04/05 test cases
                    var badStorey = storeys.First(s => s.Name == "Level 02");
                    BreakStorey(badStorey);

                    var badSpaces = badStorey.IsDecomposedBy.SelectMany(r => r.RelatedObjects.OfType<IIfcSpace>()).ToList();
                    BreakSpaces(badSpaces);

                    var badZones = zones.Skip(2).ToList();
                    BreakZones(badZones);
                })
                .AssertValid()
                .SaveAsIfc(fileName);
        }

        private void BuildTypeModel(DfeConfig config, string fileName)
        {
            var builder = new FluentModelBuilder();
            XbimEditorCredentials editor = GetEditor();
            builder.AssignEditor(editor)
                .UseStableGuids(BaseGuidStage5)
                .UseStableDateTime(new DateTime(2025, 9, 1));


            var res = builder
                .CreateModel(GenerationSchema)
                .SetHeaders()
                .SetOwnerHistory()
                .CreateEntities((factory, ctx) =>
                {
                    var project = CreateProject(factory, config);
                    GeometryDefaults = GetContext(factory, ctx.Model);
                    var site = CreateSite(factory, config, project);
                    var building = CreateBuilding(ctx, config, site);
                    var storeys = CreateBuildingStoreys(ctx, config, building, ["B1", "00"]);
                    var basement = storeys.First();
                    var firstFloor = storeys.Skip(1).First();
                    firstFloor.LongName = "Elements on this floor should pass an IDS audit";
                    basement.LongName = "Elements on this floor should fail the audit";
                    var lengthUnit = ctx.Model.Instances.OfType<IIfcSIUnit>().First(u => u.UnitType == IfcUnitEnum.LENGTHUNIT);
                    var areaUnit = ctx.Model.Instances.OfType<IIfcSIUnit>().First(u => u.UnitType == IfcUnitEnum.AREAUNIT);

                    IEnumerable<IIfcZone> zones = CreateZones(ctx, ["Basic teaching", "Non-net"]);

                    var teachingZone = zones.First();
                    var nonTeachingZone = zones.Skip(1).First();

                    var spaceTypeValid = CreateType<IIfcSpaceType>(ctx, "Space_ValidElements_Type01", "Spaces where valid elements reside", "07.05 Valid SpaceTypeName")
                        .WithDefaults(t => t with { PredefinedType = "Custom" });
                    var spaceTypeInvalid = CreateType<IIfcSpaceType>(ctx, "Space_Invalid_Elements_Type01", "Spaces where invalid elements reside", "07.05 Invalid SpaceType name")
                        .WithDefaults(t => t with { PredefinedType = "Custom" });


                    var goodTypeSpace = CreateSpace(ctx, firstFloor, "00-01G", "The Good Place (Types)", "Spaces where valid IFC Types can be tested", teachingZone)
                        .WithRepresentation(ctx, GeometryDefaults, 150, 10000, 20000)
                        .WithRelativePlacement(ctx, GeometryDefaults, firstFloor)
                        .WithClassificationReference("Uniclass SL", "SL_25_10_14", "Classrooms")
                        .WithClassificationReference("DFE ADS", "CLA12", "Classrooms (general)")
                        .AddSpaceQuants(lengthUnit, areaUnit);

                    var goodElementsSpace = CreateSpace(ctx, firstFloor, "00-02G", "The Good Place (Elements)", "Spaces where valid IFC Elements can be tested", teachingZone)
                        .WithRepresentation(ctx, GeometryDefaults, 150, 10000, 20000)
                        .WithRelativePlacement(ctx, GeometryDefaults, firstFloor, new XbimPoint3D(0, 11000, 0))
                        .WithClassificationReference("Uniclass SL", "SL_25_10_14", "Classrooms")
                        .WithClassificationReference("DFE ADS", "CLA12", "Classrooms (general)")
                        .AddSpaceQuants(lengthUnit, areaUnit);
                    goodTypeSpace.AddDefiningType(spaceTypeValid);
                    goodElementsSpace.AddDefiningType(spaceTypeValid);

                    // Spaces for failures
                    var badTypeSpace = CreateSpace(ctx, basement, "01-01B", "The Bad Place (Types)", "Spaces where invalid IFC Types can be tested", nonTeachingZone)
                        .WithRepresentation(ctx, GeometryDefaults, 150, 10000, 20000)
                        .WithRelativePlacement(ctx, GeometryDefaults, basement, new XbimPoint3D(0, 0, 0))
                        .WithClassificationReference("Uniclass SL", "SL_25_10_76", "Secondary special educational needs (SEN) classrooms")
                        .WithClassificationReference("DFE ADS", "CLA62", "Secondary SEN classrooms (AP behaviour)")
                        .AddSpaceQuants(lengthUnit, areaUnit);

                    var badElementsSpace = CreateSpace(ctx, basement, "01-02B", "The Bad Place (Elements)", "Spaces where invalid IFC Elements can be tested", nonTeachingZone)
                        .WithRepresentation(ctx, GeometryDefaults, 150, 10000, 20000)
                        .WithRelativePlacement(ctx, GeometryDefaults, basement, new XbimPoint3D(0, 11000, 0))
                        .WithClassificationReference("Uniclass SL", "SL_25_10_76", "Secondary special educational needs (SEN) classrooms")
                        .WithClassificationReference("DFE ADS", "CLA62", "Secondary SEN classrooms (AP behaviour)")
                        .AddSpaceQuants(lengthUnit, areaUnit);
                    badTypeSpace.AddDefiningType(spaceTypeInvalid);
                    badElementsSpace.AddDefiningType(spaceTypeInvalid);

                    var types = CreateTypedOccurrences(ctx, goodTypeSpace, badTypeSpace);

                    CreateObjectOccurrences(ctx, types, goodElementsSpace, badElementsSpace);
                    Sanity(ctx);
                })
                .AssertValid()
                //.ValidateIfc();
                .SaveAsIfc(fileName);
        }

        private static void BreakStorey(IIfcBuildingStorey storey)
        {
            storey.Description = "";
            storey.Name = "";
            var pset = storey.GetPropertySet("Additional_Pset_BuildingStoreyCommon");
            var psetProps = pset.HasProperties;
            var toDel = psetProps.First(p => p.Name == "NetHeight");
            psetProps.Remove(toDel);
            //storey.Model.Delete(pset);  // Tidy empty pset
        }

        private void BreakSpaces(IList<IIfcSpace> spaces)
        {
            IIfcSpace GetRoom(IEnumerator<IIfcSpace> enumerator)
            {
                if (!enumerator.MoveNext())
                {
                    enumerator.Reset();
                    enumerator.MoveNext();
                }
                return enumerator.Current;
            }
            var enumerator = spaces.GetEnumerator();

            GetRoom(enumerator).Name = "";

            GetRoom(enumerator).Description = "";

            // Remove a Roomtag from room
            var room = GetRoom(enumerator);
            var pset = room.GetPropertySet("COBie_Space");
            var psetProps = pset.HasProperties;
            var toDel = psetProps.First(p => p.Name == "Roomtag");
            psetProps.Remove(toDel);
            //room.Model.Delete(pset);// Tidy up empty pset

            // Add bogus Room tag to Room
            room = GetRoom(enumerator);
            psetProps = room.GetPropertySet("COBie_Space").HasProperties;
            var toUpdate = room.GetPropertySingleValue("COBie_Space", "Roomtag");
            toUpdate.NominalValue = new IfcText("INVALID");

            // Remove ADS classification from Room
            room = GetRoom(enumerator);
            var cls = room.HasAssociations.OfType<IIfcRelAssociatesClassification>().First(e => e.RelatingClassification is IIfcClassificationReference r && r.ReferencedSource.Name() == "DFE ADS");
            cls.RelatedObjects.Remove(room);

            // Replace ADS class with Bogus one on Room
            room = GetRoom(enumerator);
            cls = room.HasAssociations.OfType<IIfcRelAssociatesClassification>().First(e => e.RelatingClassification is IIfcClassificationReference r && r.ReferencedSource.Name() == "DFE ADS");
            cls.RelatedObjects.Remove(room);
            room.WithClassificationReference("DFE ADS", "BOGUS", "Not valid ADS");

            // Remove all Quantities from Room
            room = GetRoom(enumerator);
            var quants = room.GetElementQuantity("BaseQuantities");
            foreach(var q in quants.Quantities.ToArray())
            {
                quants.Quantities.Remove(q);
            }
            //room.Model.Delete(quants);  // Tidy up empty


            // Set all Quantities to zero on Room
            quants = GetRoom(enumerator).GetElementQuantity("BaseQuantities");
            foreach (var q in quants.Quantities.ToArray())
            {
                if(q is IIfcQuantityLength l)
                    l.LengthValue = 0;
                if (q is IIfcQuantityArea a)
                    a.AreaValue = 0;
            }

            // Remove Uniclass classification from Room
            room = GetRoom(enumerator);
            cls = room.HasAssociations.OfType<IIfcRelAssociatesClassification>().First(e => e.RelatingClassification is IIfcClassificationReference r && r.ReferencedSource.Name().StartsWith("Uniclass"));
            cls.RelatedObjects.Remove(room);

            // Replace Uniclass class with Bogus one on Room
            room = GetRoom(enumerator);
            cls = room.HasAssociations.OfType<IIfcRelAssociatesClassification>().First(e => e.RelatingClassification is IIfcClassificationReference r && r.ReferencedSource.Name().StartsWith("Uniclass"));
            cls.RelatedObjects.Remove(room);
            room.WithClassificationReference("Uniclass SL", "SL_00_00_00", "Not valid Uniclass");

            // Remove space from zone
            room = GetRoom(enumerator);
            var rel = room.HasAssignments.OfType<IIfcRelAssignsToGroup>().First(r => r.Name!.Value.ToString().Contains("Zone"));
            rel.RelatedObjects.Remove(room);

            // Mismatch ADS and Uniclass (note  other 05_15 will fail due to invalid/missing uniclass tests above)

            room = spaces[0];   // pick first space so stable
            cls = room.HasAssociations.OfType<IIfcRelAssociatesClassification>().First(e => e.RelatingClassification is IIfcClassificationReference r && r.ReferencedSource.Name().StartsWith("Uniclass"));
            cls.RelatedObjects.Remove(room);
            room.WithClassificationReference("Uniclass SL", "SL_42_40_30", "Valid Uniclass but not compatible with CLA11");

        }

        private void BreakZones(IList<IIfcZone> zones)
        {
            IIfcZone GetZone(IEnumerator<IIfcZone> enumerator)
            {
                if (!enumerator.MoveNext())
                {
                    enumerator.Reset();
                    enumerator.MoveNext();
                }
                return enumerator.Current;
            }
            var enumerator = zones.GetEnumerator();

            GetZone(enumerator).Name = "";
            GetZone(enumerator).Name = "Not a valid name";
            GetZone(enumerator).Description = "";
            GetZone(enumerator).Description = "Not a valid description";

            // Remove classification from Zone
            var zone = GetZone(enumerator);
            var cls = zone.HasAssociations.OfType<IIfcRelAssociatesClassification>().First(e => e.RelatingClassification is IIfcClassificationReference r && r.ReferencedSource.Name() == "Zones");
            cls.RelatedObjects.Remove(zone);
            if(cls.RelatedObjects.Any() == false)
            {
                zone.Model.Delete(cls);
            }

            // Replace classification code with invalid
            zone = GetZone(enumerator);
            cls = zone.HasAssociations.OfType<IIfcRelAssociatesClassification>().First(e => e.RelatingClassification is IIfcClassificationReference r && r.ReferencedSource.Name() == "Zones");
            cls.RelatedObjects.Remove(zone);
            zone.WithClassificationReference("Zones", "Invalid Category", "Not valid zone category");

        }


        private void CreateObjectOccurrences(IModelInstanceBuilder builder, IDictionary<IfcTypeHandle, IIfcTypeObject> typeMap, IIfcSpace passes, IIfcSpace fails)
        {
            // All a big hack to determine types for elements because we can't use SchemaClass.RelationTypeClasses https://github.com/buildingSMART/IDS-Audit-tool/issues/57
            var baseTypes = new string[] { "IfcBuildingElement",
                "IfcCivilElement",
                // Dist
                "IfcDistributionControlElement",
                "IfcDistributionFlowElement",
                "IfcElementAssembly",
                "IfcElementComponent",
                "IfcFurnishingElement",
                "IfcGeographicElement",
                "IfcTransportElement",

                "IfcOpeningElement"
                };

            var roots = Schema.Where(e => baseTypes.Contains(e.Name));
            var ifcProducts = roots.SelectMany(r => r.MatchingConcreteClasses).Distinct()
                .OrderBy(t => t.NameSpace).ThenBy(t => t.Name);

            var typeNo = 0;
            foreach (var elementType in ifcProducts)
            {
                if (elementType.Name == "IfcDistributionFlowElement" /*|| elementType.Name == "IfcDistributionControlElement"*/)
                    continue;   // Handled by child classes
                var candidateTypes = new List<string> { $"{elementType.Name.ToUpperInvariant()}TYPE", $"{elementType.Name.ToUpperInvariant()}STYLE" };
                if (elementType.Name.Contains("StandardCase"))
                {
                    candidateTypes.Add($"{elementType.Name.Replace("StandardCase", "").ToUpperInvariant()}TYPE");
                }
                // RelationTypeClasses is broken and doesn't include sub classes so we have to locate 'best' superclass and cross check the actual IFC schema
                var typeCandidates = elementType.RelationTypeClasses?.Where(r => candidateTypes.Any(t => t == r));

                if (elementType.Name == "IfcFlowTerminal")
                {
                    // add manually because SpaceHeaterType  was originally a EnergyConversionDeviceType in IFC2x3 becoming FlowTerminalType in Ifc4
                    typeCandidates = typeCandidates!.Append("IFCSPACEHEATERTYPE");
                }

                if (typeCandidates?.Any() == true)
                {
                    foreach (var definingType in typeCandidates)
                    {

                        var type = Schema[definingType];
                        if (type != null)
                        {
                            foreach (var concreteType in type.MatchingConcreteClasses)
                            {
                                //Console.WriteLine($"{elementType.Name} has Type {concreteType.Name}");

                                var logicalInstanceType = GetLogicalInstance(concreteType);
                                // Create an instance for each Type and PDT.
                                typeNo++;
                                var variant = 0;

                                if (concreteType.PredefinedTypeValues?.Any() == true)
                                {
                                    // One per PDT
                                    foreach (var pdt in concreteType.PredefinedTypeValues)
                                    {
                                        if (pdt == "NOTDEFINED")
                                            continue;
                                        if (typeMap.TryGetValue(new IfcTypeHandle(concreteType, pdt), out var typeObject))
                                        {
                                            //typeObject.Name.ToString()
                                            var objectName = BuildObjectName(logicalInstanceType, logicalInstanceType.Name.Replace("Ifc", ""), passes, out bool hasConventions, pdt);
                                            BuildObject(builder, elementType, typeNo, variant, passes, objectName, $"{typeObject.Name}", "08.02 Name matches PIS", showGhosted: !hasConventions)
                                                .AddDefiningType(typeObject);

                                            BuildObject(builder, elementType, typeNo, variant, fails, $"BAD-{objectName}", $"Badly named {typeObject.Name}", "08.02 Name invalid", showGhosted: !hasConventions)
                                                .AddDefiningType(typeObject);
                                        }
                                        else
                                        {
                                            Console.WriteLine($"No Family found for {concreteType.Name} {pdt}");
                                        }
                                        variant++;
                                    }
                                }
                                else
                                {
                                    if (typeMap.TryGetValue(new IfcTypeHandle(concreteType), out var typeObject))
                                    {
                                        var objectName = BuildObjectName(logicalInstanceType, elementType.Name, passes, out bool hasConventions);
                                        BuildObject(builder, elementType, typeNo, 0, passes, objectName, $"{typeObject.Name}", "08.02 Name matches PIS", showGhosted: !hasConventions)
                                            .AddDefiningType(typeObject);

                                        BuildObject(builder, elementType, typeNo, 0, fails, $"BAD-{objectName}", $"Badly named {typeObject.Name}", "08.02 Name invalid", showGhosted: !hasConventions)
                                            .AddDefiningType(typeObject);
                                    }
                                    else
                                    {
                                        Console.WriteLine($"No Family found for {concreteType.Name}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"! no type for {definingType}");
                        }
                    }
                }
                else
                {
                    //Console.WriteLine($"No Types for {elementType.Name} ({elementType.RelationTypeClasses?.Count()})");

                    // Not Typed - Just create instance without defining type, but set the Object PDT

                    if (elementType.PredefinedTypeValues.Any())
                    {
                        var variant = 0;
                        foreach (var pdt in elementType.PredefinedTypeValues)
                        {
                            if (pdt == "NOTDEFINED")
                                continue;
                            var objectName = BuildObjectName(elementType, elementType.Name, passes, out bool hasConventions, pdt);
                            var enumeration = pdt == "USERDEFINED" ? "Some Custom" : pdt;
                            BuildObject(builder, elementType, typeNo, variant, passes, objectName, $"{pdt} {elementType.Name}", "08.02 Name matches PIS", showGhosted: !hasConventions)
                                .WithDefaults(t => t with { PredefinedType = enumeration });

                            BuildObject(builder, elementType, typeNo, variant, fails, $"BAD-{objectName}", $"Badly named {pdt} {elementType.Name} instance", "08.02 Name invalid", showGhosted: !hasConventions)
                                .WithDefaults(t => t with { PredefinedType = enumeration });


                            variant++;
                        }
                    }
                    else
                    {
                        var objectName = BuildObjectName(elementType, elementType.Name, passes, out bool hasConventions);
                        BuildObject(builder, elementType, typeNo, 0, passes, objectName, elementType.Name, "08.02 Name matches PIS", showGhosted: !hasConventions);

                        BuildObject(builder, elementType, typeNo, 0, fails, $"BAD-{objectName}", $"Badly named {elementType.Name} instance", "08.02 Name invalid", showGhosted: !hasConventions);
                    }
                }

                typeNo++;
            }

            CreateBrokenObjects(builder, typeMap, fails);

        }

        private void CreateBrokenObjects(IModelInstanceBuilder builder, IDictionary<IfcTypeHandle, IIfcTypeObject> typeMap, IIfcSpace fails)
        {
            // Create broken Objects
            var typeNo = 0;

            var element = Schema["IfcDoor"];
            var doortype = GenerationSchema == XbimSchemaVersion.Ifc2X3 ? "IfcDoorStyle" : "IfcDoorType";
            var style = Schema[doortype];
            if (typeMap.TryGetValue(new IfcTypeHandle(style!), out var typeObject))
            {
                if (typeObject is null || typeObject.Name == null)
                    throw new InvalidOperationException($"Expected a valid type for IfcDoorStyle");
                var no = 0;



                BuildProduct<IIfcDoor>(builder, typeNo, no, fails, "", $"Blank Name", "08.01 Undefined Name", 1)
                    .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}")
                    .AddDfeFireRating("30")
                    .AddDefiningType(typeObject);

                BuildProduct<IIfcDoor>(builder, typeNo, ++no, fails, BuildName(), $"", "08.04 Undefined Description", 1)
                    .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}")
                    .AddDfeFireRating("30")
                    .AddDefiningType(typeObject);

                var objectName = BuildName();
                BuildProduct<IIfcDoor>(builder, typeNo, ++no, fails, objectName, $"Duplicate Name", "08.03 Unique Name (1)", 1)
                    .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}")
                    .AddDfeFireRating("30")
                    .AddDefiningType(typeObject);

                BuildProduct<IIfcDoor>(builder, typeNo, no, fails, objectName, $"Duplicate Name", "08.03 Unique Name (2)", 2)
                    .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}")
                    .AddDfeFireRating("30")
                    .AddDefiningType(typeObject);

                BuildProduct<IIfcDoor>(builder, typeNo, ++no, fails, BuildName(), $"Bad SerialNo", "08.05 Invalid SerialNo", 1)
                    .AddDfeCOBieObjectData($"BAD-{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}")
                    .AddDfeFireRating("30")
                    .AddDefiningType(typeObject);

                BuildProduct<IIfcDoor>(builder, typeNo, ++no, fails, BuildName(), $"Bad InstallDate", "08.06 Invalid Install Date", 1)
                    .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}", installationDate: "")
                    .AddDfeFireRating("30")
                    .AddDefiningType(typeObject);

                BuildProduct<IIfcDoor>(builder, typeNo, ++no, fails, BuildName(), $"Bad WarranteeDate (format)", "08.07 Invalid Warrantee Start Date", 1)
                    .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}", warranteeStartDate: "2021-01-01 12:00:00")
                    .AddDfeFireRating("30")
                    .AddDefiningType(typeObject);

                BuildProduct<IIfcDoor>(builder, typeNo, ++no, fails, BuildName(), $"Bad Tag", "08.08 Invalid Tag", 1)
                    .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}", tag: "")
                    .AddDfeFireRating("30")
                    .AddDefiningType(typeObject);

                BuildProduct<IIfcDoor>(builder, typeNo, ++no, fails, BuildName(), $"Bad Barcode", "08.09 Invalid BarCode", 1)
                    .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"BAD-{GetNextSequence("Barcode") + 65000:0000000}")
                    .AddDfeFireRating("30")
                    .AddDefiningType(typeObject);

                BuildProduct<IIfcDoor>(builder, typeNo, ++no, fails, BuildName(), $"Bad Asset Identifier", "08.10 Invalid Asset Identifier", 1)
                    .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}", assetIdentifier: "Bad")
                    .AddDfeFireRating("30")
                    .AddDefiningType(typeObject);

                BuildProduct<IIfcDoor>(builder, typeNo, ++no, fails, BuildName(), $"Bad FireRating", "08.11 Invalid Not Defined", 1)
                    .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}")
                    .AddDfeFireRating(null)
                    .AddDefiningType(typeObject);

                BuildProduct<IIfcDoor>(builder, typeNo, ++no, fails, BuildName(), $"Bad FireRating", "08.12 Invalid FireRating", 1)
                    .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}")
                    .AddDfeFireRating("BAD")
                    .AddDefiningType(typeObject);
            }

            string BuildName()
            {
                return BuildObjectName(element!, typeObject.Name.ToString(), fails);
            }
        }

        /// <summary>
        /// Create all permutations of <see cref="IIfcTypeObject"/> in the schema and all their Predefined Types, with a placed valid occurrence object to represent it visually
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="passedTypes"></param>
        /// <param name="failedTypes"></param>
        /// <returns></returns>
        private IDictionary<IfcTypeHandle, IIfcTypeObject> CreateTypedOccurrences(IModelInstanceBuilder builder, IIfcSpace passedTypes, IIfcSpace failedTypes)
        {
            var goodTypes = new Dictionary<IfcTypeHandle, IIfcTypeObject>();
            var dfeTypes = GetDfeTypes();
            var baseTypes = new string[] { "IfcTypeObject" };

            var roots = Schema.Where(e => baseTypes.Contains(e.Name));
            var ifcTypes = roots.SelectMany(r => r.MatchingConcreteClasses).Distinct()
                .OrderBy(t => t.NameSpace).ThenBy(t => t.Name);

            var typeNo = 0;
            var lastNameSpace = "";
            foreach (var ifcType in ifcTypes)
            {
                var logicalInstanceType = GetLogicalInstance(ifcType);  // Idealised instance - used for naming rules. e.g. IfcAirTerminal 
                var instanceType = MapSchemaInstance(logicalInstanceType, builder.Model);   // Best instance for schema. e.g. IfcFlowTerminal in IFC2x3
                if (ifcType.Type == ClassType.Abstract || instanceType.Type == ClassType.Abstract)
                {
                    Console.WriteLine($"Skipping abstract instance/type {ifcType.Name} / {instanceType.Name}");
                    continue;
                }
                // Skip a column between IFC domains
                if (lastNameSpace != ifcType.NameSpace)
                {
                    typeNo++;
                    lastNameSpace = ifcType.NameSpace;
                }
                int goodVariantNo = 0;
                int badVariantNo = 0;
                var typeIdentifier = "";

                // remove 'Ifc' prefix
                var typeName = ifcType.Name.Substring(3);
                // remove any 'Type' suffixes - Types follow same base rules as occurences
                var entityName = RemoveSuffix("Style", RemoveSuffix("Type", typeName));
                if (typeName.Contains("BuildingElementProxy"))   // BEP is special case for Field1 (EntityName)
                {
                    // Any proper-case entity name allowed for Proxies Types
                    entityName = "CustomEntity";
                }

                var isApplicable = enumTypeExceptions.Contains(typeName) == false;    // Item classed as applicable for PredefinedType
                if (isApplicable && ifcType.PredefinedTypeValues.Any())
                {

                    foreach (var pdt in ifcType.PredefinedTypeValues)
                    {
                        // Map PDT to DfE friendly name (PascalCased)
                        if (!dfeTypes.TryGetValue(pdt, out string? enumerationName))
                        {
                            Console.Error.WriteLine($"Missing Enum: {entityName} : {pdt}");
                            enumerationName = pdt;
                        }

                        if (pdt == "USERDEFINED")
                        {
                            // Any Proper-cased Enum will do
                            // EntityName_CustomEnumerationField_TypeNN
                            enumerationName = "SomeUserDefined";
                            typeIdentifier = $"{entityName}_{enumerationName}_Type{goodVariantNo:000}";
                        }
                        else
                        {
                            // PDTs must use the proper case PDT in field 2 of the name
                            // EntityName_EnumerationField_TypeNN
                            typeIdentifier = $"{entityName}_{enumerationName}_Type{goodVariantNo:00}";
                        }

                        // Not Defined PDT is not permited - Rule 07.03. There is no 'good' 07.05 PIS name for these so always fail
                        var isFailure = pdt == "NOTDEFINED";
                        var variant = isFailure ? badVariantNo : goodVariantNo;
                        var space = isFailure ? failedTypes : passedTypes;

                        var type = CreateType(ifcType.Name, builder, typeIdentifier, $"{enumerationName} {typeName}", isFailure ? "07.03 Not NOTDEFINED" : "07.05 Valid TypeName")
                            .WithDefaults(t => t with { PredefinedType = pdt == "USERDEFINED" ? enumerationName : pdt })
                            .AddDfeTypeData(IsCOBieType);

                        if (pdt != "USERDEFINED" && type.GetPredefinedTypeValue() == "USERDEFINED")
                        {
                            // Sanity check
                            Console.WriteLine($"Type failed to set PDT: {ifcType.Name} : {enumerationName}");
                        }

                        if (isFailure)
                        {
                            badVariantNo++;
                        }
                        else
                        {
                            // Store for re-use later to avoid duplicating (e.g. for Occurrences)
                            goodTypes.Add(new IfcTypeHandle(ifcType, pdt), type);
                            goodVariantNo++;
                        }

                        // Now build an occurrence of the type to provide representation
                        var objectName = BuildObjectName(logicalInstanceType, type.Name.ToString(), space, out var _, pdt);

                        BuildObject(builder, instanceType, typeNo, variant, space, objectName, $"{enumerationName} {entityName}", isFailure ? "07.03 Valid NOTDEFINED Occurrence" : "07.05 Valid TypeName Occurrence").AddDefiningType(type);

                        if (pdt != "NOTDEFINED")    // Already done above
                        {
                            // Creating badly named type
                            type = CreateType(ifcType.Name, builder, $"{typeIdentifier}-BAD", $"{enumerationName} {typeName}", "07.05 Invalid TypeName")
                                .WithDefaults(t => t with { PredefinedType = pdt == "USERDEFINED" ? enumerationName : pdt })
                                .AddDfeTypeData(IsCOBieType);

                            // Invalid TypeName
                            BuildObject(builder, instanceType, typeNo, badVariantNo++, failedTypes, objectName, $"{enumerationName} {entityName}", "07.05 Invalid TypeName Occurrence", 1).AddDefiningType(type);
                        }
                    }

                }
                else
                {
                    // We don't have a Typed PDT, or it's not applicable to enumerate (e.g. Doors, Furniture)
                    // EntityName_<OptionalEnumField>_TypeNN
                    var dataType = false ? "_OptionalData" : "";
                    typeIdentifier = $"{entityName}{dataType}_Type{goodVariantNo:0000}";
                    var type = CreateType(ifcType.Name, builder, typeIdentifier, $"{typeName}", "07.05 Valid TypeName")
                        .AddDfeTypeData(IsCOBieType);

                    goodTypes.Add(new IfcTypeHandle(ifcType), type);

                    var space = passedTypes;
                    var objectName = BuildObjectName(logicalInstanceType, type.Name.ToString(), space, out bool hasConventions);

                    BuildObject(builder, instanceType, typeNo, 0, space, objectName, entityName, "07.05 Valid TypeName Occurrence").AddDefiningType(type);


                    type = CreateType(ifcType.Name, builder, $"{typeIdentifier}-BAD", $"{typeName}", "07.05 Valid TypeName")
                        .AddDfeTypeData(IsCOBieType);
                    BuildObject(builder, instanceType, typeNo, 0, failedTypes, objectName, entityName, "07.05 Invalid TypeName Occurrence", 1).AddDefiningType(type);

                }

                typeNo++;
            }

            CreateBrokenTypes(builder, failedTypes);

            return goodTypes;
        }

        /// <summary>
        /// Create data specific failing cases for Types
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="fails"></param>
        private void CreateBrokenTypes(IModelInstanceBuilder builder, IIfcSpace fails)
        {
            // Create broken Types

            var style = builder.GetOrCreateStyle("Broken", builder.GetOrCreateColour("Red", 0.8, 0.1, 0.1));
            var typeNo = 1;

            // We use IfcTransportElement and IfcTransportElementType.ELEVATOR for all the test cases because it is a clear COBie Asset, has PDTs and does
            // not require 'Occurrence Type Mapping' 
            var ifcType = Schema["IfcTransportElementType"];
            var element = GetLogicalInstance(ifcType!);
            var stackNo = 2;
            var sequence = -1;

            var typeIdentifierBase = $"TransportElement_Elevator_Type";

            // 07.02 Doesn't seem possible to test given PDTs are not nullable

            BuildProduct(CreateType(ifcType!.Name, builder, $"", "TransportElementType", "07.04 Name Defined").WithDefaults(t => t with { PredefinedType = "ELEVATOR" })
                .AddDfeTypeData(IsCOBieType));

            var duplicateName = $"{typeIdentifierBase}{sequence:00}";
            BuildProduct(CreateType(ifcType!.Name, builder, duplicateName, "TransportElementType", "07.06 Name is Unique (1)").WithDefaults(t => t with { PredefinedType = "ELEVATOR" })
                .AddDfeTypeData(IsCOBieType));
            ++stackNo;
            --sequence;
            BuildProduct(CreateType(ifcType!.Name, builder, duplicateName, "TransportElementType", "07.06 Name is Unique (2)").WithDefaults(t => t with { PredefinedType = "ELEVATOR" })
                .AddDfeTypeData(IsCOBieType));
            --stackNo;
            BuildProduct(CreateType(ifcType!.Name, builder, $"{typeIdentifierBase}{sequence:00}", "", "07.07 Description Defined").WithDefaults(t => t with { PredefinedType = "ELEVATOR" })
                .AddDfeTypeData(IsCOBieType));

            BuildProduct(CreateBaseType("07.08 Is Classified with Pr_")
              .AddDfeTypeData(IsCOBieType, new MockCOBieData(ApplyClassification: false)));

            BuildProduct(CreateBaseType("07.09 AssetType Defined")
                .AddDfeTypeData(IsCOBieType, new MockCOBieData(AssetType: null)));

            BuildProduct(CreateBaseType("07.10 AssetType Is Valid")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(AssetType: "BAD")));

            BuildProduct(CreateBaseType("07.11 Manufacturer is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(Manufacturer: "")));

            BuildProduct(CreateBaseType("07.12 Manufacturer is Email or n/a")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(Manufacturer: "AcmeInc")));

            BuildProduct(CreateBaseType("07.13 Manufacturer is Email")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(Manufacturer: "n/a")));

            BuildProduct(CreateBaseType("07.35 ModelReference isDefined")
              .AddDfeTypeData(IsCOBieType, new MockCOBieData(ModelReference: null)));


            typeNo++;
            sequence = -1;

            BuildProduct(CreateBaseType("07.14 WarrantyGuarantorParts is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyGuarantorParts: "")));

            BuildProduct(CreateBaseType("07.15 WarrantyGuarantorParts is n/a or Email")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyGuarantorParts: "Not an email")));

            BuildProduct(CreateBaseType("07.16 WarrantyGuarantorParts is an Email")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyGuarantorParts: "n/a")));

            BuildProduct(CreateBaseType("07.17 WarrantyDurationParts is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyDurationParts: null)));

            BuildProduct(CreateBaseType("07.18 WarrantyDurationParts is zero or a duration")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyDurationParts: "5 years")));

            BuildProduct(CreateBaseType("07.19 WarrantyDurationParts is a non-zero duration")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyDurationParts: "0")));

            BuildProduct(CreateBaseType("07.20 WarrantyGuarantorLabor is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyGuarantorLabor: "")));

            BuildProduct(CreateBaseType("07.21 WarrantyGuarantorLabor is n/a or Email")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyGuarantorLabor: "Not an email")));

            BuildProduct(CreateBaseType("07.22 WarrantyGuarantorLabor is an Email")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyGuarantorLabor: "n/a")));

            BuildProduct(CreateBaseType("07.23 WarrantyDurationLabor is Defined")
              .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyDurationLabor: null)));

            BuildProduct(CreateBaseType("07.24 WarrantyDurationLabor is zero or a duration")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyDurationLabor: "5 years")));

            BuildProduct(CreateBaseType("07.25 WarrantyDurationLabor is a non-zero duration")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyDurationLabor: "0")));

            BuildProduct(CreateBaseType("07.30 WarrantyDescription is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyDescription: null)));

            BuildProduct(CreateBaseType("07.30 WarrantyDescription is n/a or valid")
              .AddDfeTypeData(IsCOBieType, new MockCOBieData(WarrantyDescription: "!!!!")));


            typeNo++;
            sequence = -1;

            BuildProduct(CreateBaseType("07.26 ReplacementCost is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(ReplacementCost: null)));

            BuildProduct(CreateBaseType("07.27 ReplacementCost is n/a or Valid")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(ReplacementCost: "TBC")));

            BuildProduct(CreateBaseType("07.28 ExpectedLife is Defined")
              .AddDfeTypeData(IsCOBieType, new MockCOBieData(ExpectedLife: null)));

            BuildProduct(CreateBaseType("07.29 ExpectedLife is n/a or Valid")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(ExpectedLife: "20 years")));

            typeNo++;
            sequence = -1;

            BuildProduct(CreateBaseType("07.32 NominalLength is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(NominalLength: null)));

            BuildProduct(CreateBaseType("07.33 NominalWidth is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(NominalWidth: null)));

            BuildProduct(CreateBaseType("07.34 NominalHeight is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(NominalHeight: null)));

            BuildProduct(CreateBaseType("07.36 Shape is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(Shape: null)));

            BuildProduct(CreateBaseType("07.37 Size is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(Size: null)));

            BuildProduct(CreateBaseType("07.38 Color is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(Color: null)));

            BuildProduct(CreateBaseType("07.39 Finish is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(Finish: null)));

            BuildProduct(CreateBaseType("07.40 Grade is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(Grade: null)));

            BuildProduct(CreateBaseType("07.41 Material is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(Material: null)));

            BuildProduct(CreateBaseType("07.42 Constituents is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(Constituents: null)));

            BuildProduct(CreateBaseType("07.43 Features is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(Features: null)));

            BuildProduct(CreateBaseType("07.44 AccessibilityPerformance is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(AccessibilityPerformance: null)));

            BuildProduct(CreateBaseType("07.45 CodePerformance is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(CodePerformance: null)));

            BuildProduct(CreateBaseType("07.46 SustainabilityPerformance is Defined")
               .AddDfeTypeData(IsCOBieType, new MockCOBieData(SustainabilityPerformance: null)));

            string BuildName()
            {
                return BuildObjectName(element!, $"{element.Name} of {ifcType.Name}", fails, "ELEVATOR");
            }

            IIfcTypeObject CreateBaseType(string tag)
            {
                var seq = GetNextSequence(typeIdentifierBase);
                return CreateType(ifcType!.Name, builder, $"{typeIdentifierBase}{seq:00}", $"TransportElementType", $"{tag} - Failure")
                    .WithDefaults(t => t with { PredefinedType = "ELEVATOR" });
            }

            IIfcRelDefinesByType BuildProduct(IIfcTypeObject typeObject)
            {
                return BuildProduct<IIfcTransportElement>(builder, typeNo, ++sequence, fails, BuildName(), $"{typeObject.Name} Occurrence", $"{typeObject.GetTag()} Occurrence", stackNo, style)
                                .AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}")
                                .AddDefiningType(typeObject)
                                ;
            }
        }

        private T BuildProduct<T>(IModelInstanceBuilder builder, int typeNo, int variant, IIfcSpace space, string objectName, string objectDescription, string tag, int stackNo = 1, IIfcPresentationStyle? style = null) where T : IIfcProduct
        {
            var entity = CreateObject<T>(builder, space, objectName, objectDescription, tag)
                .WithRepresentation(builder, GeometryDefaults, 100, 100, 100, style)
                .WithRelativePlacement(builder, GeometryDefaults, space,
                        new XbimPoint3D(110 * variant, (-110 * typeNo) + 10000, 100 + (220 * stackNo)))
                ;
            return entity;

        }

        static Lazy<Dictionary<string, ClassInfo>> LazyAssetDict = new Lazy<Dictionary<string, ClassInfo>>(() => DomainExtensions.CobieComponents
                .Select(c => Schema[c])
                .Where(s => s is not null)
                .SelectMany(c => c!.MatchingConcreteClasses)
                .DistinctBy(c => c.Name).ToDictionary(d => d.Name));

        static Lazy<Dictionary<string, ClassInfo>> LazyTypeDict = new Lazy<Dictionary<string, ClassInfo>>(() => DomainExtensions.CobieTypes
                .Select(c => Schema[c])
                .Where(s => s is not null)
                .SelectMany(c => c!.MatchingConcreteClasses)
                .DistinctBy(c => c.Name).ToDictionary(d => d.Name));

        private static bool IsCOBieAsset(IIfcProduct product)
        {
            var dict = LazyAssetDict.Value;

            return dict.ContainsKey(product.GetType().Name);
        }

        private static bool IsCOBieType(IIfcTypeObject type)
        {
            var dict = LazyTypeDict.Value;

            return dict.ContainsKey(type.GetType().Name);
        }

        private IIfcObject BuildObject(IModelInstanceBuilder builder, ClassInfo instanceType, int typeNo, int variant, IIfcSpace space, string objectName, string objectDescription, string tag, int stackNo = 0,
            bool showGhosted = false)
        {
            var entity = CreateObject<IIfcObject>(instanceType.Name, builder, space, objectName, objectDescription, tag);


            if (entity is IIfcOpeningElement opening)
            {
                var relVoids = builder.Factory.RelVoidsElement(o =>
                {
                    o.RelatedOpeningElement = opening;
                    o.RelatingBuildingElement = builder.Instances.OfType<IIfcWall>().First();
                });
                opening.WithRepresentation(builder, GeometryDefaults, 10, 10, 10)
                    .WithRelativePlacement(builder, GeometryDefaults, space);
            }
            else if (entity is IIfcSpace sp)
            {
                var lengthUnit = builder.Model.Instances.OfType<IIfcSIUnit>().First(u => u.UnitType == IfcUnitEnum.LENGTHUNIT);
                var areaUnit = builder.Model.Instances.OfType<IIfcSIUnit>().First(u => u.UnitType == IfcUnitEnum.AREAUNIT);
                sp
                    .WithRepresentation(builder, GeometryDefaults, 100, 100, 100)
                    .WithRelativePlacement(builder, GeometryDefaults, space,
                        new XbimPoint3D(110 * variant, (-110 * typeNo) + 10000, 100 + (220 * stackNo)))
                    .AddDfeData()
                    .AddSpaceQuants(lengthUnit, areaUnit)
                    .AddZone(builder, space.HasAssignments.OfType<IIfcRelAssignsToGroup>().First().RelatingGroup)
                    ;
            }
            else if (entity is IIfcProduct product)
            {
                var prefix = showGhosted ? "ghost" : "";
                var style = IsCOBieAsset(product) ?
                    builder.GetOrCreateStyle($"{prefix}COBie", builder.GetOrCreateColour("pink", 1, 0.5, 0.7), showGhosted ? 0.5 : 0) :
                    builder.GetOrCreateStyle($"{prefix}NonCobie", builder.GetOrCreateColour("ignore", 1, 1, 1), showGhosted ? 0.5 : 0)
                    ;
                product
                    .WithRepresentation(builder, GeometryDefaults, 100, 100, 100, style)
                    .WithRelativePlacement(builder, GeometryDefaults, space,
                        new XbimPoint3D(110 * variant, (-110 * typeNo) + 10000, 100 + (220 * stackNo)));

                if (IsCOBieAsset(product))
                {
                    product.AddDfeCOBieObjectData($"{GetNextSequence("Serial"):0000000}", $"{GetNextSequence("Barcode") + 65000:0000000}");
                }
                ;
                if (product is IIfcDoor door)
                {
                    door.AddDfeFireRating("30");
                }
                if (product is IIfcReinforcingBar rbar)
                {
                    rbar.AddDefaults();
                }
                if (product is IIfcReinforcingMesh mesh)
                {
                    mesh.AddDefaults();
                }
                if (product is IIfcTendon tendon)
                {
                    tendon.AddDefaults();
                }

                if (product is IIfcWallStandardCase wallsc)
                {
                    wallsc.AddDefaults(builder);
                }

            }

            return entity;
        }

        ConcurrentDictionary<string, int> _typeSeqenceDict = new ConcurrentDictionary<string, int>();


        private int GetNextSequence(string reference)
        {
            return _typeSeqenceDict.AddOrUpdate(reference, 1, (_, i) => ++i);
        }

        private static Lazy<IDictionary<string, string>> LazyDfeDict = new Lazy<IDictionary<string, string>>(GetDfeTypes);
        private IDictionary<string, string> DfeTypeDict { get => LazyDfeDict.Value; }

        private string BuildObjectName(ClassInfo ifcObjectType, string defaultTypeName, IIfcSpace space, string predefinedType = "")
        {
            return BuildObjectName(ifcObjectType, defaultTypeName, space, out bool _, predefinedType);
        }
        private string BuildObjectName(ClassInfo ifcObjectType, string defaultTypeName, IIfcSpace space, out bool matchFound, string predefinedType = "")
        {

            // remove 'Ifc' prefix for labeling
            var typeName = ifcObjectType.Name.Substring(3);
            var entityName = typeName;

            string? result;
            if (typeCodeDict.TryGetValue(typeName, out TypeMap? typeCode))
            {
                matchFound = true;
                if (!typeCode.HasOverides)
                {
                    // Simple case - same pattern for whole IFC class

                    var code = typeCode.GetCode();
                    if (typeCode.UsesSpaceNaming)
                    {
                        result = $"{space.Name}-{code}";
                        result = $"{result}{GetNextSequence(result):000}";
                    }
                    else
                    {
                        result = $"{code}-";
                        result = $"{result}{GetNextSequence(result):00000}";
                    }


                }
                else
                {
                    // Pattern depending on PredefinedType

                    //var enumerationName = DfeTypeDict.ContainsKey(predefinedType) ? DfeTypeDict[predefinedType] : predefinedType;

                    var code = typeCode.GetCode(predefinedType);

                    result = $"{code}-";
                    result = $"{result}{GetNextSequence(result):00000}";
                }

            }
            else if (typeName == "Space")
            {
                matchFound = true;
                const string chars = "ACDEFHIJKLMNOPQRSTUVWXYZ";
                result = $"{space.Name!.ToString()!.Substring(0, 5)}";
                var suffix = chars[GetNextSequence(result)];
                result = $"{result}{suffix}";
            }
            else
            {
                matchFound = false;
                //Console.WriteLine($"No Short name for {typeName} {predefinedType}");
                result = $"{defaultTypeName}";
                if (!string.IsNullOrEmpty(predefinedType))
                    result += $"-{predefinedType}";
                result = $"{result}{GetNextSequence(result):00000}";
            }
            return result;
        }

        /// <summary>
        /// Gets the best instance for the schema for the element type. E.g. IfcAirTerminal
        /// </summary>
        /// <param name="elementType"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        private ClassInfo MapSchemaInstance(ClassInfo elementType, IModel model)
        {
            var element = elementType.Name;
            var cls = Schema[element];
            if (cls != null)
            {
                return cls;
            }
            else
            {
                // Attempt lookup
                var map = SchemaTypeMap.InferSchemaForEntity(model, element.ToUpperInvariant());
                if (map != null)
                {
                    return map.ElementType;
                }
            }

            return Schema["IfcBuildingElementProxy"]!;
        }

        /// <summary>
        /// Gets the ideal / logical instance for a Type. E.g. IfcSensorType => IfcSensor (in IFC4 +)
        /// </summary>
        /// <param name="ifcType"></param>
        /// <returns></returns>
        private ClassInfo GetLogicalInstance(ClassInfo ifcType)
        {
            var schema = new HybridSchemaIfc2x3();
            var element = "";
            if (ifcType.Name.EndsWith("Type"))
                element = ifcType.Name.Substring(0, ifcType.Name.Length - 4);
            if (ifcType.Name.EndsWith("Style"))
                element = ifcType.Name.Substring(0, ifcType.Name.Length - 5);
            if (ifcType.Name.EndsWith("TypeObject"))
                element = "IfcObject";
            if (ifcType.Name.EndsWith("TypeProduct"))
                element = "IfcProduct";

            // 2x3 special case
            if (ifcType.Name.EndsWith("GasTerminalType"))
                element = "IfcFlowTerminal";
            if (ifcType.Name.EndsWith("ElectricHeaterType"))
                element = "IfcFlowTerminal";

            if (!string.IsNullOrEmpty(element))
            {
                var cls = schema[element];
                if (cls != null)
                {
                    return cls;
                }
            }
            return Schema["IfcBuildingElementProxy"]!;
        }

        private IEntityFactory GetEntityFactory(XbimSchemaVersion schemaVersion)
        {
            var modelProvider = provider.GetRequiredService<IModelProvider>();

            return modelProvider.EntityFactoryResolver(schemaVersion);
        }

        private static IIfcProject CreateProject(EntityCreator factory, DfeConfig config)
        {
            var project = factory.Project(o => o.WithDefaults(t => t with { Name = config.ProjectName, Description = config.ProjectDescription }));

            project.Initialize(ProjectUnits.SIUnitsUK);
            project.Phase = config.ProjectPhase;
            return project;
        }

        private static IIfcSite CreateSite(EntityCreator factory, DfeConfig config, IIfcProject project)
        {
            var site = factory.Site(o => o.WithDefaults(t => t with { Name = config.SiteName, Description = config.SiteDescription }))
                .AddLocalPlacement(0, 0, 0)
                ;
            site.CompositionType = IfcElementCompositionEnum.ELEMENT;
            project.AddSite(site);
            return site;
        }

        private IIfcBuilding CreateBuilding(IModelInstanceBuilder builder, DfeConfig config, IIfcSite site)
        {
            var building = builder.Factory.Building(o => o.WithDefaults(t => t with { Name = config.BuildingName, Description = config.BuildingDescription }))
                .WithClassificationReference("Uniclass", config.BuildingCategory)
                .WithPropertySingle("Additional_Pset_BuildingCommon", "BlockConstructionType", new IfcText(config.BuildingBlockConstructionType))
                .WithPropertySingle("Additional_Pset_BuildingCommon", "MaximumBlockHeight", new IfcLengthMeasure(config.BuildingMaximumBlockHeight ?? 18000))
                .WithPropertySingle("Pset_BuildingCommon", "NumberOfStoreys", new IfcInteger(config.BuildingNumberOfStoreys ?? 1))
                .WithPropertySingle("COBie_BuildingCommon_UK", "UPRN", new IfcText(config.BuildingUPRN))
                .WithRelativePlacement(builder, GeometryDefaults, site, null, new XbimVector3D(1, 0, 0))
            ;
            building.CompositionType = IfcElementCompositionEnum.ELEMENT;
            site.AddBuilding(building);
            return building;
        }

        private IEnumerable<IIfcBuildingStorey> CreateBuildingStoreys(IModelInstanceBuilder builder, DfeConfig config, IIfcBuilding building, string[] levels)
        {
            var storeyHeight = 3400;
            var stories = levels
                .Select((level, i) =>
                {

                    if (!floorDict.TryGetValue(level, out var floor))
                    {
                        floor = new Floor(level, "Invalid", "Bad level", "Bad Category");
                    }
                    var storey = builder.Factory.BuildingStorey(o =>
                    {
                        o.Name = floor.Name;
                        o.Description = floor.Description;
                        o.CompositionType = IfcElementCompositionEnum.ELEMENT;
                        o.Elevation = i * storeyHeight;
                    })
                    .WithPropertySingle("Additional_Pset_BuildingStoreyCommon", "NetHeight", new IfcLengthMeasure(storeyHeight - 600))
                    .WithClassificationReference("Floor", floor.Category)
                    .WithRelativePlacement(builder, GeometryDefaults, building,
                        new XbimPoint3D(0, 0, i * storeyHeight));
                    building.AddBuildingStorey(storey);
                    return storey;
                }).ToList();

            return stories;
        }

        private int floorNo = 0;

        private IIfcSpace CreateSpace(IModelInstanceBuilder builder, IIfcBuildingStorey storey, string roomNo, string description, string longDescription, IIfcZone assignedZone)
        {
            var space = builder.Factory.Space(o =>
            {
                o.Name = roomNo;
                o.LongName = longDescription;
                o.Description = description;
                o.CompositionType = IfcElementCompositionEnum.ELEMENT;
            })
            .AddDfeData()
            .AddZone(builder, assignedZone);
            
            storey.AddSpace(space);
            return space;
        }

        private IEnumerable<IIfcSpace> CreateSpaces(IModelInstanceBuilder builder, IEnumerable<IIfcBuildingStorey> storeys, string[] roomNos)
        {
            var spaces = new List<IIfcSpace>();

            var adsClassification = GetClassificationFileStrings("Dfe", "ADS_Codes.txt")
               .Select(s => new { Description = s[1].Trim(), Code = s[0].Trim(), Uniclass = s[2].Trim() })
               .ToArray();

            var adsClasses = adsClassification.Select(a => a.Uniclass).Distinct();

            var slClassification = GetClassificationFileStrings("Common", "Uniclass\\SL_Codes.txt")
                .Select(s => new { Description = s[1].Trim(), Code = s[0].Trim() })
                .Where(c => adsClasses.Contains(c.Code))
                .ToArray();

            var lengthUnit = builder.Model.Instances.OfType<IIfcSIUnit>().First(u => u.UnitType == IfcUnitEnum.LENGTHUNIT);
            var areaUnit = builder.Model.Instances.OfType<IIfcSIUnit>().First(u => u.UnitType == IfcUnitEnum.AREAUNIT);

            var i = 0;

            foreach (var storey in storeys)
            {
                floorNo++;
                var roomNo = 0;
                var rooms = roomNos.Select(r =>
                {
                    roomNo++;
                    i++;
                    var cls = i % 10;
                    var levelCode = storey.Name!.Value.ToString().Replace("Level ", "");
                    var slClass = slClassification[cls];
                    var adsClass = adsClassification.First(a => a.Uniclass == slClass.Code);

                    var space = builder.Factory.Space(o =>
                    {
                        o.Name = $"{levelCode}-{r}";
                        o.Description = $"Room {o.Name}-{i}";
                        o.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    })
                    .WithRepresentation(builder, GeometryDefaults, 2800, 5000, 4000)
                    .WithRelativePlacement(builder, GeometryDefaults, storey, new XbimPoint3D(6000 * roomNo, 0, 0))
                    .WithClassificationReference("Uniclass SL", slClass.Code, slClass.Description)
                    .WithClassificationReference("DFE ADS", adsClass.Code, adsClass.Description)
                    .WithPropertySingle("COBie_Space", "Roomtag", new IfcText("n/a"))
                    .WithQuantity("BaseQuantities", "Height", 2400, XbimQuantityTypeEnum.Length, lengthUnit)
                    .WithQuantity("BaseQuantities", "GrossFloorArea", 100 * i, XbimQuantityTypeEnum.Area, areaUnit)
                    .WithQuantity("BaseQuantities", "NetFloorArea", 95 * i, XbimQuantityTypeEnum.Area, areaUnit)
                    ;

                    storey.AddSpace(space);

                    return space;
                });
                spaces.AddRange(rooms);
            }

            return spaces;
        }

        private IEnumerable<IIfcZone> CreateZones(IModelInstanceBuilder builder, string[] zoneNames)
        {
            var zones = new List<IIfcZone>();

            var zoneDict = GetZoneData().Select(s => new { Code = s[0].Trim(), Category = s[1].Trim(), Description = s[2].Trim() })
                .ToDictionary(k => k.Code);

            foreach (var zoneName in zoneNames)
            {
                if(zoneDict.TryGetValue(zoneName, out var data))
                {
                    var zone = builder.Factory.Zone(o =>
                    {
                        o.Name = zoneName;
                        o.Description = data.Description;
                    })
                    .WithClassificationReference("Zones", data.Category);
                    zones.Add(zone);
                }
                else
                {
                    // Skip
                }

               
            }

            return zones;
        }


        private T CreateType<T>(IModelInstanceBuilder builder, string name, string description, string tag) where T : IIfcTypeObject
        {
            T type = CreateEntity<T>(builder);

            type.Name = name;
            type.Description = description;
            type.AddTag(tag);


            return type;
        }

        private IIfcTypeObject CreateType(string ifcType, IModelInstanceBuilder builder, string name, string description, string tag)
        {
            var ifcEntity = (IIfcTypeObject)CreateEntity(ifcType, builder);

            ifcEntity.Name = name;
            ifcEntity.Description = description;
            ifcEntity.AddTag(tag);

            return (IIfcTypeObject)ifcEntity;
        }

        private T CreateObject<T>(IModelInstanceBuilder ctx, IIfcSpatialElement spacialElement, string name, string description, string tag, string? objectType = null) where T : IIfcObject
        {
            T objectEntity = CreateEntity<T>(ctx);

            objectEntity.Name = name;
            objectEntity.Description = description;
            objectEntity.ObjectType = objectType;
            objectEntity.AddTag(tag);

            if (objectEntity is IIfcProduct product)
            {

                if (product is IIfcSpace space)
                {
                    space.CompositionType = IfcElementCompositionEnum.PARTIAL;
                    spacialElement.AddSpace(space);
                }
                else
                {
                    spacialElement.AddProductToSpatialStructure(product);
                }
            }

            return objectEntity;
        }

        private T CreateObject<T>(string ifcType, IModelInstanceBuilder ctx, IIfcSpatialElement spacialElement, string name, string description, string tag, string? objectType = null) where T : IIfcObject
        {
            var objectEntity = (T)CreateEntity(ifcType, ctx);

            objectEntity.Name = name;
            objectEntity.Description = description;
            objectEntity.AddTag(tag);

            if (objectEntity is IIfcProduct product)
            {
                product.ObjectType = objectType;
                if (product is IIfcSpace space)
                {
                    space.CompositionType = IfcElementCompositionEnum.PARTIAL;
                    spacialElement.AddSpace(space);
                }
                else if (product is IIfcFeatureElement)
                {
                    // Don't assign to space
                }
                else
                {
                    spacialElement.AddProductToSpatialStructure(product);
                }
            }

            return (T)objectEntity;
        }


        private static T CreateEntity<T>(IModelInstanceBuilder ctx) where T : IIfcRoot
        {
            var type = typeof(T);

            var expressName = (type.IsInterface && type.Name.StartsWith("IIfc")) ? type.Name.Substring(1) : type.Name;
            return (T)CreateEntity(expressName, ctx);
        }

        private static IIfcRoot CreateEntity(string expressTypeName, IModelInstanceBuilder ctx)
        {
            var expressType = ctx.Model.Metadata.ExpressType(expressTypeName.ToUpperInvariant());
            if (expressType is null) throw new NotSupportedException($"{expressTypeName} Type not supported in {ctx.Model.SchemaVersion}");

            var entity = (IIfcRoot)ctx.Instances.New(expressType.Type);
            return entity;
        }




        private static GeometryData GetContext(EntityCreator factory, IModel model)
        {
            return new GeometryData
            {
                Context = model.Instances.OfType<IIfcGeometricRepresentationContext>().FirstOrDefault(c => c.ContextType == "Model") ?? model.Instances.OfType<IIfcGeometricRepresentationContext>().First(),
                Origin = factory.CartesianPoint(o => o.SetXYZ(0, 0, 0)),
                Direction = factory.Direction(d => d.SetXYZ(0, 1, 0)),
                AxisUp = factory.Direction(d => d.SetXYZ(0, 0, 1))
            };
        }

        private void Sanity(IModelInstanceBuilder ctx)
        {
            // Sanity check model contents etc

            //var prodCount = ctx.Instances.OfType<IIfcProduct>().Count();
            //var typeCount = ctx.Instances.OfType<IIfcTypeObject>().Count();

            //var products = ctx.Instances.OfType<IIfcProduct>().Select(s => new { s.GetType().Name, PDT = s.GetPredefinedTypeValue() } )
            //    .GroupBy(s => new { s.Name, s.PDT })
            //    .OrderBy(s => s.Key.Name).ThenBy(s => s.Key.PDT)
            //    .ToList();

            //var types = ctx.Instances.OfType<IIfcTypeObject>().Select(s => new { s.GetType().Name, PDT = s.GetPredefinedTypeValue() })
            //    .GroupBy(s => new { s.Name, s.PDT })
            //    .OrderBy(s => s.Key.Name).ThenBy(s => s.Key.PDT)
            //    .ToList();

            //Console.WriteLine("Products");
            //foreach(var p in products)
            //{
            //    Console.WriteLine($"{p.Key.Name} : {p.Key.PDT} {p.Count()}");
            //}

            //Console.WriteLine("Tyoes");
            //foreach (var p in types)
            //{
            //    Console.WriteLine($"{p.Key.Name} : {p.Key.PDT} {p.Count()}");
            //}

            //foreach(var ent in spaces)
            //{
            //    var duplicates = ent.Ents.GroupBy(e => e.EntityLabel).Where(a => a.Count() > 1);
            //}

        }

        private static XbimEditorCredentials GetEditor()
        {
            var editor = new XbimEditorCredentials
            {
                EditorsGivenName = "Andy",
                EditorsFamilyName = "Ward",
                EditorsIdentifier = "andy.ward@xbim.net",
                EditorsOrganisationName = "Xbim",
                EditorsOrganisationIdentifier = "net.xbim",

                ApplicationFullName = "DfE test model builder",
                ApplicationVersion = "1.0.0-alpha",
                ApplicationDevelopersName = "Xbim",  // When same as EditorOrg => reuses the org
                ApplicationIdentifier = "dfe-model-builder"
            };
            return editor;
        }
    }

    public class IfcTypeHandle
    {
        public string Key { get; private set; }
        public IfcTypeHandle(ClassInfo classInfo, string predefinedType = "")
        {
            if (string.IsNullOrEmpty(predefinedType))
            {
                Key = classInfo.Name;
            }
            else
            {
                Key = $"{classInfo.Name}-{predefinedType}";
            }
        }

        public override string ToString()
        {
            return Key;
        }

        public override int GetHashCode()
        {
            return Key.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            if (obj is IfcTypeHandle h)
            {
                return h.Key == Key;
            }

            return base.Equals(obj);
        }
    }
}
