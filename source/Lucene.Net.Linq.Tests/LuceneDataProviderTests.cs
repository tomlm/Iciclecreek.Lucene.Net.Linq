using System;
using System.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Index;
using Lucene.Net.Linq.Abstractions;
using Lucene.Net.Linq.Analysis;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Store;
using NSubstitute;
using NUnit.Framework;
using LuceneVersion = Lucene.Net.Util.LuceneVersion;
using Version = Lucene.Net.Util.Version;

namespace Lucene.Net.Linq.Tests
{
    [TestFixture]
    public class LuceneDataProviderTests
    {
        public class Item
        {
            public int Id { get; set; }
        }

        [Test]
        public void OpenSessionWithoutWriterCreatesIndexWhenMissing()
        {
            var provider = new LuceneDataProvider(new RAMDirectory(), new SimpleAnalyzer(LuceneVersion.LUCENE_48), Version.LUCENE_29);

            TestDelegate call = () => provider.OpenSession<Item>();

            Assert.That(call, Throws.Nothing);
        }

        [Test]
        public void OpenSessionThrowsWhenDocumentMapperDoesNotImplementModificationDetector()
        {
            var provider = new LuceneDataProvider(new RAMDirectory(), new SimpleAnalyzer(LuceneVersion.LUCENE_48), Version.LUCENE_29);

            TestDelegate call = () => provider.OpenSession(Substitute.For<IDocumentMapper<Item>>());

            Assert.That(call, Throws.ArgumentException.With.Property("ParamName").EqualTo("documentMapper"));
        }

        [Test]
        public void RegisterCacheWarmingCallback()
        {
            var directory = new RAMDirectory();
            var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, new CaseInsensitiveKeywordAnalyzer()));
            // Stage 6 port: Lucene 4.8 doesn't lay down segments until the
            // first AddDocument; an empty commit gives DirectoryReader.Open
            // a segments_* file to work with.
            writer.Commit();
            var provider = new LuceneDataProvider(directory, new SimpleAnalyzer(LuceneVersion.LUCENE_48), Version.LUCENE_29, writer);

            var count = -1;

            provider.RegisterCacheWarmingCallback<Item>(q => count = q.Count());

            provider.Context.Reload();

            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void CreatesIndex()
        {
            var provider = new LuceneDataProvider(new RAMDirectory(), Version.LUCENE_30);

            Assert.That(provider.AsQueryable<A>().Count(), Is.EqualTo(0));
        }

        [Test]
        public void DisposesInternallyCreatedWriter()
        {
            var provider = new TestableLuceneDataProvider(new RAMDirectory(), Version.LUCENE_30);

            var writer = provider.IndexWriter;
            provider.Dispose();

            writer.Received().Dispose();
        }

        [Test]
        public void UsesSameWriterInstance()
        {
            var provider = new TestableLuceneDataProvider(new RAMDirectory(), Version.LUCENE_30);

            Assert.That(provider.IndexWriter, Is.SameAs(provider.IndexWriter), "provider.IndexWriter");
        }

        [Test]
        public void CreatesNewWriterAfterRollback()
        {
            var provider = new TestableLuceneDataProvider(new RAMDirectory(), Version.LUCENE_30);

            var first = provider.IndexWriter;

            first.IsClosed.Returns(true);

            var next = provider.IndexWriter;

            Assert.That(next, Is.Not.SameAs(first), "Should create new writer when current is closed.");
        }

        [Test]
        public void ThrowsWhenExternallyCreatedWriterIsClosed()
        {
            var writer = Substitute.For<IIndexWriter>();
            var provider = new LuceneDataProvider(new RAMDirectory(), Version.LUCENE_30, writer, new object());

            writer.IsClosed.Returns(true);

            TestDelegate call = () => provider.IndexWriter.ToString();

            Assert.That(call, Throws.InvalidOperationException);
        }

        [Test]
        public void DoesNotDisposeExternallyProvidesWriter()
        {
            var writer = Substitute.For<IIndexWriter>();
            var provider = new LuceneDataProvider(new RAMDirectory(), new KeywordAnalyzer(), Version.LUCENE_30, writer, new object());

            provider.Dispose();

            writer.DidNotReceive().Dispose();
        }

        public class TestableLuceneDataProvider : LuceneDataProvider
        {
            private readonly IIndexWriter[] writers = { null };

            public TestableLuceneDataProvider(Directory directory, Version version) : base(directory, version)
            {
            }

            protected override IIndexWriter GetIndexWriter(Analyzer analyzer)
            {
                return Substitute.For<IIndexWriter>();
            }
        }

        [Test]
        public void MergesAnalyzersForSessionsOfDifferentTypes()
        {
            var provider = new LuceneDataProvider(new RAMDirectory(), Version.LUCENE_30);

            provider.OpenSession<A>();
            provider.OpenSession<B>();

            Assert.That(provider.Analyzer["Prop1"], Is.InstanceOf<SimpleAnalyzer>());
            Assert.That(provider.Analyzer["Prop2"], Is.InstanceOf<WhitespaceAnalyzer>());
        }

        public class A
        {
            [Field(Analyzer = typeof(SimpleAnalyzer))]
            public string Prop1 { get; set; }
        }

        public class B
        {
            [Field(Analyzer = typeof(WhitespaceAnalyzer))]
            public string Prop2 { get; set; }
        }
    }
}
