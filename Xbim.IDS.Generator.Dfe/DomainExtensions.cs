using Xbim.IDS.Generator.Common;
using Xbim.Ifc;
using Xbim.Ifc.Fluent;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;

namespace Xbim.IDS.Generator.Dfe
{

    /// <summary>
    /// Dfe Domain specific Ifc implementation
    /// </summary>
    public static class DomainExtensions
    {
        public static readonly string[] CobieTypes = [
            "IfcDoorStyle",
            "IfcBuildingElementProxyType",
            //"IfcChimneyType",
            //"IfcCoveringType",
            "IfcWindowStyle",
            "IfcDistributionControlElementType",
            "IfcDistributionChamberElementType",
            "IfcEnergyConversionDeviceType",
            "IfcFlowControllerType",
            "IfcFlowMovingDeviceType",
            "IfcFlowStorageDeviceType",
            "IfcFlowTerminalType",
            "IfcFlowTreatmentDeviceType",
            "IfcDiscreteAccessoryType",
            "IfcMechanicalFastenerType",
            "IfcVibrationIsolatorType",
            "IfcFurnishingElementType",
            "IfcTransportElementType",
        ];

        // Based on https://github.com/xBimTeam/XbimCobieExpress/blob/master/Xbim.CobieExpress.Exchanger/FilterHelper/COBieDefaultFilters.config
        public static readonly string[] CobieComponents = [
            "IfcBuildingElementProxy",
            //"IfcChimney",
            //"IfcCovering",
            "IfcDoor",
            "IfcShadingDevice",
            "IfcWindow",
            "IfcFlowMovingDevice",
            "IfcVibrationIsolator",
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
            "IfcTransportElement",
        ];

        public static IIfcSpace AddDfeData(this IIfcSpace space)
        {

            return space
                .WithPropertySingle("COBie_Space", "Roomtag", new IfcText("n/a"))

                .WithClassificationReference("Uniclass SL", "SL_90_50_87", "Teaching resources stores")
                .WithClassificationReference("DFE ADS", "STT10", "Teaching resources stores")
                ;
        }

        public static IIfcSpace AddSpaceQuants(this IIfcSpace space, IIfcSIUnit lengthUnit, IIfcSIUnit areaUnit)
        {
            return space.WithQuantity("BaseQuantities", "Height", 4800, XbimQuantityTypeEnum.Length, lengthUnit)
                        .WithQuantity("BaseQuantities", "GrossFloorArea", 20000, XbimQuantityTypeEnum.Area, areaUnit)
                        .WithQuantity("BaseQuantities", "NetFloorArea", 19000, XbimQuantityTypeEnum.Area, areaUnit);
        }


        public static IIfcDoor AddDfeFireRating(this IIfcDoor entity, string? rating)
        {
            if (rating is null)
                return entity;
            return entity
                .WithPropertySingle("Pset_DoorCommon", "FireRating", new IfcLabel(rating));
        }

        public static IIfcReinforcingBar AddDefaults(this IIfcReinforcingBar entity)
        {
            entity.NominalDiameter = new IfcPositiveLengthMeasure(100);

            return entity;
        }

        public static IIfcReinforcingMesh AddDefaults(this IIfcReinforcingMesh entity)
        {
            entity.LongitudinalBarNominalDiameter = new IfcPositiveLengthMeasure(100);
            entity.TransverseBarNominalDiameter = new IfcPositiveLengthMeasure(100);
            entity.TransverseBarSpacing = new IfcPositiveLengthMeasure(100);
            entity.LongitudinalBarSpacing = new IfcPositiveLengthMeasure(100);
            return entity;
        }

        public static IIfcTendon AddDefaults(this IIfcTendon entity)
        {
            entity.NominalDiameter = new IfcPositiveLengthMeasure(100);

            return entity;
        }

