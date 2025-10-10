using static Xbim.InformationSpecifications.RequirementCardinalityOptions;
using Xbim.InformationSpecifications.Cardinality;
using Xbim.InformationSpecifications;
using Microsoft.Extensions.Logging;

namespace Xbim.IDS.Generator.Common
{
    /// <summary>
    /// Represents the context of a Specification and its Requirement within the wider EIR etc
    /// </summary>
    public class SpecContext : IDisposable
    {
        /// <summary>
        /// The parent Context's Prefix
        /// </summary>
        public string Prefix { get; private set; } = string.Empty;

        public string FullPrefix { get; private set; } = string.Empty;

        /// <summary>
        /// The ordinal section number - automaticall incremented after each Identifier is generated
        /// </summary>
        public int Section { get; set; }

        /// <summary>
        /// Optional Section name used in place of the <see cref="Section"/> number
        /// </summary>
        public string SectionName { get; protected set; } = "";

        /// <summary>
        /// Gets the Section Name or Number as a identifier
        /// </summary>
        public string Id => string.IsNullOrEmpty(SectionName) ? $"{Section:D2}" : SectionName;

        /// <summary>
        /// A user friendly name representing the Section/Context
        /// </summary>
        public string Tag { get; private set; } = "";

        /// <summary>
        /// Gets the fully qualified identifier for this Context
        /// </summary>
        public string Identifier => string.IsNullOrEmpty(Prefix) ? Id : $"{Prefix}_{Id}";

        /// <summary>
        /// The stage the IDS file is targeting
        /// </summary>
        /// <remarks>Should be a single stage</remarks>
        public RibaStages TargetStage { get; private set; }

        /// <summary>
        /// Set of Stages the scope specifications are applicable to
        /// </summary>
        public RibaStages ApplicableToStages { get; private set; } = RibaStages.All;

        private IList<SpecContext> SubScopes { get; } = new List<SpecContext>();


        /// <summary>
        /// Construct a Root Context
        /// </summary>
        /// <param name="targetStage">The stage the IDS is being produced for</param>
        /// <param name="ids"></param>
        /// <param name="logger"></param>
        public SpecContext(RibaStages targetStage, Xids ids, ILogger<SpecContext>? logger = null)
        {
            logger ??= new Microsoft.Extensions.Logging.Abstractions.NullLogger<SpecContext>();
            switch (targetStage)
            {
                case RibaStages.Stage1:
                case RibaStages.Stage2:
                case RibaStages.Stage3:
                case RibaStages.Stage4:
                case RibaStages.Stage5:
                case RibaStages.Stage6:
                    TargetStage = targetStage;
                    break;
                default:
                    throw new NotSupportedException("Must be a single stage");
            }

            Ids = ids;
            Logger = logger;
        }

        /// <summary>
        /// Folder where interim files are stored
        /// </summary>
        public string BasePath { get; set; } = "";

        /// <summary>
        /// When true, outputs a file per spec in a folder structure based on scopes
        /// </summary>
        public bool SaveOneFilePerSpec { get; set; } = false;

        /// <summary>
        /// When true, outputs a file per scope level
        /// </summary>
        public bool SaveOneFilePerScope { get; set; } = false;

        /// <summary>
        /// Construct a child context
        /// </summary>
        /// <param name="parent"></param>
        private SpecContext(SpecContext parent)
        {

            Clone(parent);
            parent.SubScopes.Add(this);
            Prefix = parent.Identifier;
            FullPrefix = string.IsNullOrEmpty(parent.FullPrefix) ? parent.Identifier : parent.FullPrefix + "/" + parent.Identifier;
            Logger = parent.Logger;
            if (parent.SaveOneFilePerScope)
            {
                lazySpec = new Lazy<SpecificationsGroup>(() => CreateNewSpecGroup(parent.CurrentSpecGroup));
            }
            else
            {
                lazySpec = new Lazy<SpecificationsGroup>(parent.CurrentSpecGroup);
            }

        }

        private void Clone(SpecContext parent)
        {
            TargetStage = parent.TargetStage;
            ApplicableToStages = parent.ApplicableToStages;
            PrefixSpecNameWithId = parent.PrefixSpecNameWithId;
            Ids = parent.Ids;
            BasePath = parent.BasePath;
            BaseName = parent.BaseName;
            SaveOneFilePerScope = parent.SaveOneFilePerScope;
            SaveOneFilePerSpec = parent.SaveOneFilePerSpec;
            Tag = parent.Tag;
        }

        /// <summary>
        /// Flag indicating whether to prefix the Spec ID to the title
        /// </summary>
        public bool PrefixSpecNameWithId { get; set; } = true;

        /// <summary>
        /// Indicates whether applicable matches are Required, Optional or Prohibited.
        /// </summary>
        /// <remarks>E.g. Walls are typically Required in a model, Sensors may be opttional, and Proxies may be prohibited</remarks>
        public CardinalityEnum ApplicabilityCardinality { get; set; } = CardinalityEnum.Optional;

        /// <summary>
        /// Indicates whether a Requirement is Expected, Prohibited (or Optional)
        /// </summary>
        /// <remarks>Setting to Prohibited allows us to Negate requirements. E.g. PredefinedType <> NOTDEFINED</remarks>
        public Cardinality RequirementCardinality { get; set; } = Cardinality.Expected;

