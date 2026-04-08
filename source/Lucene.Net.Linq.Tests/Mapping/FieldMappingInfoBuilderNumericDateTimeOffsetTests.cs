using System;
using System.Reflection;
using Lucene.Net.Documents;
using Lucene.Net.Linq.Mapping;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Mapping
{
    [TestFixture]
    public class FieldMappingInfoBuilderNumericDateTimeOffsetTests
    {
        private PropertyInfo info;

        [NumericField]
        public DateTimeOffset? TimeStampOffset { get; set; }

        [SetUp]
        public void SetUp()
        {
            info = GetType().GetProperty("TimeStampOffset");
        }

        [Test]
        public void CopyToDocument()
        {
            TimeStampOffset = new DateTimeOffset(new DateTime(2012, 4, 23), TimeSpan.Zero);

            var mapper = CreateMapper();

            var doc = new Document();

            mapper.CopyToDocument(this, doc);

            // Stage 5 port: numeric typed-field internal representation
            // (TokenStreamValue) is no longer exposed; assert on the
            // numeric value directly.
            Assert.That(doc.GetField("TimeStampOffset").GetInt64Value(), Is.EqualTo(TimeStampOffset.Value.UtcTicks));
        }

        [Test]
        public void CopyFromDocument()
        {
            var mapper = CreateMapper();

            var doc = new Document();

            var ts = new DateTimeOffset(new DateTime(2013, 1, 1));
            doc.Add(new Int64Field("TimeStampOffset", ts.ToUniversalTime().Ticks, Field.Store.YES));

            mapper.CopyFromDocument(doc, new QueryExecutionContext(), this);

            Assert.That(TimeStampOffset, Is.EqualTo(ts));
        }

        private IFieldMapper<FieldMappingInfoBuilderNumericDateTimeOffsetTests> CreateMapper()
        {
            return FieldMappingInfoBuilder.Build<FieldMappingInfoBuilderNumericDateTimeOffsetTests>(info);
        }

    }
}