using System;
using System.Linq;
using Lucene.Net.Linq.Mapping;
using Microsoft.Extensions.AI;
using NUnit.Framework;

#if NET8_0_OR_GREATER
using ElBruno.LocalEmbeddings;
using ElBruno.LocalEmbeddings.Options;
#endif

namespace Lucene.Net.Linq.Tests.Integration
{
    [DocumentKey(FieldName = "Type", Value = "VectorDoc")]
    public class VectorDocument
    {
        [Field(Key = true)]
        public string Id { get; set; }

        [Field, VectorField]
        public string Title { get; set; }

        [Field]
        public string Category { get; set; }
    }

    [TestFixture]
    public class VectorFieldMappingTests : IntegrationTestBase
    {
        private static readonly IEmbeddingGenerator<string, Embedding<float>> generator =
#if NET8_0_OR_GREATER
        new LocalEmbeddingGenerator(new LocalEmbeddingsOptions
        {
            ModelName = "SmartComponents/bge-micro-v2",
            PreferQuantized = true
        });
#else
        null;
#endif

        [SetUp]
        public override void InitializeLucene()
        {
            base.InitializeLucene();
            provider.Settings.EmbeddingGenerator = generator;
        }

        [Test]
        public void VectorFieldAttribute_IsDetectedByMapper()
        {
            var mapper = new ReflectionDocumentMapper<VectorDocument>(
                Lucene.Net.Util.LuceneVersion.LUCENE_48, null, generator);

            var info = mapper.GetMappingInfo("Title");
            Assert.That(info, Is.Not.Null);
            Assert.That(info, Is.InstanceOf<VectorFieldMapper<VectorDocument>>());
        }

        [Test]
        public void VectorFieldMapper_VectorFieldName_AppendsSuffix()
        {
            var mapper = new ReflectionDocumentMapper<VectorDocument>(
                Lucene.Net.Util.LuceneVersion.LUCENE_48, null, generator);

            var info = mapper.GetMappingInfo("Title") as VectorFieldMapper<VectorDocument>;
            Assert.That(info, Is.Not.Null);
            Assert.That(info.VectorFieldName, Is.EqualTo("Title_vector"));
        }

        [Test]
        public void AddDocument_WritesVectorField()
        {
            AddDocument(new VectorDocument { Id = "1", Title = "hello world" });

            using var handle = provider.Context.CheckoutSearcher();
            var searcher = handle.Searcher;
            var hits = searcher.Search(new Lucene.Net.Search.MatchAllDocsQuery(), 10);
            Assert.That(hits.TotalHits, Is.GreaterThan(0));

            var doc = searcher.Doc(hits.ScoreDocs[0].Doc);
#if NET8_0_OR_GREATER
            var vectorBytes = doc.GetBinaryValue("Title_vector");
            Assert.That(vectorBytes, Is.Not.Null, "Expected Title_vector StoredField to be present");
            Assert.That(vectorBytes.Length, Is.GreaterThan(0));
#endif
        }

        [Test]
        public void AddDocument_StringFieldStillReadable()
        {
            AddDocument(new VectorDocument { Id = "1", Title = "hello world", Category = "test" });

            var results = provider.AsQueryable<VectorDocument>().ToList();
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Title, Is.EqualTo("hello world"));
            Assert.That(results[0].Category, Is.EqualTo("test"));
        }

        [Test]
        public void AddDocument_NullTitle_NoVectorWritten()
        {
            AddDocument(new VectorDocument { Id = "1", Title = null, Category = "test" });

            using var handle = provider.Context.CheckoutSearcher();
            var searcher = handle.Searcher;
            var hits = searcher.Search(new Lucene.Net.Search.MatchAllDocsQuery(), 10);
            var doc = searcher.Doc(hits.ScoreDocs[0].Doc);
            var vectorBytes = doc.GetBinaryValue("Title_vector");
            Assert.That(vectorBytes, Is.Null, "No vector should be written for null text");
        }

