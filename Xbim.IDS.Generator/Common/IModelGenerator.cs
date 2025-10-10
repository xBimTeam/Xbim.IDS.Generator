using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xbim.IDS.Generator.Common
{
    /// <summary>
    /// Interface defining the creation of IFC model files
    /// </summary>
    public interface IModelGenerator
    {
        /// <summary>
        /// Generates a Test model based on conventions
        /// </summary>
        /// <returns></returns>
        Task GenerateTestModels();
    }
}
