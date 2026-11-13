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

        /// <summary>
        /// How every value of this field is indexed, when that is known
        /// without looking at the values; decides whether the field can be
        /// the key of a join executed inside Lucene.
        /// </summary>
        JoinTermForm JoinTermForm { get; }
    }

    /// <summary>
    /// The relation between a string property's value and its indexed term.
    /// Two key fields can be joined inside Lucene only when they share a form
    /// other than <see cref="None"/>.
    /// </summary>
    internal enum JoinTermForm
    {
        /// <summary>
        /// Not a single term per document that is known in advance: a numeric,
        /// converted, collection or unindexed field, or an analyzer that may
        /// produce several terms.
        /// </summary>
        None,

        /// <summary>The term is the value itself.</summary>
        Exact,

        /// <summary>The term is the lowercased value.</summary>
        Lowercase
    }
}
