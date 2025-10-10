using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc.Fluent;
using Xbim.Ifc4.Interfaces;

namespace Xbim.IDS.Generator.Common
{
    /// <summary>
    /// Generic IFC Extensions
    /// </summary>
    public static class IfcExtensions
    {
        public static void AddBuilding(this IIfcSite site, IIfcBuilding building)
        {
            var decomposition = site.IsDecomposedBy.FirstOrDefault();
            if (decomposition == null) //none defined create the relationship
            {
                var factory = new EntityCreator(site.Model);
                var relSub = factory.RelAggregates(r =>
                {
                    r.RelatingObject = site;
                    r.RelatedObjects.Add(building);
                });
            }
            else
                decomposition.RelatedObjects.Add(building);
        }

        public static void AddBuildingStorey(this IIfcSpatialElement spatialElement, IIfcBuildingStorey buildingStorey)
        {
            var decomposition = spatialElement.IsDecomposedBy.FirstOrDefault();
            if (decomposition == null) //none defined create the relationship
            {
                var factory = new EntityCreator(spatialElement.Model);
                var relSub = factory.RelAggregates(r =>
                {
                    r.RelatingObject = spatialElement;
                    r.RelatedObjects.Add(buildingStorey);
                });
            }
            else
                decomposition.RelatedObjects.Add(buildingStorey);
        }

        public static void AddSpace(this IIfcSpatialElement spatialElement, IIfcSpace space)
        {
            var decomposition = spatialElement.IsDecomposedBy.FirstOrDefault();
            if (decomposition == null) //none defined create the relationship
            {
                var factory = new EntityCreator(spatialElement.Model);
                var relSub = factory.RelAggregates(r =>
                {
                    r.RelatingObject = spatialElement;
                    r.RelatedObjects.Add(space);
                });
            }
            else
                decomposition.RelatedObjects.Add(space);
        }

        public static void AddProductToSpatialStructure(this IIfcSpatialElement spatialElement, IIfcProduct product)
        {
            var contains = spatialElement.ContainsElements.FirstOrDefault();
            if (contains == null) //none defined create the relationship
            {
                var factory = new EntityCreator(spatialElement.Model);
                var relSub = factory.RelContainedInSpatialStructure(r =>
                {
                    r.RelatingStructure = spatialElement;
                    r.RelatedElements.Add(product);
                });
            }
            else
                contains.RelatedElements.Add(product);
        }

        public static T AddLocalPlacement<T>(this T element, double x, double y, double z, IIfcSpatialElement? parent = null) where T: IIfcProduct
        {
            var factory = new EntityCreator(element.Model);

            var placement = factory.Axis2Placement3D(o =>
            {
                o.Location = element.GetOrCreateCartesianPoint(factory, x, y, z);
            });
            var localPlacement = factory.LocalPlacement(o =>
            {
                o.RelativePlacement = placement;
                o.PlacementRelTo = parent?.ObjectPlacement;
            });

            element.ObjectPlacement = localPlacement;

            return element;
        }

        public static IIfcCartesianPoint GetOrCreateCartesianPoint<T>(this T entity, EntityCreator factory,  double x, double y, double? z = null) where T : IIfcObject
        {
            var point = entity.Model.Instances.OfType<IIfcCartesianPoint>().FirstOrDefault(p => p.X == x && p.Y == y && p.Z == z);
            if(point is null)
            {
                point = factory.CartesianPoint(o =>
                {
                    if(z.HasValue)
                    {
                        o.SetXYZ(x, y, z.Value);
                    }
                    else
                    {
                        o.SetXY(x, y);
                    }
                });
            }
            return point;
        }

        public static IIfcCartesianPoint GetOrCreateCartesianPoint<T>(this T entity, EntityCreator factory, IIfcCartesianPoint src) where T : IIfcObject
        {
            var point = entity.Model.Instances.OfType<IIfcCartesianPoint>().FirstOrDefault(p => p.X == src.X && p.Y == src.Y && p.Z == src.Z);
            if (point is null)
            {
                point = src;
            }
            return point;
        }

        public static IIfcDirection GetOrCreateDirection<T>(this T entity, EntityCreator factory, double x, double y, double z) where T : IIfcObject
        {
            var direction = entity.Model.Instances.OfType<IIfcDirection>().FirstOrDefault(p => p.X == x && p.Y == y && p.Z == z);
            if (direction is null)
            {
                direction = factory.Direction(o =>
                {
                    o.SetXYZ(x, y, z);
                });
            }
            return direction;
        }

