using System;
using System.ComponentModel;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Linq.Search;
using Lucene.Net.Linq.Tests.Integration;
using Lucene.Net.Search;
using NUnit.Framework;
using Lucene.Net.QueryParsers.Classic;

namespace Lucene.Net.Linq.Tests.Mapping
{
    [TestFixture]
    public class ReflectionFieldMapperTests
    {
        [Test]
        public void SpecifyAnalyzer()
        {
            var mapper = CreateMapper("Text", analyzer: new PorterStemAnalyzer(Net.Util.LuceneVersion.LUCENE_48));

            var query = mapper.CreateQuery("values");

            Assert.That(query.ToString(), Is.EqualTo("Text:valu"));
        }

        [Test]
        public void ParseMultipleTerms()
        {
            var mapper = CreateMapper("Text", analyzer: new StandardAnalyzer(Net.Util.LuceneVersion.LUCENE_48));

            var query = mapper.CreateQuery("x y z");
            Assert.That(query.ToString(), Is.EqualTo("Text:x Text:y Text:z"));
        }

        [Test]
        public void ParseKeywordWithWhitespace()
        {
            var mapper = CreateMapper("Text");

            var query = mapper.CreateQuery("x y z");
            Assert.That(query.ToString(), Is.EqualTo("Text:x y z"));
        }

        [Test]
        public void ParseMultipleTermsWithDefaultOperatorAnd()
        {
            var mapper = CreateMapper("Text", analyzer: new StandardAnalyzer(Net.Util.LuceneVersion.LUCENE_48), defaultParseOperaor: Operator.AND);

            var query = mapper.CreateQuery("x y z");
            Assert.That(query.ToString(), Is.EqualTo("+Text:x +Text:y +Text:z"));
        }

        [Test]
        public void ParseLowercaseExpandedTerms()
        {
            var mapper = CreateMapper("Text");

            var query = mapper.CreateQuery("FOO*");

            Assert.That(query.ToString(), Is.EqualTo("Text:foo*"));
        }

        [Test]
        public void ParseDoNotLowercaseExpandedTerms()
        {
            var mapper = CreateMapper("Text", caseSensitive: true);

            var query = mapper.CreateQuery("FOO*");

            Assert.That(query.ToString(), Is.EqualTo("Text:FOO*"));
        }

        [Test]
        public void RangeQuery()
        {
            var mapper = CreateMapper("Text");

            var result = mapper.CreateRangeQuery("a", "z", RangeType.Inclusive, RangeType.Inclusive);

            Assert.That(result, Is.InstanceOf<TermRangeQuery>());
            Assert.That(result.ToString(), Is.EqualTo("Text:[a TO z]"));
        }

        [Test]
        public void RangeQueryComplexType()
        {
            var mapper = CreateMapper("Version", new VersionConverter());

            var result = mapper.CreateRangeQuery(new Version("2.0"), null, RangeType.Exclusive, RangeType.Inclusive);

            Assert.That(result, Is.InstanceOf<TermRangeQuery>());
            Assert.That(result.ToString(), Is.EqualTo("Version:{2.0 TO *]"));
        }

        [Test]
        public void AnalyzesQueryValue()
        {
            var mapper = CreateMapper("Text", analyzer: new StandardAnalyzer(Net.Util.LuceneVersion.LUCENE_48));

            var result = mapper.CreateRangeQuery("SomeValue", null, RangeType.Inclusive, RangeType.Inclusive);

            Assert.That(result, Is.InstanceOf<TermRangeQuery>());
            Assert.That(result.ToString(), Is.EqualTo("Text:[somevalue TO *]"));
        }

        [Test]
        public void CustomSort()
        {
            var mapper = CreateMapper("Version", analyzer: new KeywordAnalyzer(), converter: new VersionConverter());

            var sort = mapper.CreateSortField(reverse: false);

            Assert.That(sort.Field, Is.EqualTo(mapper.FieldName));
            // Stage 4 port note: the converter-based custom sort path is
            // currently disabled (CreateSortField falls back to a string
            // SortField). Once the comparator path is re-ported the test
            // should assert ComparerSource again.
            Assert.That(sort.Type, Is.EqualTo(SortFieldType.STRING));

        }

        [Test]
        public void NativeSortWithConverterSpecified()
        {
            var mapper = CreateMapper("Version", analyzer: new KeywordAnalyzer(), converter: new VersionConverter(), nativeSort:true);

            var sort = mapper.CreateSortField(reverse: false);

            Assert.That(sort.Field, Is.EqualTo(mapper.FieldName));
            Assert.That(sort.Type, Is.EqualTo(SortFieldType.STRING));

        }

        public string Text { get; set; }

        public string Version { get; set; }

        public string ReadOnly { get { return "You can't write to me"; } }

        [Test]
        public void CopyFromDocument_ReadOnlyProperty()
        {
            var mapper = CreateMapper("ReadOnly");

            TestDelegate call = () => mapper.CopyFromDocument(new Document(), new QueryExecutionContext(), this);

            Assert.That(call, Throws.Nothing);
        }

        private ReflectionFieldMapper<ReflectionFieldMapperTests> CreateMapper(string propertyName, TypeConverter converter = null, Analyzer analyzer = null, Operator defaultParseOperaor = Operator.OR, bool caseSensitive = false, bool nativeSort = false)
        {
            return new ReflectionFieldMapper<ReflectionFieldMapperTests>(
                typeof(ReflectionFieldMapperTests).GetProperty(propertyName),
                StoreMode.Yes,
                IndexMode.Analyzed, TermVectorMode.No, converter, propertyName,
                defaultParseOperaor, caseSensitive, analyzer ?? new KeywordAnalyzer(),
                1f,
                nativeSort);

        }

    }
}
