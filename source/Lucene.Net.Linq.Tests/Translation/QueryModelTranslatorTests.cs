using System;
using System.Linq.Expressions;
using Lucene.Net.Linq.Clauses;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Linq.Translation;
using Lucene.Net.Search;
using Lucene.Net.Store;
using NSubstitute;
using NUnit.Framework;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Lucene.Net.Linq.Tests.Translation
{
    [TestFixture]
    public class QueryModelTranslatorTests
    {
        private IFieldMappingInfoProvider mappingProvider;
        private QueryModelTranslator transformer;
        private readonly QueryModel queryModel = new QueryModel(new MainFromClause("i", typeof(Record), Expression.Constant("r")), new SelectClause(Expression.Constant("a")));
        private Context context;

        [SetUp]
        public void SetUp()
        {
            mappingProvider = Substitute.For<IFieldMappingInfoProvider>();
            context = new Context(new RAMDirectory(), new object());
            transformer = new QueryModelTranslator(mappingProvider, context);
        }

        [Test]
        public void NoOrderByClauses()
        {
            Assert.That(transformer.Model.Sort, Is.Not.Null);
            Assert.That(transformer.Model.Sort.GetSort().Length, Is.EqualTo(1));
            Assert.That(transformer.Model.Sort.GetSort(), Is.EqualTo(new Sort().GetSort()));
        }

        [Test]
        public void ConvertsToSort()
        {
            var orderByClause = new OrderByClause();
            orderByClause.Orderings.Add(new Ordering(new LuceneQueryFieldExpression(typeof(string), "Name"), OrderingDirection.Asc));
            ExpectSortOnProperty("Name", SortFieldType.STRING, OrderingDirection.Asc);

            transformer.VisitOrderByClause(orderByClause, queryModel, 0);

            Assert.That(transformer.Model.Sort.GetSort().Length, Is.EqualTo(1));
            AssertSortFieldEquals(transformer.Model.Sort.GetSort()[0], "Name", OrderingDirection.Asc, SortFieldType.STRING);
        }

        [Test]
        public void ConvertsDateTimeOffsetToSort()
        {
            var orderByClause = new OrderByClause();
            orderByClause.Orderings.Add(new Ordering(new LuceneQueryFieldExpression(typeof(DateTimeOffset?), "Date"), OrderingDirection.Asc));
            ExpectSortOnProperty("Date", SortFieldType.INT64, OrderingDirection.Asc);

            transformer.VisitOrderByClause(orderByClause, queryModel, 0);

            Assert.That(transformer.Model.Sort.GetSort().Length, Is.EqualTo(1));
            AssertSortFieldEquals(transformer.Model.Sort.GetSort()[0], "Date", OrderingDirection.Asc, SortFieldType.INT64);
        }

        [Test]
        public void ConvertsToSort_Desc()
        {
            var orderByClause = new OrderByClause();
            orderByClause.Orderings.Add(new Ordering(new LuceneQueryFieldExpression(typeof(string), "Name"), OrderingDirection.Desc));
            ExpectSortOnProperty("Name", SortFieldType.STRING, OrderingDirection.Desc);

            transformer.VisitOrderByClause(orderByClause, queryModel, 0);

            Assert.That(transformer.Model.Sort.GetSort().Length, Is.EqualTo(1));
            AssertSortFieldEquals(transformer.Model.Sort.GetSort()[0], "Name", OrderingDirection.Desc, SortFieldType.STRING);
        }

        [Test]
        public void ConvertsToSort_MultipleOrderings()
        {
            var orderByClause = new OrderByClause();
            orderByClause.Orderings.Add(new Ordering(new LuceneQueryFieldExpression(typeof(string), "Name"), OrderingDirection.Asc));
            orderByClause.Orderings.Add(new Ordering(new LuceneQueryFieldExpression(typeof(int), "Id"), OrderingDirection.Desc));

            ExpectSortOnProperty("Name", SortFieldType.STRING, OrderingDirection.Asc);
            ExpectSortOnProperty("Id", SortFieldType.INT64, OrderingDirection.Desc);

            transformer.VisitOrderByClause(orderByClause, queryModel, 0);

            Assert.That(transformer.Model.Sort.GetSort().Length, Is.EqualTo(2));
            AssertSortFieldEquals(transformer.Model.Sort.GetSort()[0], "Name", OrderingDirection.Asc, SortFieldType.STRING);
            AssertSortFieldEquals(transformer.Model.Sort.GetSort()[1], "Id", OrderingDirection.Desc, SortFieldType.INT64);
        }

        [Test]
        public void ConvertsToSort_MultipleClauses()
        {
            ExpectSortOnProperty("Name", SortFieldType.STRING, OrderingDirection.Asc);
            ExpectSortOnProperty("Id", SortFieldType.INT64, OrderingDirection.Desc);

            var orderByClause = new OrderByClause();
            orderByClause.Orderings.Add(new Ordering(new LuceneQueryFieldExpression(typeof(string), "Name"), OrderingDirection.Asc));

            transformer.VisitOrderByClause(orderByClause, queryModel, 0);

            orderByClause = new OrderByClause();
            orderByClause.Orderings.Add(new Ordering(new LuceneQueryFieldExpression(typeof(int), "Id"), OrderingDirection.Desc));

            transformer.VisitOrderByClause(orderByClause, queryModel, 1);

            // As in LINQ, the later orderby clause is the primary sort and the earlier one breaks ties.
            Assert.That(transformer.Model.Sort.GetSort().Length, Is.EqualTo(2));
            AssertSortFieldEquals(transformer.Model.Sort.GetSort()[0], "Id", OrderingDirection.Desc, SortFieldType.INT64);
            AssertSortFieldEquals(transformer.Model.Sort.GetSort()[1], "Name", OrderingDirection.Asc, SortFieldType.STRING);
        }

        [Test]
        public void SetsDocumentTracker()
        {
            var expr = Expression.Constant(this);
            transformer.VisitTrackRetrievedDocumentsClause(new TrackRetrievedDocumentsClause(expr), queryModel, 0);

            Assert.That(transformer.Model.DocumentTracker, Is.SameAs(expr.Value));
        }


        private void AssertSortFieldEquals(SortField sortField, string expectedFieldName, OrderingDirection expectedDirection, SortFieldType expectedType)
        {
            Assert.That(sortField.Field, Is.EqualTo(expectedFieldName));
            Assert.That(sortField.Type, Is.EqualTo(expectedType), "SortField type for field " + expectedFieldName);
            Assert.That(sortField.IsReverse, Is.EqualTo(expectedDirection == OrderingDirection.Desc), "Reverse");
        }

        private void ExpectSortOnProperty(string propertyName, SortFieldType sortType, OrderingDirection direction)
        {
            var mappingInfo = Substitute.For<IFieldMappingInfo>();
            mappingProvider.GetMappingInfo(propertyName).Returns(mappingInfo);
            mappingInfo.FieldName.Returns(propertyName);
            mappingInfo.CreateSortField(direction == OrderingDirection.Desc).Returns(new SortField(propertyName, sortType, direction == OrderingDirection.Desc));
        }
    }
}