        public static IIfcWallStandardCase AddDefaults(this IIfcWallStandardCase entity, IModelInstanceBuilder ctx)
        {
            ctx.Factory.RelAssociatesMaterial(o =>
            {
                o.RelatedObjects.Add(entity);
                o.RelatingMaterial = ctx.Factory.MaterialLayerSetUsage(u =>
                {
                    u.DirectionSense = IfcDirectionSenseEnum.NEGATIVE;
                    u.LayerSetDirection = IfcLayerSetDirectionEnum.AXIS2;
                    u.OffsetFromReferenceLine = 100;
                    u.ForLayerSet = ctx.Factory.MaterialLayerSet(ls =>
                    {
                        ls.LayerSetName = "Block";
                        ls.MaterialLayers.Add(ctx.Factory.MaterialLayer(l =>
                        {
                            l.LayerThickness = 100;
                            l.Material = ctx.Factory.Material(m =>
                            {
                                m.Name = "Brick";
                            });
                        }));
                    });
                });
            });

            return entity;
        }

        public static T AddDfeCOBieObjectData<T>(this T entity, string serialNo, string barcode, string? assetIdentifier = null, string? tag = null,
            string? installationDate = null, string? warranteeStartDate = null) where T : IIfcProduct
        {
            return entity
                .WithPropertySingle("Pset_ManufacturerOccurrence", "SerialNumber", new IfcIdentifier(serialNo))
                .WithPropertySingle("Pset_ManufacturerOccurrence", "BarCode", new IfcIdentifier(barcode))
                .WithPropertySingle("COBie_Component", "AssetIdentifier", new IfcText(assetIdentifier ?? "n/a"))
                .WithPropertySingle("COBie_Component", "TagNumber", new IfcText(tag ?? "n/a"))
                .WithPropertySingle("COBie_Component", "InstallationDate", new IfcText(installationDate ?? "1900-12-31T23:59:59"))
                .WithPropertySingle("COBie_Component", "WarrantyStartDate", new IfcText(warranteeStartDate ?? "1900-12-31T23:59:59"))
                ;
        }

        public static T AddDfeTypeData<T>(this T type, Func<IIfcTypeObject,bool>? isCobieFn = null, MockCOBieData? data = null) where T: IIfcTypeObject
        {
            data ??= new MockCOBieData();
            isCobieFn ??= _ => true;
            var domain = type.GetType().Namespace!.Split(".").LastOrDefault();
            var productType =  domain switch 
            {
                "BuildingcontrolsDomain" => "Pr_75",
                "ElectricalDomain" => "Pr_75",
                "HVACDomain" => "Pr_60_60",
                "PlumbingFireProtectionDomain" => "Pr_40",
                "ProductExtension" => "Pr_35",
                "SharedBldgElements" => "Pr_20",
                "SharedBldgServiceElements" => "Pr_60_65",
                _ => "Pr_60"
            };
            var manufacturer = $"sales.{domain}@example.com";
            var guarantor = $"service.{domain}@example.com";
            if(data.ApplyClassification)
                type
                    .WithClassificationReference("Uniclass Pr", productType, domain!);

            if(isCobieFn(type))
                type
                .WithPropertyOptionalSingle("COBie_Asset", "AssetType", new IfcText(data.AssetType))
                .WithPropertyOptionalSingle("Pset_ManufacturerTypeInformation", "Manufacturer", new IfcLabel(data.Manufacturer ?? manufacturer))
                .WithPropertyOptionalSingle("Pset_ManufacturerTypeInformation", "ModelReference", new IfcLabel(data.ModelReference))
                .WithPropertyOptionalSingle("COBie_Warranty", "WarrantyGuarantorParts", new IfcText(data.WarrantyGuarantorParts ?? guarantor))
                .WithPropertyOptionalSingle("COBie_Warranty", "WarrantyDurationParts", new IfcText(data.WarrantyDurationParts))
                .WithPropertyOptionalSingle("COBie_Warranty", "WarrantyGuarantorLabor", new IfcText(data.WarrantyGuarantorLabor ?? guarantor))
                .WithPropertyOptionalSingle("COBie_Warranty", "WarrantyDurationLabor", new IfcText(data.WarrantyDurationLabor))
                .WithPropertyOptionalSingle("COBie_Warranty", "WarrantyDescription", new IfcText(data.WarrantyDescription))
                .WithPropertyOptionalSingle("COBie_EconomicImpactValues", "ReplacementCost", new IfcText(data.ReplacementCost))
                .WithPropertyOptionalSingle("COBie_ServiceLife", "ExpectedLife", new IfcText(data.ExpectedLife))

                .WithPropertyOptionalSingle("COBie_Specification", "NominalLength", new IfcLengthMeasure(data.NominalLength ?? 0))
                .WithPropertyOptionalSingle("COBie_Specification", "NominalWidth", new IfcLengthMeasure(data.NominalWidth ?? 0))
                .WithPropertyOptionalSingle("COBie_Specification", "NominalHeight", new IfcLengthMeasure(data.NominalHeight ?? 0))

                .WithPropertyOptionalSingle("COBie_Specification", "Shape", new IfcText(data.Shape))
                .WithPropertyOptionalSingle("COBie_Specification", "Color", new IfcText(data.Color))
                .WithPropertyOptionalSingle("COBie_Specification", "Size", new IfcText(data.Size))
                .WithPropertyOptionalSingle("COBie_Specification", "Finish", new IfcText(data.Finish))
                .WithPropertyOptionalSingle("COBie_Specification", "Grade", new IfcText(data.Grade))
                .WithPropertyOptionalSingle("COBie_Specification", "Material", new IfcText(data.Material))
                .WithPropertyOptionalSingle("COBie_Specification", "Constituents", new IfcText(data.Constituents))
                .WithPropertyOptionalSingle("COBie_Specification", "Features", new IfcText(data.Features))
                .WithPropertyOptionalSingle("COBie_Specification", "AccessibilityPerformance", new IfcText(data.AccessibilityPerformance))
                .WithPropertyOptionalSingle("COBie_Specification", "CodePerformance", new IfcText(data.CodePerformance))
                .WithPropertyOptionalSingle("COBie_Specification", "SustainabilityPerformance", new IfcText(data.SustainabilityPerformance))

                ;
            return type;
        }
    }

