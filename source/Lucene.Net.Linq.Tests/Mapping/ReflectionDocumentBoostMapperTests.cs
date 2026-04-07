using Lucene.Net.Documents;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Search;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Mapping
{
    [TestFixture]
    public class ReflectionDocumentBoostMapperTests
    {
        private ReflectionDocumentBoostMapper<Sample> mapper;

        private Document document;
        private Sample sample;

        public class Sample
        {
            [DocumentBoost]
            public float Boost { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            document = new Document();
            sample = new Sample();
        }

        [Test]
        [Ignore("Document-level boost was removed in Lucene.Net 4.8; CopyToDocument is now a no-op.")]
        public void CopyToDocument()
        {
            mapper = new ReflectionDocumentBoostMapper<Sample>(typeof(Sample).GetProperty("Boost"));
            sample.Boost = 2f;

            mapper.CopyToDocument(sample, document);

            Assert.That(document.Boost, Is.EqualTo(2f));
        }
    }
}