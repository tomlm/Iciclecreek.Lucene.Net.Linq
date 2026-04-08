using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Documents;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Search;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Mapping
{
    [TestFixture]
    public class DocValuesMappingTests
    {
        public class Sample
        {
            [Field(DocValues = true)]
            public string DvName { get; set; }

            [Field]
            public string PlainName { get; set; }

            [NumericField(DocValues = true)]
            public long Long { get; set; }

            [Field(DocValues = true)]
            public List<string> Tags { get; set; }

            [Field(Converter = typeof(VersionConverter))]
            public System.Version Version { get; set; }
        }

        private static ReflectionFieldMapper<Sample> Mapper(string property)
        {
            var p = typeof(Sample).GetProperty(property);
            var m = FieldMappingInfoBuilder.Build<Sample>(p);
            return (ReflectionFieldMapper<Sample>)m;
        }

        [Test]
        public void DvField_StringProperty_WritesSortedDocValuesField()
        {
            var doc = new Document();
            Mapper("DvName").CopyToDocument(new Sample { DvName = "hello" }, doc);

            var dv = doc.GetFields("DvName").OfType<SortedDocValuesField>().SingleOrDefault();
            Assert.That(dv, Is.Not.Null);
        }

        [Test]
        public void NumericField_WritesNumericDocValuesField()
        {
            var doc = new Document();
            Mapper("Long").CopyToDocument(new Sample { Long = 42 }, doc);

            var dv = doc.GetFields("Long").OfType<NumericDocValuesField>().SingleOrDefault();
            Assert.That(dv, Is.Not.Null);
        }

        [Test]
        public void PlainField_DefaultsToNoDocValuesField()
        {
            var doc = new Document();
            Mapper("PlainName").CopyToDocument(new Sample { PlainName = "hello" }, doc);

            Assert.That(doc.GetFields("PlainName").OfType<SortedDocValuesField>().Any(), Is.False);
        }

        [Test]
        public void Collection_SilentlyDowngradesDocValues()
        {
            var mapper = Mapper("Tags");
            Assert.That(mapper.DocValues, Is.False);
        }

        [Test]
        public void DvField_CreateSortField_ReturnsStringSortFieldWithoutComparerSource()
        {
            var sf = Mapper("DvName").CreateSortField(false);
            Assert.That(sf.Type, Is.EqualTo(SortFieldType.STRING));
            Assert.That(sf.ComparerSource, Is.Null);
        }

        [Test]
        public void NonDvConverterField_CreateSortField_StillUsesComparerSource()
        {
            var sf = Mapper("Version").CreateSortField(false);
            Assert.That(sf.ComparerSource, Is.Not.Null);
        }
    }
}