        [Test]
        public void MultipleDocuments_AllHaveVectors()
        {
            AddDocument(new VectorDocument { Id = "1", Title = "first document" });
            AddDocument(new VectorDocument { Id = "2", Title = "second document" });
            AddDocument(new VectorDocument { Id = "3", Title = "third document" });

            using var handle = provider.Context.CheckoutSearcher();
            var searcher = handle.Searcher;
            var hits = searcher.Search(new Lucene.Net.Search.MatchAllDocsQuery(), 10);
            Assert.That(hits.TotalHits, Is.EqualTo(3));
#if NET8_0_OR_GREATER
            for (int i = 0; i < hits.ScoreDocs.Length; i++)
            {
                var doc = searcher.Doc(hits.ScoreDocs[i].Doc);
                var vectorBytes = doc.GetBinaryValue("Title_vector");
                Assert.That(vectorBytes, Is.Not.Null, $"Document {i} should have a vector");
            }
#endif
        }
    }

    [TestFixture]
    public class VectorFluentMappingTests
    {
        [Test]
        public void AsVectorField_CreatesVectorPropertyMap()
        {
            var classMap = new Lucene.Net.Linq.Fluent.ClassMap<VectorDocument>(Lucene.Net.Util.LuceneVersion.LUCENE_48);
            var propertyMap = classMap.Property(x => x.Title);
            var vectorMap = propertyMap.AsVectorField();

            Assert.That(vectorMap, Is.InstanceOf<Lucene.Net.Linq.Fluent.VectorPropertyMap<VectorDocument>>());
        }

        [Test]
        public void AsVectorField_ToFieldMapper_ReturnsVectorFieldMapper()
        {
            var classMap = new Lucene.Net.Linq.Fluent.ClassMap<VectorDocument>(Lucene.Net.Util.LuceneVersion.LUCENE_48);
            classMap.Property(x => x.Id).NotAnalyzed();
            classMap.Property(x => x.Title).AsVectorField().WithK(5);

            var mapper = classMap.ToDocumentMapper();
            var info = mapper.GetMappingInfo("Title");
            Assert.That(info, Is.InstanceOf<VectorFieldMapper<VectorDocument>>());

            var vectorMapper = (VectorFieldMapper<VectorDocument>)info;
            Assert.That(vectorMapper.K, Is.EqualTo(5));
        }

        [Test]
        public void AsVectorField_Chaining_PreservesSettings()
        {
            var classMap = new Lucene.Net.Linq.Fluent.ClassMap<VectorDocument>(Lucene.Net.Util.LuceneVersion.LUCENE_48);
            classMap.Property(x => x.Id).NotAnalyzed();
            classMap.Property(x => x.Title)
                .AsVectorField()
                .WithK(20)
                .WithM(32)
                .WithEfSearch(100);

            var mapper = classMap.ToDocumentMapper();
            var vectorMapper = (VectorFieldMapper<VectorDocument>)mapper.GetMappingInfo("Title");
            Assert.That(vectorMapper.K, Is.EqualTo(20));
            Assert.That(vectorMapper.HnswM, Is.EqualTo(32));
            Assert.That(vectorMapper.HnswEfSearch, Is.EqualTo(100));
        }

        [Test]
        public void AsVectorField_CalledTwice_ReturnsSameInstance()
        {
            var classMap = new Lucene.Net.Linq.Fluent.ClassMap<VectorDocument>(Lucene.Net.Util.LuceneVersion.LUCENE_48);
            var first = classMap.Property(x => x.Title).AsVectorField();
            var second = first.AsVectorField();
            Assert.That(second, Is.SameAs(first));
        }
    }

    [TestFixture]
    [NonParallelizable]
    public class VectorSimilaritySearchTests
    {
        private static readonly IEmbeddingGenerator<string, Embedding<float>> generator =
#if NET8_0_OR_GREATER
            new LocalEmbeddingGenerator(new LocalEmbeddingsOptions
        {
            ModelName = "SmartComponents/bge-micro-v2",
            PreferQuantized = true
        });
#else
        null;
#endif
        private LuceneDataProvider provider;
        private Lucene.Net.Store.RAMDirectory directory;
        private Lucene.Net.Index.IndexWriter writer;

        [OneTimeSetUp]
        public void SetupVectorIndex()
        {
            directory = new Lucene.Net.Store.RAMDirectory();
            writer = new Lucene.Net.Index.IndexWriter(directory,
                new Lucene.Net.Index.IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_48,
                    new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48)));
            provider = new LuceneDataProvider(directory, Lucene.Net.Util.LuceneVersion.LUCENE_48, writer);
            provider.Settings.EmbeddingGenerator = generator;

            AddDoc(new VectorDocument { Id = "1", Title = "the quick brown fox jumps over the lazy dog", Category = "animals" });
            AddDoc(new VectorDocument { Id = "2", Title = "a small kitten sleeping on a warm blanket", Category = "animals" });
            AddDoc(new VectorDocument { Id = "3", Title = "quantum physics and string theory research", Category = "science" });
            AddDoc(new VectorDocument { Id = "4", Title = "the big friendly bear eats honey in the forest", Category = "animals" });
            AddDoc(new VectorDocument { Id = "5", Title = "machine learning and neural network training", Category = "science" });
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            writer?.Dispose();
            directory?.Dispose();
        }

        private IQueryable<VectorDocument> Documents => provider.AsQueryable<VectorDocument>();

        private void AddDoc(VectorDocument doc)
        {
            using (var session = provider.OpenSession<VectorDocument>())
            {
                session.Add(doc);
                session.Commit();
            }
        }

        [Test]
        public void Similar_ReturnsResults()
        {
            var query = Documents
                .Where(d => d.Title.Similar("a cute cat napping"));
#if NET8_0_OR_GREATER
            var result = query.ToList();
            Assert.That(result, Is.Not.Empty, "Similar() should return results");
#else
            Assert.Throws<InvalidOperationException>(() => query.ToList());
#endif
        }

        [Test]
        public void Similar_RanksSemanticallySimilarHigher()
        {
            // "a cute cat napping" should be most similar to "a small kitten sleeping on a warm blanket"
            var query = Documents
                .Where(d => d.Title.Similar("a cute cat napping"));
#if NET8_0_OR_GREATER
            var result = query.ToList();
            Assert.That(result, Is.Not.Empty);
            Assert.That(result[0].Id, Is.EqualTo("2"),
                "The kitten sleeping document should rank first for 'a cute cat napping'");
#else
            Assert.Throws<InvalidOperationException>(() => query.ToList());
#endif
        }

        [Test]
        public void Similar_ScienceQueryRanksScienceHigher()
        {
            var query = Documents
                .Where(d => d.Title.Similar("deep learning artificial intelligence"));
#if NET8_0_OR_GREATER
            var result = query.ToList();
            Assert.That(result, Is.Not.Empty);
            Assert.That(result[0].Category, Is.EqualTo("science"),
                "Science query should rank science documents higher");
#else
            Assert.Throws<InvalidOperationException>(() => query.ToList());
#endif
        }

        [Test]
        public void Similar_WithTake()
        {
            var query = Documents
                .Where(d => d.Title.Similar("animals"))
                .Take(2);
#if NET8_0_OR_GREATER
            var result = query.ToList();
            Assert.That(result.Count, Is.LessThanOrEqualTo(2));
#else
            Assert.Throws<InvalidOperationException>(() => query.ToList());
#endif
        }

        [Test]
        public void Similar_WithoutTake_DefaultsTo10()
        {
            // We only have 5 documents, so all should be returned
            var query = Documents
                .Where(d => d.Title.Similar("test query"));
#if NET8_0_OR_GREATER
            var result = query.ToList();
            Assert.That(result.Count, Is.EqualTo(5));
#else
            Assert.Throws<InvalidOperationException>(() => query.ToList());
#endif
        }

        [Test]
        public void Similar_CombinedWithTextFilter()
        {
            // Hybrid query: filter first, then rank by vector similarity
            var query = Documents
                .Where(d => d.Title.Similar("furry animals in nature") && d.Category == "animals");
#if NET8_0_OR_GREATER
            var result = query.ToList();
            Assert.That(result, Is.Not.Empty);
            Assert.That(result.All(d => d.Category == "animals"), Is.True,
                "All results should match the category filter");
#else
            Assert.Throws<InvalidOperationException>(() => query.ToList());
#endif
        }

        [Test]
        public void Similar_CombinedWithTextFilter_AndTake()
        {
            var query = Documents
                .Where(d => d.Title.Similar("furry animals in nature") && d.Category == "animals")
                .Take(2);
#if NET8_0_OR_GREATER
            var result = query.ToList();
            Assert.That(result.Count, Is.LessThanOrEqualTo(2));
            Assert.That(result.All(d => d.Category == "animals"), Is.True);
#else
            Assert.Throws<InvalidOperationException>(() => query.ToList());
#endif
        }

        [Test]
        public void Similar_WithoutEmbeddingGenerator_ThrowsInvalidOperationException()
        {
            // Use a separate provider without embedding generator
            using var dir = new Lucene.Net.Store.RAMDirectory();
            using var w = new Lucene.Net.Index.IndexWriter(dir,
                new Lucene.Net.Index.IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_48,
                    new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48)));
            var p = new LuceneDataProvider(dir, Lucene.Net.Util.LuceneVersion.LUCENE_48, w);
            // Explicitly no embedding generator
            var docs = p.AsQueryable<VectorDocument>();

            Assert.Throws<InvalidOperationException>(() =>
                docs.Where(d => d.Title.Similar("test")).ToList());
        }

        [Test]
        public void Similar_NullQueryText_ThrowsInvalidOperationException()
        {
#if NET8_0_OR_GREATER
            string nullText = null;
            Assert.Throws<InvalidOperationException>(() =>
                Documents.Where(d => d.Title.Similar(nullText)).ToList());
#endif
        }

        [Test]
        public void Similar_EmptyQueryText_ThrowsInvalidOperationException()
        {
#if NET8_0_OR_GREATER
            Assert.Throws<InvalidOperationException>(() =>
                Documents.Where(d => d.Title.Similar("")).ToList());
#endif
        }

        [Test]
        public void Similar_AllDocumentsRetrievable()
        {
            // Verify that documents indexed with vectors can still be queried normally
            var allDocs = Documents.ToList();
            Assert.That(allDocs.Count, Is.EqualTo(5));

            var doc1 = allDocs.First(d => d.Id == "1");
            Assert.That(doc1.Title, Is.EqualTo("the quick brown fox jumps over the lazy dog"));
        }
    }

    [TestFixture]
    public class VectorFieldAttributeTests
    {
        [Test]
        public void DefaultValues()
        {
            var attr = new VectorFieldAttribute();
            Assert.That(attr.K, Is.EqualTo(10));
            Assert.That(attr.M, Is.EqualTo(16));
            Assert.That(attr.EfSearch, Is.EqualTo(50));
        }

        [Test]
        public void CustomValues()
        {
            var attr = new VectorFieldAttribute { K = 20, M = 32, EfSearch = 100 };
            Assert.That(attr.K, Is.EqualTo(20));
            Assert.That(attr.M, Is.EqualTo(32));
            Assert.That(attr.EfSearch, Is.EqualTo(100));
        }
    }
}
