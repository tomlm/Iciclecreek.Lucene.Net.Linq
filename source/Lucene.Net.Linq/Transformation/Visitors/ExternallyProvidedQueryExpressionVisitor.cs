using System.Linq.Expressions;
using System.Reflection;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Search;
using Remotion.Linq;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    /// <summary>
    /// Replaces method calls like <c cref="LuceneMethods.Matches{T}">Matches</c> with query expressions.
    /// </summary>
    internal class ExternallyProvidedQueryExpressionVisitor : MethodInfoMatchingVisitor
    {
        private static readonly MethodInfo MatchesMethod = Lucene.Net.Linq.Util.Reflection.MethodOf(() => LuceneMethods.Matches<object>(null, null));

        internal ExternallyProvidedQueryExpressionVisitor()
        {
            AddMethod(MatchesMethod);
        }

        protected override Expression VisitSupportedMethodCallExpression(MethodCallExpression expression)
        {
            return new LuceneQueryExpression((Query) ((ConstantExpression)expression.Arguments[0]).Value);
        }
    }
}