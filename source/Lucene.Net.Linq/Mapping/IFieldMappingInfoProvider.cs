using System;
using System.Collections.Generic;
using Lucene.Net.Analysis;
using Lucene.Net.Search;

namespace Lucene.Net.Linq.Mapping
{
    /// <summary>
    /// Provides mapping information for the properties
    /// of a given type and corresponding field metadata.
    /// </summary>
    public interface IFieldMappingInfoProvider
    {
        /// <summary>
        /// Returns the CLR type that this provider maps documents to.
        /// </summary>
        Type MappedType { get; }

        /// <summary>
        /// Returns the set of fields defined for the given document.
        /// </summary>
        IEnumerable<string> AllProperties { get; }

        /// <summary>
        /// Returns the set of property names used to compose
        /// a <see cref="IDocumentKey"/> for the document.
        /// </summary>
        IEnumerable<string> KeyProperties { get; }

        /// <summary>
        /// Returns the set of fields defined for the given document.
        /// </summary>
        IEnumerable<string> IndexedProperties { get; }

        /// <summary>
        /// The default property for queries that don't specify which field to search.
        /// Used by <see cref="LuceneMethods.Similar{T}(T, string)"/> for object-level
        /// vector similarity and by <see cref="FieldMappingQueryParser{T}"/> for free-text queries.
        /// </summary>
        string DefaultSearchProperty { get; }

        /// <summary>
        /// Returns detailed mapping info for a given property name.
        /// Property names are case sensitive.
        /// </summary>
        /// <exception cref="KeyNotFoundException">if the property name is not found.</exception>
        IFieldMappingInfo GetMappingInfo(string propertyName);

        /// <summary>
        /// Create a query that matches the pattern on any field.
        /// Used in conjunction with <see cref="LuceneMethods.AnyField{T}"/>
        /// </summary>
        Query CreateMultiFieldQuery(string pattern);
    }
}
