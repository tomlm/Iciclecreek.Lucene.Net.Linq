using Iciclecreek.Lucene.Net.Vector;
using Lucene.Net.Index;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Search;

namespace Lucene.Net.Linq.Search
{
    /// <summary>
    /// A placeholder <see cref="Query"/> that carries the query vector and
    /// defers actual construction until an <see cref="IndexReader"/> is
    /// available at execution time. Resolves to a <see cref="VectorQuery"/>
    /// which automatically selects HNSW (KNN) or brute-force cosine
    /// similarity based on the runtime.
    /// </summary>
    internal class DeferredVectorQuery : Query
    {
        private const int DefaultK = 10;

        public DeferredVectorQuery(string field, float[] queryVector)
        {
            Field = field;
            QueryVector = queryVector;
        }

        public string Field { get; }
        public float[] QueryVector { get; }

        /// <summary>
        /// Resolves this deferred query into a real query.
        /// </summary>
        /// <param name="reader">The index reader.</param>
        /// <param name="maxResults">K for KNN — typically the query's Take() value.</param>
        /// <param name="vectorFieldInfo">Optional field mapping to read M/EfSearch from the attribute config.</param>
        public Query Resolve(IndexReader reader, int maxResults, IVectorFieldMappingInfo vectorFieldInfo = null)
        {
            var effectiveK = maxResults > 0 && maxResults < int.MaxValue ? maxResults : DefaultK;

            var options = new VectorIndexOptions
            {
                M = vectorFieldInfo?.HnswM ?? 16,
                EfSearch = vectorFieldInfo?.HnswEfSearch ?? 50,
            };
            return new VectorQuery(Field, QueryVector, effectiveK, reader, options);
        }

        public override string ToString(string field)
        {
            return $"DeferredVectorQuery(field={Field})";
        }

        public override bool Equals(object obj)
        {
            if (obj is DeferredVectorQuery other)
            {
                return Field == other.Field;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Field.GetHashCode();
        }
    }
}
