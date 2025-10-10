using IdsLib.IfcSchema;
using System.Collections;
using System.Reflection;
using Xbim.IDS.Validator.Core.Helpers;

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
}
