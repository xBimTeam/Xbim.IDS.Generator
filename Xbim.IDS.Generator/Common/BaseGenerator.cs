using IdsLib.IfcSchema;
using System.Collections.ObjectModel;
using Xbim.Common.Step21;
using Xbim.InformationSpecifications;
using Xbim.InformationSpecifications.Cardinality;
using static Xbim.InformationSpecifications.PartOfFacet;
using static Xbim.InformationSpecifications.RequirementCardinalityOptions;

namespace Xbim.IDS.Generator.Common
{
    public abstract class BaseGenerator : IIdsSchemaGenerator
    {
        /// <summary>
        /// The Schema we use to generate the specifications. 
        /// </summary>
        /// <remarks>E.g. to generate specs from IfcProduct/IfcTypeObject</remarks>
        public static XbimSchemaVersion GenerationSchema { get; set; } = XbimSchemaVersion.Ifc4;

        /// <summary>
        /// Schemas we support in the produced IDS specificatons
        /// </summary>
        public static IfcSchemaVersions SupportedIfcSchemas { get; set; } = IfcSchemaVersions.IfcAllVersions;

        /// <summary>
        /// Set the IDS Identifier/Guid based on the context
        /// </summary>
        public static bool SetIdsIdentifier { get; set; } = true;

        /// <summary>
        /// Use IDS type inference - e.g. IfcAirTerminal = IfcFlowTerminal typedBy IfcAirTerminalType.
        /// </summary>
        /// <remarks>See https://github.com/buildingSMART/IDS/blob/development/Documentation/ImplementersDocumentation/ifc2x3-occurrence-type-mapping-table.md</remarks>
        public static bool UseIfc4TypesIn2x3 { get; set; } = false;

        /// <summary>
        /// Determines if the files output should be validated by the ids-audit tool
        /// </summary>
        public static bool ValidateIDSOutputs { get; set; } = false;

        public abstract Task PublishIDS();

        /// <summary>
        /// Gets the <see cref="SchemaInfo"/> for the current <see cref="GenerationSchema"/>
        /// </summary>
        public static SchemaInfo Schema
        {
            get
            {
                return GenerationSchema switch
                {
                    XbimSchemaVersion.Ifc2X3 => SchemaInfo.SchemaIfc2x3,
                    XbimSchemaVersion.Ifc4 => SchemaInfo.SchemaIfc4,
                    XbimSchemaVersion.Ifc4x1 => SchemaInfo.SchemaIfc4,
                    XbimSchemaVersion.Ifc4x3 => SchemaInfo.SchemaIfc4x3,
                    _ => throw new InvalidOperationException($"Schema '{GenerationSchema}' not supported")
                };
            }
        }

        public static IfcSchemaVersions IdsLibSchema
        {
            get
            {
                return GenerationSchema switch
                {
                    XbimSchemaVersion.Ifc2X3 => IfcSchemaVersions.Ifc2x3,
                    XbimSchemaVersion.Ifc4 => IfcSchemaVersions.Ifc4,
                    XbimSchemaVersion.Ifc4x1 => IfcSchemaVersions.Ifc4,
                    XbimSchemaVersion.Ifc4x3 => IfcSchemaVersions.Ifc4x3,

                    _ => IfcSchemaVersions.IfcNoVersion
                };
            }
        }

        public static IfcSchemaVersion XidsIfcSchema
        {
            get
            {
                return GenerationSchema switch
                {
                    XbimSchemaVersion.Ifc2X3 => IfcSchemaVersion.IFC2X3,
                    XbimSchemaVersion.Ifc4 => IfcSchemaVersion.IFC4,
                    XbimSchemaVersion.Ifc4x1 => IfcSchemaVersion.IFC4,
                    XbimSchemaVersion.Ifc4x3 => IfcSchemaVersion.IFC4X3,

                    _ => IfcSchemaVersion.Undefined
                };
            }
        }

