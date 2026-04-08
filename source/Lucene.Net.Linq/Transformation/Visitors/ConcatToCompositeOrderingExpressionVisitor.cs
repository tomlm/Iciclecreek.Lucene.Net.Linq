using System.Linq.Expressions;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Util;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    /// <summary>
    /// Replaces method calls like string.Concat([LuceneQueryFieldExpression], [LuceneQueryFieldExpression]) to LuceneCompositeOrderingExpression
    /// </summary>
    internal class ConcatToCompositeOrderingExpressionVisitor : LuceneExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression expression)
        {
            if (expression.Method.Name == "Concat" && expression.Arguments.Count == 2)
            {
                var fields = new[] { (LuceneQueryFieldExpression)expression.Arguments[0], (LuceneQueryFieldExpression)expression.Arguments[1] };
                return new LuceneCompositeOrderingExpression(fields);
            }
            
            return base.VisitMethodCall(expression);
        }
    }
}