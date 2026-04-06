namespace Lucene.Net.Linq.Mapping
{
    /// <summary>
    /// Controls how a field is indexed. Maps onto Lucene 4.8's
    /// <c>IndexOptions</c> + tokenization + omitNorms triple.
    /// </summary>
    public enum IndexMode
    {
        NotIndexed = 0,
        Analyzed = 1,
        AnalyzedNoNorms = 2,
        NotAnalyzed = 3,
        NotAnalyzedNoNorms = 4,
    }
}
