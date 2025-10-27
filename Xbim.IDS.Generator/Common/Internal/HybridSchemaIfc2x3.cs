using IdsLib.IfcSchema;
using System.Collections;
using System.Reflection;
using Xbim.Common;
using Xbim.Common.Metadata;


namespace Xbim.IDS.Generator.Common.Internal
{

    // idslib's SchemaInfo is completely unextendable so we replicate what we need here

    /// <summary>
    /// A variant of the IFC2x3 schema that supports use of certain IFC4 elements that have an implicit equivalent in the older
    /// schema via a Type relationship
    /// </summary>
    public class HybridSchemaIfc2x3 : IEnumerable<ClassInfo>
    {

        protected Dictionary<string, ClassInfo> _classes = new Dictionary<string, ClassInfo>();

        bool linked = false;

        public HybridSchemaIfc2x3()
        {
            var baseline = SchemaInfo.SchemaIfc2x3;

            foreach(var cls in baseline)
            {
                var copy = new ClassInfo(cls.Name, cls.ParentName, cls.Type, cls.PredefinedTypeValues, cls.NameSpace, cls.DirectAttributes);
                SetProperty(copy, nameof(copy.RelationTypeClasses), cls.RelationTypeClasses!);
                Add(copy);
            }

            var mappableIfc4Types = SchemaTypeMap.Ifc2x3TypeMap;

            foreach(var kp in mappableIfc4Types)
            {
                // e.g IFCAirTerminal is usable in IFC2x3 using implementor agreement
                var cls = SchemaInfo.SchemaIfc4[kp.Key];
                if(cls != null)
                {
                    var copy = new ClassInfo(cls.Name, cls.ParentName, cls.Type, cls.PredefinedTypeValues, cls.NameSpace, cls.DirectAttributes);
                    SetProperty(copy, nameof(copy.RelationTypeClasses), cls.RelationTypeClasses!);
                    Add(copy);
                }
            }
            LinkTree();
        }


        private void LinkTree()
        {
            foreach (var currClass in _classes.Values)
            {
                var parent = currClass.ParentName;
                if (!string.IsNullOrWhiteSpace(parent) && _classes.TryGetValue(parent, out var resolvedParent))
                {
                    // if it's not in the subclasses yet, add it
                    if (!resolvedParent.SubClasses.Any(x => x.Name == currClass.Name))
                    {
                        resolvedParent.SubClasses.Add(currClass);
                    }
                    //currClass.Parent = resolvedParent;
                    SetProperty(currClass, nameof(currClass.Parent), resolvedParent);
                }
            }
            linked = true;
        }

        public static void SetProperty(object instance, string propertyName, object newValue)
        {
            try
            {
                Type type = instance.GetType();

                PropertyInfo? prop = type.GetProperty(propertyName);
                if (prop == null)
                    return;
                if (prop.PropertyType == typeof(decimal))
                {
                    prop.SetValue(instance, Convert.ToDecimal(newValue), null);

                }
                else
                {
                    prop.SetValue(instance, newValue, null);
                }
            }
            catch
            {
            }

        }

        /// <summary>
		/// Add a new classInfo to the collection
		/// </summary>
		public void Add(ClassInfo classToAdd)
        {
            _classes.Add(classToAdd.Name, classToAdd);
        }

        public ClassInfo? this[string className]
        {
            get
            {
                if (_classes.TryGetValue(className, out var cl))
                {
                    return cl;
                }
                return _classes.Values.FirstOrDefault(x => x.Name.Equals(className, StringComparison.InvariantCultureIgnoreCase));
            }
        }
        /// <summary>
		/// The default enumerator for the schema returns the classes defined within
		/// </summary>
		public IEnumerator<ClassInfo> GetEnumerator()
        {
          
            return _classes.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

    }


    // Copied from Xbim.IDS.Validator to avoid taking dependency on the validator
    internal class SchemaTypeMap
    {
        private static Lazy<IDictionary<string, SchemaInference>> lazySchemaMap = new Lazy<IDictionary<string, SchemaInference>>(() => BuildSchemaInferenceMappings());