        public static void FinaliseSpec(Specification spec, SpecContext context, string title)
        {
            var id = context.GenerateIdentifier();
            if (SetIdsIdentifier)
                spec.Guid = id;
            if (context.PrefixSpecNameWithId)
            {
                spec.Name = $"{id} : {title}";
            }
            else
            {
                spec.Name = $"{title}";
            }
            spec.Description = title;
            SetRequirementCardinality(spec.Requirement!, context.RequirementCardinality);
            var schemas = GetApplicableSchemas(spec.Applicability);
            spec.IfcVersion = schemas;

            if (context.SaveOneFilePerSpec)
            {
                SaveSingleSpec(spec, context);
            }
        }

        private static void SaveSingleSpec(Specification spec, SpecContext context)
        {
            var singleSpecIds = new Xids()
            {
                Guid = Guid.NewGuid().ToString(),
                Name = $"{spec.Name}",
                Stages = new List<string> { context.TargetStage.ToString() },
                SpecificationsGroups = new List<SpecificationsGroup>()
            };
            var now = DateTime.UtcNow;
            var singleFileHeader = new SpecificationsGroup(singleSpecIds)
            {
                // No/static date/version so we can minimse changes in version control & see differences easily
                 
                Guid = Guid.NewGuid().ToString(),
                Name = $"Single IDS File: {spec.Name} at {context.TargetStage}",
                Milestone = context.TargetStage.ToDescription(),
                Author = "info@xbim.net",
                Description = $"Assurance of IFC-SPF deliverables against DfE's information requirements",
                Version = $"S0",
                Purpose = "IDS testing and evaluation",
                Copyright = "xbim Ltd",

            };
            singleFileHeader.Specifications.Add(spec);
            singleSpecIds.SpecificationsGroups.Add(singleFileHeader);
            var folder = Path.Combine(Path.Combine(context.BasePath, context.TargetStage.ToString()), context.FullPrefix);


            Directory.CreateDirectory(folder);
            var fileName = MakeSafeFile(spec.Name) + ".ids";
            var stage = context.TargetStage.ToDescription().Replace("Stage ", "");
            fileName = $"{stage}_{fileName}";
            var file = Path.Combine(folder, fileName);
            singleSpecIds.ExportBuildingSmartIDS(file);
        }

