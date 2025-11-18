using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xbim.InformationSpecifications;

namespace Xbim.IDS.Generator.Common
{
    public static class IdsExtensions
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="group"></param>
        /// <returns></returns>
        public static string Decode(this FacetGroup group)
        {
            if (group.Facets.Any())
            {
                return "All elements " + string.Join(" AND ", group.Facets.Select((IFacet x, int i) =>  x.ApplicabilityDescription)) +
                    (
                    group.RequirementOptions is null ? "" :
                    string.Join(", ", group.RequirementOptions?.Select(x => x?.RelatedFacetCardinality.ToString() ?? "")!)
                    ); 
            }
            return group.Name ?? group.Description ?? group.Guid;
        }
    }
}
