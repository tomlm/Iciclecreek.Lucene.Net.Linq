using System.Collections.Generic;
using Lucene.Net.Documents;

namespace Lucene.Net.Linq.Mapping
{
    /// <summary>
    /// Facet metadata for a mapped field. Implemented by field mappers that
    /// can take part in <see cref="LuceneMethods.Facets{T}"/>.
    /// </summary>
    public interface IFacetFieldMappingInfo : IFieldMappingInfo
    {
        /// <summary>
        /// <c>true</c> when the field's values are written as facet values.
        /// The facet dimension is <see cref="IFieldMappingInfo.FieldName"/>.
        /// </summary>
        bool Facet { get; }

        /// <summary>
        /// Name of the numeric doc-values field used for range counts,
        /// or <c>null</c> when the field does not support range counts.
        /// The built-in mappers use a hidden field named <c>{Field}$range</c>.
        /// </summary>
        /// <remarks>
        /// Use a field that is not also indexed. In a segment where the field
        /// has no doc values, Lucene's FieldCache un-inverts its indexed terms,
        /// so documents written before the column existed would be counted
        /// (or fail to decode) instead of being skipped.
        /// </remarks>
        string RangeFacetField { get; }

        /// <summary>
        /// How values in <see cref="RangeFacetField"/> are encoded:
        /// <see cref="NumericFieldType.INT64"/> (a <c>NumericDocValuesField</c>) or
        /// <see cref="NumericFieldType.DOUBLE"/> (a <c>DoubleDocValuesField</c>).
        /// <see cref="NumericFieldType.NONE"/> when the field does not support
        /// range counts. Other encodings are not read.
        /// </summary>
        NumericFieldType RangeFacetValueType { get; }

        /// <summary>
        /// Converts a range bound (usually a value of the property's type) to the
        /// value stored in <see cref="RangeFacetField"/>: a <see cref="long"/> for
        /// <see cref="NumericFieldType.INT64"/>, otherwise a <see cref="double"/>.
        /// </summary>
        object ConvertToRangeFacetValue(object value);
    }

    /// <summary>
    /// A field mapper that supplies facet values for an instance of <typeparamref name="T"/>.
    /// <see cref="DocumentMapperBase{T}"/> discovers implementations among its
    /// field mappers, including ones added with
    /// <see cref="DocumentMapperBase{T}.AddField"/>, and writes their values to the
    /// facet field when it maps a document. A mapper that supports range counts
    /// writes its own <see cref="IFacetFieldMappingInfo.RangeFacetField"/> in
    /// <see cref="IFieldMapper{T}.CopyToDocument"/>.
    /// </summary>
    public interface IFacetFieldMapper<in T> : IFacetFieldMappingInfo, IFieldMapper<T>
    {
        /// <summary>
        /// Returns the facet values (labels) for <paramref name="source"/>.
        /// Null or empty values are ignored.
        /// </summary>
        IEnumerable<string> GetFacetValues(T source);
    }
}