        private static string MakeSafeFile(string fileName)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '-');
            }
            if (fileName.EndsWith("."))
                fileName = fileName.Substring(0, fileName.Length - 1);
            return fileName;
        }

        // Infer the schema support from the Applicable Entity Tyoe
        // e.g. IfcTank can only be IFC4 / 4X3
        public static List<IfcSchemaVersion> GetApplicableSchemas(FacetGroup applicability)
        {
            var result = new HashSet<IfcSchemaVersion>();
            var firstType = applicability.Facets.OfType<IfcTypeFacet>().FirstOrDefault();
            if (firstType?.IfcType?.IsSingleExact(out var exact) == true)
            {
                var ifcClass = exact.ToString();
                var classInfo = SchemaInfo.AllConcreteClasses.FirstOrDefault(s => s.UpperCaseName == ifcClass);

                if (classInfo != null)
                {
                    AddSchemas(result, classInfo);
                }

            }
            if (!result.Any())
            {
                result.Add(XidsIfcSchema);    // default
            }

            return result.ToList();

            static void AddSchemas(HashSet<IfcSchemaVersion> result, IfcClassInformation classInfo)
            {
                static bool IsSchemaSupported(IfcClassInformation classInfo, IfcSchemaVersions expectedSchema)
                {
                    return classInfo.ValidSchemaVersions.HasFlag(expectedSchema) && SupportedIfcSchemas.HasFlag(expectedSchema);
                }

                if (IsSchemaSupported(classInfo, IfcSchemaVersions.Ifc2x3))
                {
                    result.Add(IfcSchemaVersion.IFC2X3);
                }
                if (IsSchemaSupported(classInfo, IfcSchemaVersions.Ifc4))
                {
                    result.Add(IfcSchemaVersion.IFC4);
                }
                if (IsSchemaSupported(classInfo, IfcSchemaVersions.Ifc4x3))
                {
                    result.Add(IfcSchemaVersion.IFC4X3);
                }

                if (!classInfo.ValidSchemaVersions.HasFlag(IfcSchemaVersions.Ifc2x3) && !classInfo.UpperCaseName.EndsWith("TYPE"))
                {
                    // Look for Type equivalents. E.g we can use AirTerminal in IFC2x3 by inference from AirTerminalType
                    var candidate = classInfo.UpperCaseName + "TYPE";
                    var ifc2x3classInfo = SchemaInfo.SchemaIfc2x3[candidate];
                    if (ifc2x3classInfo != null)
                    {
                        result.Add(IfcSchemaVersion.IFC2X3);
                    }
                }
            }
        }


        /// <summary>
        /// Gets Uniclass Space Codes
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> GetUniclassSLCodes() => GetClassificationFile("Common", "Uniclass/SL_Codes.txt", 0);

        /// <summary>
        /// Gets Uniclass Entity Codes
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> GetUniclassEnCodes() => GetClassificationFile("Common", "Uniclass/EN_Codes.txt", 0);

        /// <summary>
        /// Gets NRM1 Codes
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> GetNrm1Codes() => GetClassificationFile("Common", "NRM/NRM1.txt", 0);

        /// <summary>
        /// Gets NRM2 Codes
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> GetNrm2Codes() => GetClassificationFile("Common", "NRM/NRM2.txt", 0);

        /// <summary>
        /// Gets NRM3 Codes
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> GetNrm3Codes() => GetClassificationFile("Common", "NRM/NRM3.txt", 0);

        /// <summary>
        /// Gets SFG20 Codes
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> GetSfg20Codes() => GetClassificationFile("Common", "SFG20.txt", 0);

        public static IEnumerable<string> GetClassificationFile(string area, string filename, int index)
        {
            var localFile = @$"{area}\Content\{filename}";
            string[] lines = [];
            if (File.Exists(localFile))
            {
                lines = File.ReadAllLines(localFile);
            }
            else
            {
                // try read from embedded
                var file = filename.Replace('/', '.').Replace('\\', '.');
                var fileOrResourceName = $"Xbim.IDS.Generator.{area}.Content.{file}";
                var thisAssembly = global::System.Reflection.Assembly.GetExecutingAssembly();
                using (var inputStream = thisAssembly.GetManifestResourceStream(fileOrResourceName))
                {
                    if (inputStream != null)
                    {
                        throw new NotImplementedException($"File not found: {filename}");
                    }
                    using var reader = new StreamReader(inputStream);
                    lines = reader.ReadAllLines().ToArray();
                }
            }

            return lines.Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => l.Split(":")[index].Trim());
        }

        public static IEnumerable<KeyValuePair<string, string>> GetClassificationFilePairs(string area, string filename)
        {
            return File.ReadAllLines(@$"{area}\Content\{filename}")
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(":"))
                .Select(v => new KeyValuePair<string, string>(v[1].Trim(), v[0].Trim()));
        }

        public static IEnumerable<string[]> GetClassificationFileStrings(string area, string filename)
        {
            return File.ReadAllLines(@$"{area}\Content\{filename}")
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(":"))
                ;
        }

        /// <summary>
        /// Returns word "should" or "shouldn't" or "can" based on the requirement cardinality
        /// </summary>
        /// <param name="context">Validation context</param>
        /// <returns>Returns word "should" or "shouldn't" or "can"</returns>
        private static string ShouldOrShouldnt(SpecContext context)
        {
            return context.RequirementCardinality switch
            {
                Cardinality.Prohibited => "Should not",
                Cardinality.Expected => "Should",
                Cardinality.Optional => "Can",
                _ => throw new ArgumentOutOfRangeException(nameof(context), "Unexpected requirement cardinality")
            };
        }

        public static void CreateAttributeValueSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string attribute, string value, SpecContext context, string? title = null)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetAttributeConstraint(ids, attribute, value);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, title ?? $"{selector.Name} {ShouldOrShouldnt(context)} Have {attribute} Matching '{value}'");
        }

        public static void CreateAttributePatternSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string attribute, string pattern, SpecContext context, string? patternNarrative = null)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetAttributePatternConstraint(ids, attribute, pattern);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {attribute} {ShouldOrShouldnt(context)} Match {patternNarrative ?? "Project Standards"}");
        }

        public static void CreateAttributeFromListSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string attribute, IEnumerable<string> values, SpecContext context, NetTypeName baseType = NetTypeName.String)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetAttributeFromListConstraint(ids, attribute, values, baseType);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have {attribute} In One Of {values.Count()} Predefined Values.");
        }

        public static void CreateAttributeDefinedSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string attribute, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetAttributeDefinedConstraint(ids, attribute);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have {attribute} Defined");
        }

        public static void CreateAttributeMinMaxLengthSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string attribute, int minLen, int maxLen, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetAttributeMinMaxConstraint(ids, attribute, minLen, maxLen);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have {attribute} Length Between {minLen} And {maxLen}");
        }

        public static void CreateAttributeWithValueInRangeSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string attribute, object? minValue, bool minInclusive, object? maxValue, bool maxInclusive, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetAttributeWithValueInRangeConstraint(ids, attribute, minValue, minInclusive, maxValue, maxInclusive);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name}  {ShouldOrShouldnt(context)} Have {attribute} Value Between {minValue ?? "nil"} And {maxValue ?? "&infin;"}");
        }

        public static void CreateClassificationDefinedSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string classificationName, ValueConstraint classification, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetClassificationSystemDefined(ids, classificationName, classification);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have {classificationName} Classification Defined");
        }


        public static void CreateClassificationPatternSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string classificationPattern, string pattern, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetClassificationConstraintPatterns(ids, classificationPattern, pattern);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have {classificationPattern} Classification With Pattern '{pattern}'");
        }

        public static void CreateClassificationFromListSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string classificationName, ValueConstraint classificationSystem, IEnumerable<string> values, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetClassificationConstraintList(ids, classificationName, classificationSystem, values);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have {classificationName} Classification With One Of {values.Count()} Predefined Values");
        }

        public static void CreateClassificationCodeValueSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string classificationName, ValueConstraint classificationSystem, string value, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetClassificationValueConstraint(ids, classificationName, classificationSystem, value);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have {classificationName} Classification With Value '{value}'");
        }

        public static void CreatePropertyFromListSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string property, string propertySet, IEnumerable<string> values, SpecContext context, string? dataType = null)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetPropertyIsFromListConstraint(ids, property, propertySet, values, dataType);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have Property '{propertySet}.{property}' With One Of {values.Count()} Predefined Values.");
        }

        public static void CreatePropertyDefinedSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string property, string propertySet, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetPropertyDefinedConstraint(ids, property, propertySet);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have Property '{propertySet}.{property}' Defined.");
        }

        public static void CreatePropertyWithValueSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string property, string propertySet, string value, SpecContext context, string? dataType = null)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetPropertyWithValueConstraint(ids, property, propertySet, value, dataType);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have Property '{propertySet}.{property}' With Value '{value}'.");
        }

        public static void CreatePropertyWithValueInRangeSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string property, string propertySet, SpecContext context, object? minValue, bool minInclusive, object? maxValue, bool maxInclusive, string? dataType = null)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetPropertyWithValueInRangeConstraint(ids, property, propertySet, dataType, minValue, minInclusive, maxValue, maxInclusive);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            var range = constraint.Facets.OfType<IfcPropertyFacet>().First().PropertyValue.Short();
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have Property '{propertySet}.{property}' With Value '{range}'.");
        }

        public static void CreatePropertyWithPatternSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string property, string propertySet, string pattern, string patternName, SpecContext context, string? dataType = "")
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetPropertyWithValuePatternConstraint(ids, property, propertySet, pattern, dataType);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have Property '{propertySet}.{property}' With Value Matching {patternName}");
        }


        public static void CreatePropertyWithPsetPatternSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string propertySetPattern, string propertyName, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetPropertyWithPsetPatternConstraint(ids, propertyName, propertySetPattern);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have Property Matching '{propertySetPattern}.{propertyName}' Defined");
        }

        public static void CreateIfcTypePredefinedType(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, string ifcType, string predefinedType, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetEntityPredefinedTypeConstraint(ids, ifcType, predefinedType);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} {ShouldOrShouldnt(context)} Have predefinedType '{predefinedType}'.");
        }


        public static void CreateMandatoryCardinalitySpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;

            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, null);
            FinaliseSpec(spec, context, $"Must have {selector.Name}s");
            MarkAsRequired(selector, projectSpecs);
        }

        public static void CreatePartOfSpecification(SpecificationsGroup projectSpecs, FacetGroup selector, Xids ids, PartOfRelation? relationship, string entityType, SpecContext context)
        {
            if (context.ShouldSkipSpecForStage()) return;
            var constraint = GetPartofConstraint(ids, relationship, entityType);
            var spec = ids.PrepareSpecification(projectSpecs, IfcSchemaVersion.IFC2X3, selector, constraint);
            FinaliseSpec(spec, context, $"{selector.Name} Should Have a {entityType}");
        }

        private static FacetGroup? GetPartofConstraint(Xids ids, PartOfRelation? relationship, string entityType)
        {
            var relation = relationship?.ToString();
            return new FacetGroup(ids.FacetRepository)
            {
                Name = entityType,
                Description = $"Should be part of {entityType} through {relationship} relation",
                Facets = new ObservableCollection<IFacet>
                {
                    new PartOfFacet
                    {
                        EntityRelation = relation!,
                        EntityType = new IfcTypeFacet
                        {
                            IfcType = entityType
                        }
                    }
                }
            };
        }

        public static FacetGroup GetPropertyDefinedConstraint(Xids ids, string property, string propertySet)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = property,
                Description = $"Property '{propertySet}.{property}' should be defined",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcPropertyFacet
                    {
                        PropertyName = property,
                        PropertySetName = propertySet,
                    }
                }
            };
        }

        /// <summary>
        /// Indicates the applicablity is required.
        /// </summary>
        /// <param name="applicability"></param>
        /// <param name="group"></param>
        public static void MarkAsRequired(FacetGroup applicability, SpecificationsGroup group)
        {
            MarkApplicability(applicability, group, CardinalityEnum.Required);
        }

        /// <summary>
        /// Indicates the applicability is prohibited
        /// </summary>
        /// <param name="applicability"></param>
        /// <param name="group"></param>
        public static void MarkAsProhibited(FacetGroup applicability, SpecificationsGroup group)
        {
            MarkApplicability(applicability, group, CardinalityEnum.Prohibited);
        }

        /// <summary>
        /// Indicates the applicability is optional (default)
        /// </summary>
        /// <param name="applicability"></param>
        /// <param name="group"></param>
        public static void MarkAsOptional(FacetGroup applicability, SpecificationsGroup group)
        {
            MarkApplicability(applicability, group, CardinalityEnum.Optional);
        }

        public static void MarkApplicability(FacetGroup applicability, SpecificationsGroup group, SpecContext context)
        {
            MarkApplicability(applicability, group, context.ApplicabilityCardinality);
        }

        public static void MarkApplicability(FacetGroup applicability, SpecificationsGroup group, CardinalityEnum expectation)
        {
            // Find each spec with this applicability and mark the cardinality we expect. E.g. Are Space required, optional or prohibited
            foreach (var cardinality in group.Specifications
                .Where(s => s.Applicability == applicability)
                .Select(s => s.Cardinality)
                .OfType<SimpleCardinality>())
            {
                cardinality.ApplicabilityCardinality = expectation;
            }
        }

        public static string RemoveSuffix(string suffix, string targetName)
        {
            if (targetName.EndsWith(suffix))
            {
                return targetName.Substring(0, targetName.Length - suffix.Length);
            }
            return targetName;
        }

        private static void SetRequirementCardinality(FacetGroup group, Cardinality cardinality)
        {
            foreach (var facet in group.Facets)
            {
                group.RequirementOptions = new ObservableCollection<RequirementCardinalityOptions> { new(facet, cardinality) };
            }
        }


        public static FacetGroup GetPropertyWithValueConstraint(Xids ids, string property, string propertySet, string value, string? dataType)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = property,
                Description = $"Property '{propertySet}.{property}' Should Have a value",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcPropertyFacet
                    {
                        PropertyName = property,
                        PropertySetName = propertySet,
                        PropertyValue = value,
                        DataType = dataType?.ToUpperInvariant()
                    }
                }
            };
        }

        public static FacetGroup GetPropertyWithValueInRangeConstraint(Xids ids, string property, string propertySet, string? dataType, object? minValue, bool minInclusive, object? maxValue, bool maxInclusive)
        {

            var range = new RangeConstraint();
            if (minValue != null)
            {
                range.MinValue = minValue.ToString();
                range.MinInclusive = minInclusive;
            }
            if (maxValue != null)
            {
                range.MaxValue = maxValue.ToString();
                range.MaxInclusive = maxInclusive;
            }
            var constraint = new ValueConstraint(NetTypeName.Double);
            constraint.AcceptedValues.Add(range);
            return new FacetGroup(ids.FacetRepository)
            {
                Name = property,
                Description = $"Property '{propertySet}.{property}' Should Have A Value",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcPropertyFacet
                    {
                        PropertyName = property,
                        PropertySetName = propertySet,
                        PropertyValue = constraint,
                        DataType = dataType?.ToUpperInvariant()
                    }
                }
            };
        }

        public static FacetGroup GetPropertyWithValuePatternConstraint(Xids ids, string property, string propertySet, string valuePattern, string? dataType)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = property,
                Description = $"Property '{propertySet}.{property}' Should Have A Value Matching Pattern '/{valuePattern}/'",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcPropertyFacet
                    {
                        PropertyName = property,
                        PropertySetName = propertySet,
                        PropertyValue = ValueConstraint.CreatePattern(valuePattern),
                        DataType = dataType?.ToUpperInvariant()
                    }
                }
            };
        }

        public static FacetGroup GetPropertyWithPsetPatternConstraint(Xids ids, string property, string propertySetPattern)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = property,
                Description = $"Property Matching '{propertySetPattern}.{property}' Should Be Defined'",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcPropertyFacet
                    {
                        PropertyName = property,
                        PropertySetName =  ValueConstraint.CreatePattern(propertySetPattern),
                        //PropertyValue = ValueConstraint.CreatePattern(valuePattern),
                    }
                }
            };
        }

        public static FacetGroup GetPropertyIsFromListConstraint(Xids ids, string property, string propertySet, IEnumerable<string> values, string? dataType)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = $"{propertySet}.{property}",
                Description = $"Property '{propertySet}.{property}' should be one of {values.Count()} values",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcPropertyFacet
                    {
                        PropertyName = property,
                        PropertySetName = propertySet,
                        PropertyValue = new ValueConstraint(values),
                        DataType = dataType?.ToUpperInvariant()
                    }
                }
            };
        }

        public static FacetGroup GetClassificationConstraintPatterns(Xids ids, string classificationPattern, string pattern)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = classificationPattern,
                Description = $"Classification '/{classificationPattern}/' Should Have value Matching the pattern /{pattern}/",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcClassificationFacet
                    {
                        ClassificationSystem = ValueConstraint.CreatePattern(classificationPattern),
                        Identification = ValueConstraint.CreatePattern(pattern)
                    }
                }
            };
        }

        public static FacetGroup GetClassificationConstraintPattern(Xids ids, string classification, string pattern)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = classification,
                Description = $"Classification '{classification}' Should Have value Matching the pattern /{pattern}/",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcClassificationFacet
                    {
                        ClassificationSystem = classification,
                        Identification = ValueConstraint.CreatePattern(pattern)
                    }
                }
            };
        }

        public static FacetGroup GetClassificationConstraintList(Xids ids, string name, ValueConstraint classificationSystem, IEnumerable<string> values)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = name,
                Description = $"Classification '{name}' Should Have Value From One Of {values.Count()} Oredefined Values.",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcClassificationFacet
                    {
                        ClassificationSystem = classificationSystem,
                        Identification = new ValueConstraint(values)
                    }
                }
            };
        }

        public static FacetGroup GetClassificationValueConstraint(Xids ids, string classificationName, ValueConstraint system, string value)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = classificationName,
                Description = $"Classification '{classificationName}' Should Have Value '{value}'",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcClassificationFacet
                    {
                        ClassificationSystem = system,
                        Identification = value
                    }
                }
            };
        }

        public static FacetGroup GetClassificationSystemDefined(Xids ids, string classificationName, ValueConstraint system)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = classificationName,
                Description = $"Classification system '{classificationName}' Should Be Defined",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcClassificationFacet
                    {
                        ClassificationSystem = system
                    }
                }
            };
        }

        public static FacetGroup GetAttributeDefinedConstraint(Xids ids, string attribute)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = attribute,
                Description = $"Attribute '{attribute}' should be defined",
                Facets = new ObservableCollection<IFacet>
                {
                    new AttributeFacet
                    {
                        AttributeName = attribute
                    }
                }
            };
        }

        public static FacetGroup GetAttributeMinMaxConstraint(Xids ids, string attribute, int minLen, int maxLen)
        {
            var facet = new AttributeFacet
            {
                AttributeName = attribute,
                AttributeValue = new ValueConstraint()
            };
            var lenConstraint = new StructureConstraint()
            {
                MinLength = minLen,
                MaxLength = maxLen
            };
            facet.AttributeValue.AddAccepted(lenConstraint);
            return new FacetGroup(ids.FacetRepository)
            {
                Name = attribute,
                Description = $"Attribute '{attribute}' value should be between {minLen} and {maxLen} chars",
                Facets = new ObservableCollection<IFacet>
                {
                    facet
                }
            };
        }

        public static FacetGroup GetAttributeWithValueInRangeConstraint(Xids ids, string attributeName, object? minValue, bool minInclusive, object? maxValue, bool maxInclusive)
        {

            var range = new RangeConstraint();
            if (minValue != null)
            {
                range.MinValue = minValue.ToString();
                range.MinInclusive = minInclusive;
            }
            if (maxValue != null)
            {
                range.MaxValue = maxValue.ToString();
                range.MaxInclusive = maxInclusive;
            }
            var constraint = new ValueConstraint(NetTypeName.Double);
            constraint.AcceptedValues!.Add(range);
            return new FacetGroup(ids.FacetRepository)
            {
                Name = attributeName,
                Description = $"Attribute {attributeName}' Should Have a value in range",
                Facets = new ObservableCollection<IFacet>
                {
                    new AttributeFacet
                    {
                        AttributeName = attributeName,
                        AttributeValue = constraint,
                    }
                }
            };
        }


        public static FacetGroup GetAttributeConstraint(Xids ids, string name, string value)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = name,
                Description = $"Attribute '{name}' should be '{value}'",
                Facets = new ObservableCollection<IFacet>
                {
                    new AttributeFacet
                    {
                        AttributeName = name,
                        AttributeValue = value
                    }
                }
            };
        }

        public static FacetGroup GetAttributePatternConstraint(Xids ids, string name, string pattern)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = name,
                Description = $"Attribute '{name}' Should Match pattern /{pattern}/",
                Facets = new ObservableCollection<IFacet>
                {
                    new AttributeFacet
                    {
                        AttributeName = name,
                        AttributeValue = ValueConstraint.CreatePattern(pattern)
                    }
                }
            };
        }

        public static FacetGroup GetAttributeFromListConstraint(Xids ids, string name, IEnumerable<string> values, NetTypeName baseType = NetTypeName.String)
        {
            var constraint = new ValueConstraint(values);
            constraint.BaseType = baseType;
            return new FacetGroup(ids.FacetRepository)
            {
                Name = name,
                Description = $"Attribute '{name}' should be one of {values.Count()} predefined values.",
                Facets = new ObservableCollection<IFacet>
                {
                    new AttributeFacet
                    {
                        AttributeName = name,
                        AttributeValue = constraint
                    }
                }
            };
        }

        public static FacetGroup GetEntityPredefinedTypeConstraint(Xids ids, string ifcType, string predefinedType)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = ifcType,
                Description = $"Entity '{ifcType}' Should Have predefined Type {predefinedType}",
                Facets = new ObservableCollection<IFacet>
                {
                    new IfcTypeFacet
                    {
                        IfcType = ifcType,
                        PredefinedType = predefinedType
                    }
                }
            };
        }

        public static FacetGroup GetEntityApplicability(Xids ids, string name, string ifcType, bool includeSubTypes = true)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = name,
                Description = $"{name} entity selector",
                Facets = new ObservableCollection<IFacet> {
                    new IfcTypeFacet {
                        IfcType = ifcType.ToUpperInvariant(),
                        IncludeSubtypes = includeSubTypes
                    }
                }
            };
        }

        public static FacetGroup GetEntityApplicability(Xids ids, string name, string[] ifcTypes)
        {
            IEnumerable<string> allTypes = GetSubTypes(ifcTypes);
            return new FacetGroup(ids.FacetRepository)
            {
                Name = name,
                Description = $"{name} entity selector",
                Facets = new ObservableCollection<IFacet> {
                    new IfcTypeFacet {
                        IfcType = new ValueConstraint(allTypes),
                        IncludeSubtypes = false
                    }
                }
            };
        }

        public static FacetGroup GetClassificationSystemApplicability(Xids ids, string name, string systemPattern, string itemPattern = "")
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = name,
                Description = $"{name} entity selector",
                Facets = new ObservableCollection<IFacet> {
                    new IfcClassificationFacet {
                        ClassificationSystem = ValueConstraint.CreatePattern(systemPattern),
                        Identification = !string.IsNullOrEmpty(itemPattern) ? ValueConstraint.CreatePattern(itemPattern) : null
                    }
                }
            };
        }

        public static FacetGroup GetPSetApplicability(Xids ids, string name, string psetName, string? propName = null)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = name,
                Description = $"{psetName}.{propName} property selector",
                Facets = new ObservableCollection<IFacet> {
                    new IfcPropertyFacet {
                        PropertySetName = psetName,
                        PropertyName = propName != null ? propName : ValueConstraint.CreatePattern(".*"),
                    }
                }
            };
        }


        public static IEnumerable<string> GetSubTypes(string[] ifcTypes)
        {
            foreach (string ifcType in ifcTypes)
            {

                var subTypes = SchemaInfo.GetConcreteClassesFrom(ifcType.ToUpperInvariant(), IdsLibSchema);
                foreach (var subType in subTypes)
                {
                    yield return subType;
                }
            }
        }

        public static FacetGroup GetEntityApplicabilityWithPredefinedType(Xids ids, string name, string ifcType, string predefinedType, bool includeSubTypes = true)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = name,
                Description = $"{name}.{predefinedType} entity selector",
                Facets = new ObservableCollection<IFacet> {
                    new IfcTypeFacet {
                        IfcType = ifcType,
                        PredefinedType = predefinedType,
                        IncludeSubtypes = includeSubTypes
                    }
                }
            };
        }

        public static FacetGroup GetEntityApplicabilityWithClassification(Xids ids, string name, string ifcType, string systemPattern, string classification, bool includeSubTypes = true)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = name,
                Description = $"{name} entity selector with {classification} Classification",
                Facets = new ObservableCollection<IFacet> {
                    new IfcTypeFacet {
                        IfcType = ifcType,
                        IncludeSubtypes = includeSubTypes
                    },
                    new IfcClassificationFacet {
                        ClassificationSystem = ValueConstraint.CreatePattern(systemPattern),
                        Identification = classification
                    }
                }
            };
        }

        public static FacetGroup GetEntityApplicabilityWithClassifications(Xids ids, string name, string ifcType, string systemPattern, IEnumerable<string> classifications, bool includeSubTypes = true)
        {
            return new FacetGroup(ids.FacetRepository)
            {
                Name = name,
                Description = $"{name} entity selector with {systemPattern} Classification",
                Facets = new ObservableCollection<IFacet> {
                    new IfcTypeFacet {
                        IfcType = ifcType,
                        IncludeSubtypes = includeSubTypes
                    },
                    new IfcClassificationFacet {
                        ClassificationSystem = ValueConstraint.CreatePattern(systemPattern),
                        Identification = new ValueConstraint(classifications)
                    }
                }
            };
        }
    }

    internal static class StreamExtensions
    {
        internal static IEnumerable<string> ReadAllLines(this StreamReader reader)
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                yield return line;
            }
        }
    }
}
