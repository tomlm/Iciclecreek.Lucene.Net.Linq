using System;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Linq.Abstractions;
using Lucene.Net.Search;
using Lucene.Net.Store;
using NUnit.Framework;
using LuceneVersion = Lucene.Net.Util.LuceneVersion;

namespace Lucene.Net.Linq.Tests
{
    [TestFixture]
    public class ContextTests
    {
        // Stage 5 port: the original tests mocked IndexReader and IndexSearcher
        // via Rhino.Mocks to verify Dispose semantics. In Lucene.Net 4.8 those
        // types are heavily abstract and IndexSearcher is no longer IDisposable;
        // mocking them is impractical. We now drive Context against a real
        // RAMDirectory and assert observable behaviour: handle counts, reload
        // semantics. Disposal-by-mock tests are removed; the underlying Dispose
        // path is exercised by integration tests.

        private Context context;
        private static readonly Directory directory = new RAMDirectory();

        [SetUp]
        public void SetUp()
        {
            // Ensure the index has at least one segment so DirectoryReader.Open works.
            using (var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, new StandardAnalyzer(LuceneVersion.LUCENE_48))))
            {
                writer.Commit();
            }

            context = new Context(directory, new object());
        }

        [Test]
        public void SearchHandleCreatesNew()
        {
            var handle = context.CheckoutSearcher();

            Assert.That(handle.Searcher, Is.Not.Null);
        }

        [Test]
        public void SearchHandleRetainsInstance()
        {
            var handle = context.CheckoutSearcher();

            var s1 = handle.Searcher;
            context.Reload();
            var s2 = handle.Searcher;

            Assert.That(s2, Is.SameAs(s1));
        }

        [Test]
        public void SearcherInstanceDoesNotChangeWhenIndexReaderNotReloaded()
        {
            var s1 = context.CheckoutSearcher().Searcher;

            context.Reload();

            var s2 = context.CheckoutSearcher().Searcher;
            Assert.That(s2, Is.SameAs(s1), "Searcher instance after Reload() with no index changes");
        }

        [Test]
        public void DisposeHandleThrowsWhenAlreadyDisposed()
        {
            var handle = context.CheckoutSearcher();

            handle.Dispose();

            Assert.Throws<ObjectDisposedException>(handle.Dispose);
        }

        [Test]
        public void TwoHandles()
        {
            var h1 = context.CheckoutSearcher();
            var h2 = context.CheckoutSearcher();

            Assert.That(context.CurrentTracker.ReferenceCount, Is.EqualTo(2));

            h1.Dispose();

            Assert.That(context.CurrentTracker.ReferenceCount, Is.EqualTo(1));

            h2.Dispose();

            Assert.That(context.CurrentTracker.ReferenceCount, Is.EqualTo(0));
        }

        [Test]
        public void ReloadFiresLoadingEvent()
        {
            // Force a write so OpenIfChanged returns a new reader.
            using (var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, new StandardAnalyzer(LuceneVersion.LUCENE_48))))
            {
                writer.AddDocument(new Document());
                writer.Commit();
            }

            // Prime the existing reader.
            context.CheckoutSearcher().Dispose();

            // Add another doc to ensure OpenIfChanged returns non-null.
            using (var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, new StandardAnalyzer(LuceneVersion.LUCENE_48))))
            {
                writer.AddDocument(new Document());
                writer.Commit();
            }

            IndexSearcher next = null;
            context.SearcherLoading += (e, x) => { next = x.IndexSearcher; };
            context.Reload();

            Assert.That(next, Is.Not.Null, "Should fire loading event with non-null new searcher");
        }

        [Test]
        public void ReloadFromAWriter_SeesWhatItHasNotCommitted()
        {
            var own = new RAMDirectory();
            var ownContext = new Context(own, new object());
            using var writer = new IndexWriter(own, new IndexWriterConfig(LuceneVersion.LUCENE_48, new StandardAnalyzer(LuceneVersion.LUCENE_48)));
            writer.Commit();
            Assert.That(CountOf(ownContext), Is.EqualTo(0));

            writer.AddDocument(new Document());
            ownContext.Reload();
            Assert.That(CountOf(ownContext), Is.EqualTo(0), "from the directory: nothing is committed yet");

            ownContext.Reload(writer);
            Assert.That(CountOf(ownContext), Is.EqualTo(1), "from the writer: the uncommitted document");

            writer.AddDocument(new Document());
            ownContext.Reload(writer);
            Assert.That(CountOf(ownContext), Is.EqualTo(2), "reopened from the writer");
        }

        [Test]
        public void ReloadWithoutTheWriter_AfterItIsClosed_ReadsTheDirectoryAgain()
        {
            var own = new RAMDirectory();
            var ownContext = new Context(own, new object());
            var writer = new IndexWriter(own, new IndexWriterConfig(LuceneVersion.LUCENE_48, new StandardAnalyzer(LuceneVersion.LUCENE_48)));
            writer.AddDocument(new Document());
            writer.Commit();
            ownContext.Reload(writer);
            writer.AddDocument(new Document());
            writer.Commit();
            writer.Dispose();

            // A reader from a closed writer cannot be reopened from it: the directory is read instead.
            Assert.That(() => ownContext.Reload(), Throws.Nothing);
            Assert.That(CountOf(ownContext), Is.EqualTo(2));

            using (var other = new IndexWriter(own, new IndexWriterConfig(LuceneVersion.LUCENE_48, new StandardAnalyzer(LuceneVersion.LUCENE_48))))
            {
                other.AddDocument(new Document());
                other.Commit();
            }
            ownContext.Reload();
            Assert.That(CountOf(ownContext), Is.EqualTo(3), "and reopens from the directory as before");
        }

        [Test]
        public void ReloadWithoutTheWriter_BeforeAnythingIsCommitted_ReadsNothing()
        {
            var own = new RAMDirectory();
            var ownContext = new Context(own, new object());
            using var writer = new IndexWriter(own, new IndexWriterConfig(LuceneVersion.LUCENE_48, new StandardAnalyzer(LuceneVersion.LUCENE_48)));
            writer.AddDocument(new Document());
            ownContext.Reload(writer);
            Assert.That(CountOf(ownContext), Is.EqualTo(1));

            // No commit yet, and the writer holds the lock: the directory reads as empty,
            // rather than a second writer being opened to lay down an empty commit.
            Assert.That(() => ownContext.Reload(), Throws.Nothing);
            Assert.That(CountOf(ownContext), Is.EqualTo(0));
            Assert.That(() => ownContext.Reload(), Throws.Nothing, "still nothing committed");

            writer.Commit();
            ownContext.Reload();
            Assert.That(CountOf(ownContext), Is.EqualTo(1), "the commit is read from the directory");

            writer.AddDocument(new Document());
            ownContext.Reload(writer);
            Assert.That(CountOf(ownContext), Is.EqualTo(2), "and the writer can be read from again");
        }

        private static int CountOf(Context of)
        {
            using var handle = of.CheckoutSearcher();
            return handle.Searcher.IndexReader.NumDocs;
        }
    }

    public class NoOpIndexWriter : IIndexWriter
    {
        public void Dispose() { }
        public void AddDocument(Document doc) { }
        public void DeleteDocuments(Query[] queries) { }
        public void DeleteAll() { }
        public void Commit() { }
        public void Rollback() { }
        public void ForceMerge(int maxNumSegments) { }
        public DirectoryReader GetReader() => null;
        public bool IsClosed => false;
    }
}
