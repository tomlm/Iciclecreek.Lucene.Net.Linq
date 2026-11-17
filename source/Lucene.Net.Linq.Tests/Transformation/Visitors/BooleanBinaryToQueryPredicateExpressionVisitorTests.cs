using System.Linq;
using System.Linq.Expressions;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Search;
using Lucene.Net.Linq.Transformation.Visitors;
using Lucene.Net.Search;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Transformation.Visitors
{
    [TestFixture]
    public class BooleanBinaryToQueryPredicateExpressionVisitorTests
    {
        private BooleanBinaryToQueryPredicateExpressionVisitor visitor;

        // Query(Name:foo*)
        private static readonly LuceneQueryPredicateExpression predicate = new LuceneQueryPredicateExpression(
            new LuceneQueryFieldExpression(typeof (string), "Name"),
            Expression.Constant("foo"),
            Occur.MUST,
            QueryType.Prefix);

        [SetUp]
        public void SetUp()
        {
            visitor = new BooleanBinaryToQueryPredicateExpressionVisitor();
        }

        [Test]
        public void EqualFalse()
        {
            var call = CreateBinaryExpression(ExpressionType.Equal, false);

            var result = visitor.Visit(call) as LuceneQueryPredicateExpression;
            
            AssertResult(result, Occur.MUST_NOT);
        }

        [Test]
        public void NotEqualTrue()
        {
            var call = CreateBinaryExpression(ExpressionType.NotEqual, true);

            var result = visitor.Visit(call) as LuceneQueryPredicateExpression;

            AssertResult(result, Occur.MUST_NOT);
        }

        [Test]
        public void EqualTrue()
        {
            var call = CreateBinaryExpression(ExpressionType.Equal, true);

            var result = visitor.Visit(call);

            Assert.That(result, Is.SameAs(predicate));
        }

        [Test]
        public void NotEqualFalse()
        {
            var call = CreateBinaryExpression(ExpressionType.NotEqual, false);

            var result = visitor.Visit(call);

            Assert.That(result, Is.SameAs(predicate));
        }
        [Test]
        public void RetainsBoostAndAllowSpecialCharacters()
        {
            var call = CreateBinaryExpression(ExpressionType.Equal, false);


            predicate.AllowSpecialCharacters = true;
            predicate.Boost = 1234f;

            var result = visitor.Visit(call) as LuceneQueryPredicateExpression;

            AssertResult(result, Occur.MUST_NOT);
        }

        [Test]
        public void QueryEqualFalse()
        {
            var query = new LuceneQueryExpression(new MatchAllDocsQuery());

            var result = visitor.Visit(Expression.MakeBinary(ExpressionType.Equal, query, Expression.Constant(false))) as LuceneQueryExpression;

            Assert.That(result, Is.Not.Null, "Expected LuceneQueryExpression to be returned.");
            var clause = ((BooleanQuery)result.Query).GetClauses().Single();
            Assert.That(clause.Query, Is.SameAs(query.Query));
            Assert.That(clause.Occur, Is.EqualTo(Occur.MUST_NOT));
        }

        [Test]
        public void QueryEqualTrue()
        {
            var query = new LuceneQueryExpression(new MatchAllDocsQuery());

            var result = visitor.Visit(Expression.MakeBinary(ExpressionType.Equal, query, Expression.Constant(true)));

            Assert.That(result, Is.SameAs(query));
        }

        private static void AssertResult(LuceneQueryPredicateExpression result, Occur expectedOccur)
        {
            Assert.That(result, Is.Not.Null, "Expected LuceneQueryPredicateExpression to be returned.");
            Assert.That(result, Is.Not.SameAs(predicate));
            Assert.That(result.QueryField, Is.SameAs(predicate.QueryField));
            Assert.That(result.QueryPattern, Is.SameAs(predicate.QueryPattern));
            Assert.That(result.QueryType, Is.EqualTo(predicate.QueryType));
            Assert.That(result.Occur, Is.EqualTo(expectedOccur));
            Assert.That(result.Boost, Is.EqualTo(predicate.Boost));
            Assert.That(result.AllowSpecialCharacters, Is.EqualTo(predicate.AllowSpecialCharacters));
        }

        private static BinaryExpression CreateBinaryExpression(ExpressionType expressionType, bool value)
        {
            return Expression.MakeBinary(
                expressionType,
                predicate,
                Expression.Constant(value));
        }
    }
}