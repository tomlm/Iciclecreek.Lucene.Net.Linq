using Lucene.Net.Facet;

namespace Lucene.Net.Linq.Mapping
{
    /// <summary>
    /// A <see cref="FacetsConfig"/> in which every dimension is multi-valued.
    /// It holds no per-dimension state and is never modified, so one shared
    /// instance serves every mapper and thread instead of a new config per
    /// document.
    /// </summary>
    internal sealed class MultiValuedFacetsConfig : FacetsConfig
    {
        public static readonly MultiValuedFacetsConfig Instance = new MultiValuedFacetsConfig();

        private static readonly DimConfig MultiValued = new DimConfig { IsMultiValued = true };

        private MultiValuedFacetsConfig()
        {
        }

        protected override DimConfig DefaultDimConfig => MultiValued;
    }
}
