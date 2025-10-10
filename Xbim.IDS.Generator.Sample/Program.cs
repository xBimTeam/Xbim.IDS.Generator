using Xbim.IDS.Generator.Common;
using Xbim.IDS.Generator.Sample;


BaseGenerator generator = new SampleIdsGenerator();

await generator.PublishIDS();
