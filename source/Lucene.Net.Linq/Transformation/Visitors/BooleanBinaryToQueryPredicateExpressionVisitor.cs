using System.Linq;
using System.Linq.Expressions;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Util;
using Lucene.Net.Search;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    /// <summary>
    /// Replaces boolean binary expressions like <c>[LuceneQueryPredicateExpression](+field:query) == false</c> to <c>[LuceneQueryPredicateExpression](-field:query)</c>.
    /// A <c>[LuceneQueryExpression] == false</c> becomes a query that prohibits the original one.
    /// </summary>
    internal class BooleanBinaryToQueryPredicateExpressionVisitor : LuceneExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression expression)
        {
            var predicate = expression.Left as LuceneQueryPredicateExpression;
            var query = expression.Left as LuceneQueryExpression;

            var constant = expression.Right.IsTrueConstant();

            if ((predicate == null && query == null) || !(constant || expression.Right.IsFalseConstant()))
            {
                return base.VisitBinary(expression);
            }

            if ((expression.NodeType == ExpressionType.Equal && constant) ||
                (expression.NodeType == ExpressionType.NotEqual && !constant))
            {
                return expression.Left;
            }

            if (query != null)
            {
                return new LuceneQueryExpression(new BooleanQuery { { WithMatchAll(query.Query), Occur.MUST_NOT } });
            }

            return new LuceneQueryPredicateExpression(predicate.QueryField, predicate.QueryPattern, Occur.MUST_NOT, predicate.QueryType)
                    { Boost = predicate.Boost, AllowSpecialCharacters = predicate.AllowSpecialCharacters };
        }

        /// <summary>
        /// A query of only prohibited clauses, like <c>-field:a</c>, means everything except them, but matches
        /// nothing once it is nested, so prohibiting it as it is would exclude nothing. Gives it the match-all
        /// clause it gets as a whole query (see <c>QueryBuildingExpressionVisitor.Query</c>).
        /// </summary>
        private static Query WithMatchAll(Query query)
        {
            var booleanQuery = query as BooleanQuery;

            if (booleanQuery == null || !booleanQuery.GetClauses().All(c => c.Occur == Occur.MUST_NOT))
            {
                return query;
            }

            booleanQuery = (BooleanQuery) booleanQuery.Clone();
            booleanQuery.Add(new MatchAllDocsQuery(), Occur.SHOULD);

            return booleanQuery;
        }
    }
}
