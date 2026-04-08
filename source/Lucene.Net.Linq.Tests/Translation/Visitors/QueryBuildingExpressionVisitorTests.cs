using System;
using System.Linq.Expressions;
using Lucene.Net.Analysis;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Search;
using Lucene.Net.Linq.Tests.Integration;
using Lucene.Net.Linq.Translation.Visitors;
using Lucene.Net.Search;
using NUnit.Framework;
using Version = Lucene.Net.Util.LuceneVersion;

namespace Lucene.Net.Linq.Tests.Translation.Visitors
{
    [TestFixture]
    public class QueryBuildingExpressionVisitorTests
    {
        private QueryBuildingExpressionVisitor builder;

        private static readonly Expression MemberAccessId =
            new LuceneQueryFieldExpression(typeof (int), "Id");

        private FieldMappingInfoProviderStub fieldMappingInfoProvider;

        [SetUp]
        public void SetUp()
        {
            fieldMappingInfoProvider = new FieldMappingInfoProviderStub();
            builder = new QueryBuildingExpressionVisitor(fieldMappingInfoProvider);
        }

        [Test]
        public void DefaultMatchesAllDocs()
        {
            Assert.That(builder.Query.ToString(), Is.EqualTo("*:*"));
        }

        [Test]
        public void ThrowsOnUnRecognizedExpressionType()
        {
            var expression = (Expression) Expression.MakeBinary(
                ExpressionType.Modulo,
                MemberAccessId,
                Expression.Constant(1));

            TestDelegate call = () => builder.Visit(expression);

            Assert.That(call, Throws.Exception.InstanceOf<NotSupportedException>());
        }

        [Test]
        public void GreaterThan()
        {
            var expression = new LuceneQueryPredicateExpression(
                new LuceneQueryFieldExpression(typeof(string), "Name"),
                Expression.Constant("SampleName"),
                Occur.MUST,
                QueryType.GreaterThan);

            builder.Visit(expression);

            Assert.That(builder.Query.ToString(), Is.EqualTo("+Name:{SampleName TO *]"));
        }

        [Test]
        public void GreaterThanOrEqual()
        {
            var expression = new LuceneQueryPredicateExpression(
                new LuceneQueryFieldExpression(typeof(string), "Name"),
                Expression.Constant("SampleName"),
                Occur.MUST,
                QueryType.GreaterThanOrEqual);

            builder.Visit(expression);

            Assert.That(builder.Query.ToString(), Is.EqualTo("+Name:[SampleName TO *]"));
        }

        [Test]
        public void LessThan()
        {
            var expression = new LuceneQueryPredicateExpression(
                new LuceneQueryFieldExpression(typeof(string), "Name"),
                Expression.Constant("SampleName"),
                Occur.MUST,
                QueryType.LessThan);

            builder.Visit(expression);

            Assert.That(builder.Query.ToString(), Is.EqualTo("+Name:[* TO SampleName}"));
        }

        [Test]
        public void LessThanOrEqual()
        {
            var expression = new LuceneQueryPredicateExpression(
                new LuceneQueryFieldExpression(typeof(string), "Name"),
                Expression.Constant("SampleName"),
                Occur.MUST,
                QueryType.LessThanOrEqual);

            builder.Visit(expression);

            Assert.That(builder.Query.ToString(), Is.EqualTo("+Name:[* TO SampleName]"));
        }
    }
}