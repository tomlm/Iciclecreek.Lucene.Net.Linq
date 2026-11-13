using System;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Microsoft.Extensions.AI;

namespace Lucene.Net.Linq
{
    /// <summary>
    /// Holds configuration settings that specify how the library behaves.
    /// </summary>
    public class LuceneDataProviderSettings
    {
        public LuceneDataProviderSettings()
        {
            DeletionPolicy = new KeepOnlyLastCommitDeletionPolicy();
            RAMBufferSizeMB = IndexWriterConfig.DEFAULT_RAM_BUFFER_SIZE_MB;
            EnableJoinPushdown = true;
            VectorExactSearchLimit = 10000;
        }

        /// <summary>
        /// When <c>true</c> (the default), a LINQ join whose result uses only one
        /// of its two sides is executed inside Lucene as a query-time join
        /// (<c>Lucene.Net.Join</c>), so the other side's documents are never
        /// loaded and paging, sorting and counting run in the index. Joins that
        /// don't qualify are joined in memory. Set to <c>false</c> to always
        /// join in memory; results are the same either way.
        /// </summary>
        public bool EnableJoinPushdown { get; set; }

        /// <summary>
        /// Receives a message per join saying whether it was pushed into Lucene
        /// or why it was not, or a single message when the whole query is ruled
        /// out (pushdown disabled, a group join, a subquery...). For tests.
        /// </summary>
        internal Action<string> JoinPushdownTrace { get; set; }

        /// <summary>
        /// Specifies the <see cref="IndexDeletionPolicy"/> of the <see cref="IndexWriter"/>.
        /// Default: <see cref="KeepOnlyLastCommitDeletionPolicy"/>.
        /// </summary>
        public IndexDeletionPolicy DeletionPolicy { get; set; }

        /// <summary>
        /// Specifies the RAM buffer size of the <see cref="IndexWriter"/> in megabytes.
        /// Default: <see cref="IndexWriterConfig.DEFAULT_RAM_BUFFER_SIZE_MB"/>.
        /// </summary>
        public double RAMBufferSizeMB { get; set; }

        /// <summary>
        /// A factory that produces a <see cref="MergePolicy"/> for use with
        /// <see cref="IndexWriter"/>. Default: <c>null</c>, which causes
        /// <see cref="IndexWriter"/> to use its default policy.
        /// </summary>
        /// <remarks>
        /// The Lucene 3.x version of this delegate took the in-progress
        /// <c>IndexWriter</c> as an argument so callers could inspect its
        /// state. Lucene 4.8 requires the merge policy to be set on the
        /// <see cref="IndexWriterConfig"/> before the writer is constructed,
        /// so the writer is no longer available to the factory.
        /// </remarks>
        public Func<MergePolicy> MergePolicyBuilder { get; set; }

        /// <summary>
        /// An embedding generator used to convert text into vector embeddings
        /// for properties marked with <see cref="Mapping.VectorFieldAttribute"/>.
        /// When set, text is automatically embedded at index time and queries
        /// using <see cref="LuceneMethods.Similar"/> generate embeddings at query time.
        /// </summary>
        public IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator { get; set; }

        /// <summary>
        /// Decides how a query that is only a <see cref="LuceneMethods.Similar"/>
        /// call finds its nearest documents. The search filter (the document type,
        /// joins run inside Lucene) is applied first. When it leaves no more than
        /// this many documents, each of them is compared with the query vector:
        /// exact, and no vector index is built. Above the limit the approximate
        /// (HNSW) index is searched instead, where the runtime has one.
        /// Default 10,000. Does not apply when Similar is combined with other
        /// conditions: those always select the documents to compare.
        /// </summary>
        public int VectorExactSearchLimit { get; set; }
    }
}
