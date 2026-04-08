namespace Lucene.Net.Linq.Mapping
{
    /// <summary>
    /// Controls term-vector storage for a field.
    /// </summary>
    public enum TermVectorMode
    {
        No = 0,
        Yes = 1,
        WithOffsets = 2,
        WithPositions = 3,
        WithPositionsAndOffsets = 4,
    }
}
