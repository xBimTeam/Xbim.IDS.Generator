namespace Xbim.IDS.Generator.Common
{
    /// <summary>
    /// Maps a Type to a code, with overrides for any PredefinedType
    /// </summary>
    /// <param name="Code"></param>
    public record TypeMap(string Code)
    {
        private IDictionary<string, string> overrides = new Dictionary<string, string>();

        public TypeMap OverrideWith(string pdt, string code)
        {
            overrides.Add(pdt, code);
            return this;
        }

        public bool HasOverides { get => overrides.Any(); }

        public bool UsesSpaceNaming { get; private set; }

        public string GetCode(string pdt = "")
        {
            if (!string.IsNullOrEmpty(pdt) && HasOverides)
            {
                if (overrides.ContainsKey(pdt))
                {
                    return overrides[pdt];
                }
            }
            return Code;
        }

        public TypeMap SpaceNaming()
        {
            UsesSpaceNaming = true;
            return this;
        }
    }
}