        public static IIfcDirection GetOrCreateDirection<T>(this T entity, EntityCreator factory, IIfcDirection src) where T : IIfcObject
        {
            var direction = entity.Model.Instances.OfType<IIfcDirection>().FirstOrDefault(p => p.X == src.X && p.Y == src.Y && p.Z == src.Z);
            if (direction is null)
            {
                direction = src;
            }
            return direction;
        }

        /// <summary>
        /// Creates a PropertySingleValue for an entity in the given PropertySet with the supplied Name and Value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        /// <param name="propertySet"></param>
        /// <param name="propertyName"></param>
        /// <param name="ifcValue"></param>
        /// <returns></returns>
        public static T WithClassificationReference<T>(this T entity, string classSystem, string identifier, string description = "") where T : IIfcObjectDefinition
        {

            var factory = new EntityCreator(entity.Model);

            var classReference = entity.Model.GetOrCreateClassificationReference(classSystem, identifier, description, factory);
            
            var association = entity.Model.GetOrCreateClassificationRel(classReference, factory);
            association.RelatedObjects.Add(entity);


            return entity;
        }

       

        private static IIfcClassification GetOrCreateClassification(this IModel model, string systemName, EntityCreator factory)
        {
            var system = model.Instances.OfType<IIfcClassification>().FirstOrDefault(c => c.Name == systemName);

            if(system == null)
            {
                system = factory.Classification(o => 
                { 
                    o.Name = systemName; 
                    o.Source = "http://tbc.com";
                    o.Edition = "2015";
                });
            }

            return system;
        }

        private static IIfcClassificationReference GetOrCreateClassificationReference(this IModel model, string systemName, string identifier, string description, EntityCreator factory)
        {
            var system = model.GetOrCreateClassification(systemName, factory);
            var classReference = model.Instances.OfType<IIfcClassificationReference>().FirstOrDefault(c => c.Identification == identifier && c.ReferencedSource == system);

            if (classReference == null)
            {
                classReference = factory.ClassificationReference(o => 
                { 
                    o.Identification = identifier;
                    o.Name = description;
                    o.ReferencedSource = system; 
                });
            }

            return classReference;
        }

        private static IIfcRelAssociatesClassification GetOrCreateClassificationRel(this IModel model, IIfcClassificationReference classification, EntityCreator factory)
        {
            var relAssociates = model.Instances.OfType<IIfcRelAssociatesClassification>().FirstOrDefault(c => c.RelatingClassification == classification);

            if (relAssociates == null)
            {
                relAssociates = factory.RelAssociatesClassification(o =>
                {
                    o.Name = classification.Name;
                    o.RelatingClassification = classification;
                });
            }

            return relAssociates;
        }


        public static IIfcSurfaceStyle GetOrCreateStyle(this IModelInstanceBuilder builder, string styleName, IIfcColourRgb colour, double transparency = 0)
        {
            var factory = builder.Factory;
            var style = builder.Instances.OfType<IIfcSurfaceStyle>().FirstOrDefault(s => s.Name == styleName);
            style ??= factory.SurfaceStyle(o =>
            {
                o.Name = styleName;
                o.Side = IfcSurfaceSide.BOTH;
                o.Styles.Add(factory.SurfaceStyleRendering(r =>
                {
                    r.SurfaceColour = colour;
                    r.Transparency = transparency > 0 ? new Ifc4.MeasureResource.IfcNormalisedRatioMeasure(transparency) : null;
                }));
            });
            return style;
        }

        public static IIfcColourRgb GetOrCreateColour(this IModelInstanceBuilder builder, string colourName, double red, double green, double blue)
        {
            var factory = builder.Factory;
            var colour = builder.Instances.OfType<IIfcColourRgb>().FirstOrDefault(s => s.Name == colourName);

            colour ??= factory.ColourRgb(o =>
            {
                o.Name = colourName;
                o.Red = red;
                o.Green = green;
                o.Blue = blue;
                
            });
            return colour;
        }

        public static IIfcShapeRepresentation CreateExtrudedShape<T>(this T entity, EntityCreator factory, GeometryData geometryData, 
            double x,
            double y,
            double extrusionDepth,
            IIfcPresentationStyle? style = null) where T : IIfcObject
        {

            var insertPoint2D = entity.GetOrCreateCartesianPoint(factory, 0, 0);

            var rectProf = factory.RectangleProfileDef(o =>
            {
                o.ProfileType = IfcProfileTypeEnum.AREA;
                o.XDim = x;
                o.YDim = y;
                o.Position = factory.Axis2Placement2D(o => o.Location = insertPoint2D);
            });


            var body = factory.ExtrudedAreaSolid(o =>
            {
                o.Depth = extrusionDepth;
                o.SweptArea = rectProf;
                o.ExtrudedDirection = geometryData.AxisUp;
                o.Position = factory.Axis2Placement3D(o => o.Location = geometryData.Origin);
            });


            var shape = factory.ShapeRepresentation(o =>
            {
                o.ContextOfItems = geometryData.Context;
                o.RepresentationType = "SweptSolid";
                o.RepresentationIdentifier = "Body";
                o.Items.Add(body);
            });

            if(style != null)
            {
                factory.StyledItem(o =>
                {
                    o.Item = body;
                    o.Styles.Add(style);
                });
            }

            return shape;
        }

