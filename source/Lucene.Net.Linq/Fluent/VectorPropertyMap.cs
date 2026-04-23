using System.Reflection;
using Lucene.Net.Linq.Mapping;
using Microsoft.Extensions.AI;

namespace Lucene.Net.Linq.Fluent
{
    /// <summary>
    /// Extends <see cref="PropertyMap{T}"/> to configure a property
    /// as a vector embedding field for KNN similarity search.
    /// See <see cref="PropertyMap{T}.AsVectorField"/>.
    /// </summary>
    public class VectorPropertyMap<T> : PropertyMap<T>
    {
        private int k = 10;
        private int m = 16;
        private int efSearch = 50;
        private IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;

        internal VectorPropertyMap(ClassMap<T> classMap, PropertyInfo propInfo, PropertyMap<T> copy)
            : base(classMap, propInfo, copy)
        {
        }

        /// <summary>
        /// Set the number of nearest neighbors to return. Default: 10.
        /// </summary>
        public VectorPropertyMap<T> WithK(int k)
        {
            this.k = k;
            return this;
        }

        /// <summary>
        /// Set the HNSW M parameter (max edges per node). Default: 16.
        /// </summary>
        public VectorPropertyMap<T> WithM(int m)
        {
            this.m = m;
            return this;
        }

        /// <summary>
        /// Set the HNSW EfSearch parameter (search quality). Default: 50.
        /// </summary>
        public VectorPropertyMap<T> WithEfSearch(int efSearch)
        {
            this.efSearch = efSearch;
            return this;
        }

        /// <summary>
        /// Set a per-field embedding generator, overriding the one on
        /// <see cref="LuceneDataProviderSettings"/>.
        /// </summary>
        public VectorPropertyMap<T> WithEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>> generator)
        {
            this.embeddingGenerator = generator;
            return this;
        }

        protected internal override IFieldMapper<T> ToFieldMapper()
        {
            var inner = base.ToFieldMapperInternal();
            return new VectorFieldMapper<T>(inner, embeddingGenerator, k, m, efSearch);
        }
    }
}