    /// <summary>
    /// Represents some valid COBie Type data that can be mutated into specific invalid data
    /// </summary>
    /// <remarks>BY convention, null and zero are undefined and therefore omitted</remarks>
    /// <param name="AssetType"></param>
    /// <param name="Manufacturer"></param>
    /// <param name="ModelReference"></param>
    /// <param name="WarrantyGuarantorParts"></param>
    /// <param name="WarrantyDurationParts"></param>
    /// <param name="WarrantyGuarantorLabor"></param>
    /// <param name="WarrantyDurationLabor"></param>
    /// <param name="WarrantyDescription"></param>
    /// <param name="ReplacementCost"></param>
    /// <param name="ExpectedLife"></param>
    /// <param name="NominalLength"></param>
    /// <param name="NominalWidth"></param>
    /// <param name="NominalHeight"></param>
    /// <param name="Shape"></param>
    /// <param name="Color"></param>
    /// <param name="Size"></param>
    /// <param name="Finish"></param>
    /// <param name="Grade"></param>
    /// <param name="Material"></param>
    /// <param name="Constituents"></param>
    /// <param name="Features"></param>
    /// <param name="AccessibilityPerformance"></param>
    /// <param name="CodePerformance"></param>
    /// <param name="SustainabilityPerformance"></param>
    /// <param name="ApplyClassification"></param>
    public record MockCOBieData(
        string? AssetType = "Fixed", 
        string? Manufacturer = null,
        string? ModelReference = "Some Ref",
        string? WarrantyGuarantorParts = null,
        string? WarrantyDurationParts = "2",
        string? WarrantyGuarantorLabor = null,
        string? WarrantyDurationLabor = "2",
        string? WarrantyDescription = "n/a",
        string? ReplacementCost = "n/a",
        string? ExpectedLife = "n/a",

        double? NominalLength = 100,
        double? NominalWidth = 100,
        double? NominalHeight = 100,

        string? Shape = "n/a",
        string? Color = "n/a",
        string? Size = "n/a",
        string? Finish = "n/a",
        string? Grade = "n/a",
        string? Material = "n/a",
        string? Constituents = "n/a",
        string? Features = "n/a",
        string? AccessibilityPerformance = "n/a",
        string? CodePerformance = "n/a",
        string? SustainabilityPerformance = "n/a",
        bool ApplyClassification = true
        );
}
