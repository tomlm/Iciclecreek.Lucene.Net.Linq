using System;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;

namespace Lucene.Net.Linq.Abstractions
{
    /// <summary>
    /// Abstraction of <see cref="IndexWriter"/> to facilitate unit testing.
    /// </summary>
    public interface IIndexWriter : IDisposable
    {
        void AddDocument(Document doc);
        void DeleteDocuments(Query[] queries);
        void DeleteAll();
        void Commit();
        void Rollback();

        /// <summary>
        /// Forces a merge to a single segment. Replaces the Lucene 3.x
        /// <c>Optimize()</c> call.
        /// </summary>
        void ForceMerge(int maxNumSegments);

        /// <summary>
        /// Returns a near-real-time reader over the writer's current state.
        /// </summary>
        DirectoryReader GetReader();

        /// <summary>
        /// Whether the writer has been closed by <see cref="Dispose"/>
        /// or <see cref="Rollback"/>.
        /// </summary>
        bool IsClosed { get; }
    }

    /// <summary>
    /// Wraps an <see cref="IndexWriter"/> with an implementation of
    /// <see cref="IIndexWriter"/>.
    /// </summary>
    public class IndexWriterAdapter : IIndexWriter
    {
        private readonly IndexWriter target;
        private bool closed;

        public IndexWriterAdapter(IndexWriter target)
        {
            this.target = target;
        }

        public void DeleteAll() => target.DeleteAll();
        public void DeleteDocuments(Query[] queries) => target.DeleteDocuments(queries);
        public void Commit() => target.Commit();
        public void AddDocument(Document doc) => target.AddDocument(doc);

        public void Dispose()
        {
            closed = true;
            target.Dispose();
        }

        public void ForceMerge(int maxNumSegments) => target.ForceMerge(maxNumSegments);

        public void Rollback()
        {
            closed = true;
            target.Rollback();
        }

        public DirectoryReader GetReader() => DirectoryReader.Open(target, applyAllDeletes: true);

        public bool IsClosed => closed;
    }
}
