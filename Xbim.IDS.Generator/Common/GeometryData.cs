using Xbim.Ifc4.Interfaces;

namespace Xbim.IDS.Generator.Common
{
    public class GeometryData
    {
        public required IIfcGeometricRepresentationContext Context { get; set; }
        public required IIfcDirection AxisUp { get; set; }

        public required IIfcDirection Direction { get; set; }

        public required IIfcCartesianPoint Origin { get; set; }
    }
}