        public static IIfcLocalPlacement CreateLocalPlacement<T>(this T spatialElement, EntityCreator factory, GeometryData geometryContext, IIfcCartesianPoint origin3D, IIfcDirection? refDirection = null) where T : IIfcSpatialElement
        {
            return factory.LocalPlacement(o =>
            {
                var axis3D = factory.Axis2Placement3D(o =>
                {
                    o.Location = origin3D;
                    o.RefDirection = refDirection ?? geometryContext.Direction;
                    o.Axis = geometryContext.AxisUp;
                });
                o.PlacementRelTo = spatialElement.ObjectPlacement;
                o.RelativePlacement = axis3D;
            });
        }


        public static T WithRepresentation<T>(this T product, IModelInstanceBuilder builder, GeometryData geometryContext, double height, double width, double thickness,
            IIfcPresentationStyle? style = null) where T: IIfcProduct
        {
            var factory = builder.Factory;
            var representation = factory.ProductDefinitionShape(o =>
            {
                o.Representations.Add(product.CreateExtrudedShape(factory, geometryContext, width, thickness, height, style));
            });
            product.Representation = representation;
            return product;
        }

        public static T WithRelativePlacement<T>(this T product, IModelInstanceBuilder builder, GeometryData geometryContext, IIfcSpatialElement spatialElement, 
            XbimPoint3D? point = null,
            XbimVector3D? direction = null
        ) where T : IIfcProduct
        {
            point ??= XbimPoint3D.Zero;
            direction ??= new XbimVector3D(0, 1, 0);
            var factory = builder.Factory;
            
 
            var origin3D = product.GetOrCreateCartesianPoint(factory, point.Value.X, point.Value.Y, point.Value.Z);
            var ifcDirection = product.GetOrCreateDirection(factory, direction.Value.X, direction.Value.Y, direction.Value.Z);
            IIfcLocalPlacement relativeSpacePlacement = spatialElement.CreateLocalPlacement(factory, geometryContext, origin3D, ifcDirection);
            product.ObjectPlacement = relativeSpacePlacement;
            return product;
        }

        public static IIfcSpace AddZone(this IIfcSpace space, IModelInstanceBuilder builder, IIfcGroup zone)
        {
            if(zone != null)
            {
                var assignment = builder.Instances.OfType<IIfcRelAssignsToGroup>().FirstOrDefault(r => r.RelatingGroup == zone);
                assignment ??= builder.Factory.RelAssignsToGroup(o =>
                {
                    o.RelatingGroup = zone;
                    o.Name = "Spatial Zone Assignment";
                }
                    );
                assignment.RelatedObjects.Add(space);
            }
            
            return space;
        }

        public static T WithPropertyOptionalSingle<T>(this T entity, string propertySet, string propertyName, IIfcValue ifcValue) where T : IIfcObjectDefinition
        {
            if (ifcValue.Value is null ||string.IsNullOrEmpty(ifcValue.ToString()) || ifcValue is IIfcLengthMeasure l && l.Value == 0)
                return entity;
            var type = ifcValue.GetType();
            entity.SetPropertySingleValue(propertySet, propertyName, type).NominalValue = ifcValue;

            return entity;
        }



        public static T AddTag<T>(this T entity, string tag) where T: IIfcObjectDefinition
        {
            if (entity is IIfcElement elem)
            {
                elem.Tag = tag;
            }
            else if (entity is IIfcElementType et)
            {
                et.Tag = tag;
            }
            else if (entity is IIfcTypeProduct tp)
            {
                tp.Tag = tag;
            }
            else if(entity is IIfcSpatialElement sp)
            {
                sp.LongName = tag;
            }
            else
            {
                Console.WriteLine($"Warning, cannot tag {entity.GetType().Name}");
                //throw new NotImplementedException("Cannot tag this type");
            }
            return entity;
        }

        public static string? GetTag<T>(this T entity) where T : IIfcObjectDefinition
        {
            if (entity is IIfcElement elem)
            {
                return elem.Tag;
            }
            if (entity is IIfcElementType et)
            {
                return et.Tag;
            }
            return "";
        }
    }
}
