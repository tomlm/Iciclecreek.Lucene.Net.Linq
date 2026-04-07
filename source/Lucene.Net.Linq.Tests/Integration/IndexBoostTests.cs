using System;
using System.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Linq.Mapping;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    [TestFixture]
    public class IndexBoostTests : IntegrationTestBase
    {
        public class BoostDocument
        {
            [Field(Analyzer = typeof(KeywordAnalyzer), Boost = 2f)]
            public string Title { get; set; }

            [Field(Analyzer = typeof(KeywordAnalyzer))]
            public string Body { get; set; }

            [QueryScore]
            public float Score { get; set; }
        }

        [Test]
        public void NormalFieldBoost()
        {
            AddDocument(new BoostDocument { Title = "car", Body = "truck" });
            AddDocument(new BoostDocument { Title = "truck", Body = "auto" });

            var result = from doc in provider.AsQueryable<BoostDocument>()
                         where doc.Body == "truck" || doc.Title == "truck"
                         select doc;

            Assert.That(result.First().Title, Is.EqualTo("truck"));
            Assert.That(result.OrderByDescending(doc => doc.Score()).First().Title, Is.EqualTo("car"));
        }
    }
}