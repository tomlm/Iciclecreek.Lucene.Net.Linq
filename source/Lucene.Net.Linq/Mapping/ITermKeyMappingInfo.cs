namespace Lucene.Net.Linq.Mapping
{
    /// <summary>
    /// A field mapping that can say which single indexed term a property
    /// value becomes. Joins use it to look keys up in the index and to
    /// compare them the way the field is configured (for example
    /// case-insensitively on a default-mapped string field).
    /// </summary>
    internal interface ITermKeyMappingInfo : IFieldMappingInfo
    {
        /// <summary>
        /// The text of the term <paramref name="value"/> is indexed as, or
        /// <c>null</c> when the value is not indexed as exactly one term.
        /// </summary>
        string ConvertToTermText(object value);
    }
}
