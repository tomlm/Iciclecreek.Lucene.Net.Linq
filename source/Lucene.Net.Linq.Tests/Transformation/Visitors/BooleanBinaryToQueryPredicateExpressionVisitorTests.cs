using System.Linq;
using System.Linq.Expressions;
using Lucene.Net.Index;
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
        public void QueryEqualFalse_QueryOfOnlyProhibitedClauses()
        {
            // -Name:foo -Name:bar selects everything except foo and bar, but nothing once it is nested.
            var foo = new TermQuery(new Term("Name", "foo"));
            var bar = new TermQuery(new Term("Name", "bar"));
            var query = new LuceneQueryExpression(new BooleanQuery { { foo, Occur.MUST_NOT }, { bar, Occur.MUST_NOT } });

            var result = visitor.Visit(Expression.MakeBinary(ExpressionType.Equal, query, Expression.Constant(false))) as LuceneQueryExpression;

            Assert.That(result, Is.Not.Null, "Expected LuceneQueryExpression to be returned.");
            var clause = ((BooleanQuery)result.Query).GetClauses().Single();
            Assert.That(clause.Occur, Is.EqualTo(Occur.MUST_NOT));

            // -(-Name:foo -Name:bar *:*)
            var prohibited = ((BooleanQuery)clause.Query).GetClauses();
            Assert.That(prohibited.Length, Is.EqualTo(3));
            Assert.That(prohibited[0].Query, Is.SameAs(foo));
            Assert.That(prohibited[0].Occur, Is.EqualTo(Occur.MUST_NOT));
            Assert.That(prohibited[1].Query, Is.SameAs(bar));
            Assert.That(prohibited[1].Occur, Is.EqualTo(Occur.MUST_NOT));
            Assert.That(prohibited[2].Query, Is.InstanceOf<MatchAllDocsQuery>());
            Assert.That(prohibited[2].Occur, Is.EqualTo(Occur.SHOULD));

            Assert.That(((BooleanQuery)query.Query).GetClauses().Length, Is.EqualTo(2), "The original query must not be changed.");
        }

        [Test]
        public void QueryEqualFalse_QueryWithAPositiveClause()
        {
            // +Name:foo -Name:bar is prohibited as it is.
            var query = new LuceneQueryExpression(new BooleanQuery
            {
                { new TermQuery(new Term("Name", "foo")), Occur.MUST },
                { new TermQuery(new Term("Name", "bar")), Occur.MUST_NOT }
            });

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