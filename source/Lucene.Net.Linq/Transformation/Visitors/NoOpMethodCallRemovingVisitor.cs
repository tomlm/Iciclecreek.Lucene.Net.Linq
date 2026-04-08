using System.Collections.Generic;
using System.Linq.Expressions;
using Lucene.Net.Linq.Util;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    /// <summary>
    /// Removes method calls like string.ToLower() that have no effect on a query due to
    /// case sensitivity in Lucene being configured elsewhere by the Analyzer.
    /// </summary>
    internal class NoOpMethodCallRemovingVisitor : LuceneExpressionVisitor
    {
        private static readonly ISet<string> NoOpMethods =
            new HashSet<string>
                {
                    "ToLower",
                    "ToLowerInvariant",
                    "ToUpper",
                    "ToUpeprInvariant"
                };

        protected override Expression VisitMethodCall(MethodCallExpression expression)
        {
            if (NoOpMethods.Contains(expression.Method.Name))
            {
                return expression.Object;
            }

            return base.VisitMethodCall(expression);
        }
    }
}