        /// <summary>
        /// Indicates whether this spec is applicable to the target stage
        /// </summary>
        public bool IsStageApplicable { get => ApplicableToStages.HasFlag(TargetStage); }
        public Xids Ids { get; private set; }
        public ILogger<SpecContext> Logger { get; }

        /// <summary>
        /// The current IDS spec group specs are added to
        /// </summary>
        public SpecificationsGroup CurrentSpecGroup { get => lazySpec.Value; }

        Lazy<SpecificationsGroup> lazySpec = new Lazy<SpecificationsGroup>();

        public string? BaseName { get; set; } = "";

        /// <summary>
        /// Override the section number
        /// </summary>
        /// <param name="section"></param>
        /// <returns></returns>
        public SpecContext SetSection(int section)
        {
            Section = section;
            return this;
        }

        /// <summary>
        /// Set a section name
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public SpecContext SetName(string name)
        {
            SectionName = name;
            return this;
        }

        public SpecContext SetRule(Cardinality cardinality)
        {
            RequirementCardinality = cardinality;
            return this;
        }

        public SpecContext ResetRule()
        {
            RequirementCardinality = Cardinality.Expected;
            return this;
        }

        public SpecContext SetMatches(CardinalityEnum cardinality)
        {
            ApplicabilityCardinality = cardinality;
            return this;
        }

        public SpecContext ResetMatches()
        {
            ApplicabilityCardinality = CardinalityEnum.Optional;
            return this;
        }

        public SpecContext SetApplicableStages(RibaStages stages)
        {
            ApplicableToStages = stages;
            return this;
        }

        /// <summary>
        /// Create a new Sub Scoped Context
        /// </summary>
        /// <param name="identifier"></param>
        /// <returns></returns>
        public SpecContext BeginSubscope(string identifier = "")
        {
            Increment();  // Increment parent section number when starting a new scope
            SectionName = identifier;
            return new SpecContext(this);
        }

        /// <summary>
        /// Generates the unique identifier for this context
        /// </summary>
        /// <returns></returns>
        public string GenerateIdentifier()
        {
            Increment();
            return Identifier;
        }

        /// <summary>
        /// Increments the Context section number
        /// </summary>
        /// <returns></returns>
        private SpecContext Increment()
        {
            Section++;
            return this;
        }

        /// <summary>
        /// Skip a section number
        /// </summary>
        /// <param name="reason"></param>
        /// <param name="quiet"></param>
        public void Skip(string reason, bool quiet = false)
        {
            Increment();
            if(IsStageApplicable)
            {
                if(!quiet)
                {
                    Logger.LogWarning("-Skipping {Identifier} {Tag}: {reason}", Identifier, Tag, reason);
                }
                else
                {
                    Logger.LogTrace("-Skipping {Identifier} {Tag}: {reason}", Identifier, Tag, reason);
                }
            }
        }

        /// <summary>
        /// Determines if Spec should be skipped for this stage, and skips where inapplicable
        /// </summary>
        /// <returns></returns>
        public bool ShouldSkipSpecForStage()
        {
            if (IsStageApplicable)
            {
                return false;
            }
            else
            {
                Skip($"Not applicable for {TargetStage}", true);
                return true;
            }
        }

        #region Dispose
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if(SaveOneFilePerScope)
                    {
                        CloseScope();
                    }
                    foreach (SpecContext scope in SubScopes)
                    {
                        scope.Dispose();
                    }
                }
                SubScopes.Clear();

                disposedValue = true;
            }
        }

        public void CloseScope()
        {
            if(CurrentSpecGroup.Specifications.Any())
            {
                Logger.LogInformation("{FullPrefix} {Tag} contains {SpecCount} specs", FullPrefix, Tag, CurrentSpecGroup.Specifications.Count());
            }
            else
            {
                Logger.LogTrace("Deleting empty group: {FullPrefix} {Tag}", FullPrefix ?? "Root", Tag);
                Ids.SpecificationsGroups.Remove(CurrentSpecGroup);
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private SpecificationsGroup CreateNewSpecGroup(SpecificationsGroup toCopy)
        {
            var stage = TargetStage.ToDescription().Replace("Stage ", "");

            var specGroup = new SpecificationsGroup(Ids)
            {
                Guid = Guid.NewGuid().ToString(),
                Name = $"{stage}_{Prefix} {Tag} - {BaseName}",
                Specifications = new List<Specification>(),

                Date = toCopy.Date,
                Milestone = toCopy.Milestone,
                Author = toCopy.Author,
                Description = toCopy.Description,
                Version = toCopy.Version,
                Purpose = toCopy.Purpose,
                Copyright = toCopy.Copyright

            };
            Ids.SpecificationsGroups.Add(specGroup);
            return specGroup;
        }

        /// <summary>
        /// Initialise the IDS header for this group of IDS specs
        /// </summary>
        /// <remarks>When <see cref="SaveOneFilePerScope"/> is <c>true</c> this template is used to propogate to child contexts</remarks>
        /// <param name="specGroupTemplate"></param>
        public void InitialiseSpecGroup(SpecificationsGroup specGroupTemplate)
        {
            lazySpec = new Lazy<SpecificationsGroup>(() => CreateNewSpecGroup(specGroupTemplate));
            BaseName = specGroupTemplate.Name;
        }

        public SpecContext AddTag(string tagName)
        {
            Tag = tagName;
            return this;
        }

        #endregion
    }
}