        static IDictionary<string, SchemaInference> _ifc2x3Inferences => lazySchemaMap.Value;


        /// <summary>
        /// Infers the IFC2x3 equivalent of a new type in IFC 4, where one exists
        /// </summary>
        /// <remarks>Supports the implementation of the 'AirTerminal' issue in IFC2x3: https://github.com/buildingSMART/IDS/issues/116</remarks>
        /// <param name="model"></param>
        /// <param name="entityType"></param>
        /// <returns></returns>
        public static SchemaInference? InferSchemaForEntity(IModel model, string entityType)
        {
            if (_ifc2x3Inferences.TryGetValue(entityType, out var result))
            {
                return result;
            }
            return null;
        }

        /// <summary>
        /// A map of IFC4 types to IFC2x3 equivalents using a qualifying DefiningType
        /// </summary>
        public static IEnumerable<KeyValuePair<string, SchemaInference>> Ifc2x3TypeMap { get => _ifc2x3Inferences; }


        // Inferences: New types in later schemas that can be inferred in older schemas using Type information
        static IDictionary<string, SchemaInference> BuildSchemaInferenceMappings()
        {
            var baseSchema = ExpressMetaData.GetMetadata(new Ifc2x3.EntityFactoryIfc2x3());
            var targetSchema = ExpressMetaData.GetMetadata(new Ifc4.EntityFactoryIfc4());

            var implicitlyMapped = GetMappings(baseSchema, targetSchema);

            var dict = new Dictionary<string, SchemaInference>();

            foreach (var mapping in implicitlyMapped)
            {

                // The IFC2x3 element is usually the supertype of the IFC type, but not always
                string ifc2x3Element = mapping.NewType.ExpressNameUpper switch
                {
                    "IFCVIBRATIONISOLATOR" => "IFCEQUIPMENTELEMENT",    // Special case for IfcVibrationIsolatorType which moved to abstract IfcElementComponent[Type] in IFC4
                    "IFCSPACEHEATER" => "IFCENERGYCONVERSIONDEVICE",    // Spacial case for SpaceHeaterType which moved to IfcFlowTerminal in IFC4
                    _ => mapping.NewType.SuperType.ExpressName
                };
                var inference = new SchemaInference(SchemaInfo.SchemaIfc2x3[ifc2x3Element], SchemaInfo.SchemaIfc2x3[mapping.DefinedBy.ExpressName]);
                dict.Add(mapping.NewType.ExpressNameUpper, inference);
            }
            return dict;

        }

        private static IEnumerable<(ExpressType NewType, ExpressType DefinedBy)> GetMappings(ExpressMetaData baseSchema, ExpressMetaData targetSchema)
        {
            var products = targetSchema.ExpressType("IFCPRODUCT");

            foreach (var type in products.AllSubTypes)
            {
                if (baseSchema.ExpressType(type.ExpressNameUpper) == null)
                {
                    // New in the target (newer) schema. Check if a Type exists by convention we can use in the base schema
                    var baseSchemaType = baseSchema.ExpressType(type.ExpressNameUpper + "TYPE");
                    if (baseSchemaType != null)
                    {
                        // New but with Type we can use to discriminate in base schema
                        yield return (type, baseSchemaType);
                    }

                }
            }
        }

    }

    /// <summary>
    /// Defines a combination of IfcElement and IfcType that can be used to infer an IFC4+ type in IFC2x3
    /// </summary>
    /// <remarks>e.g. IFC2x3's IFCAIRTERMINAL = IFCFlOWTERMINAL defined by an IFCAIRTERMINALTYPE</remarks>
    internal class SchemaInference
    {

        internal SchemaInference(ClassInfo elementType, ClassInfo definingType)
        {
            DefiningType = definingType ?? throw new ArgumentNullException(nameof(definingType));
            ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
        }
        /// <summary>
        /// The IfcTypeObject that defines the IFC2x3 element
        /// </summary>
        public ClassInfo DefiningType { get; private set; }

        /// <summary>
        /// The IfcProduct appropriate to the IFC2x3 element
        /// </summary>
        public ClassInfo ElementType { get; private set; }
    }
}
