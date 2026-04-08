using System.Linq.Expressions;
using Lucene.Net.Linq.Transformation;
using Lucene.Net.Linq.Util;
using NSubstitute;
using NUnit.Framework;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Lucene.Net.Linq.Tests.Transformation
{
    [TestFixture]
    public class QueryModelTransformerTests
    {
        private static readonly ConstantExpression constantExpression = Expression.Constant(true);
        private static readonly WhereClause whereClause = new WhereClause(constantExpression);
        private LuceneExpressionVisitor visitor1;
        private LuceneExpressionVisitor visitor2;
        private QueryModelTransformer transformer;
        private readonly QueryModel queryModel = new QueryModel(new MainFromClause("i", typeof(Record), Expression.Constant("r")), new SelectClause(Expression.Constant("a")));

        [SetUp]
        public void SetUp()
        {
            visitor1 = Substitute.For<LuceneExpressionVisitor>();
            visitor2 = Substitute.For<LuceneExpressionVisitor>();
            var visitors = new[] { visitor1, visitor2 };
            transformer = new QueryModelTransformer(visitors, visitors);

            visitor1.Visit(whereClause.Predicate).Returns(whereClause.Predicate);
            visitor2.Visit(whereClause.Predicate).Returns(whereClause.Predicate);
        }

        [Test]
        public void VisitsWhereClause()
        {
            transformer.VisitWhereClause(whereClause, queryModel, 0);

            Received.InOrder(() =>
            {
                visitor1.Visit(whereClause.Predicate);
                visitor2.Visit(whereClause.Predicate);
            });
        }

        [Test]
        public void VisitsOrderByClause()
        {
            var orderByClause = new OrderByClause();
            orderByClause.Orderings.Add(new Ordering(constantExpression, OrderingDirection.Asc));

            transformer.VisitOrderByClause(orderByClause, queryModel, 0);

            visitor1.Received().Visit(constantExpression);
            visitor2.Received().Visit(constantExpression);
        }
    }
}
