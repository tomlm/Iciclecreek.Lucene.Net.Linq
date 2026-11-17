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
                return new LuceneQueryExpression(new BooleanQuery { { query.Query, Occur.MUST_NOT } });
            }

            return new LuceneQueryPredicateExpression(predicate.QueryField, predicate.QueryPattern, Occur.MUST_NOT, predicate.QueryType)
                    { Boost = predicate.Boost, AllowSpecialCharacters = predicate.AllowSpecialCharacters };
        }
    }
}
