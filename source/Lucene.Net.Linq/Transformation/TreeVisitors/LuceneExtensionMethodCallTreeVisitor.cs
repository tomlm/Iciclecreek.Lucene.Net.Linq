using System.Linq.Expressions;
using System.Reflection;
using Lucene.Net.Linq.Clauses.Expressions;
using Remotion.Linq;

namespace Lucene.Net.Linq.Transformation.TreeVisitors
{
    internal class LuceneExtensionMethodCallTreeVisitor : MethodInfoMatchingTreeVisitor
    {
        private static readonly MethodInfo AnyFieldMethod = global::Remotion.Linq.Utilities.ReflectionUtility.GetMethod(() => LuceneMethods.AnyField<object>(null));
        private static readonly MethodInfo ScoreMethod = global::Remotion.Linq.Utilities.ReflectionUtility.GetMethod(() => LuceneMethods.Score<object>(null));

        public LuceneExtensionMethodCallTreeVisitor()
        {
            AddMethod(AnyFieldMethod);
            AddMethod(ScoreMethod);
        }

        protected override Expression VisitSupportedMethodCallExpression(MethodCallExpression expression)
        {
            if (expression.Method.Name == AnyFieldMethod.Name)
            {
                return LuceneQueryAnyFieldExpression.Instance;
            }

            return LuceneOrderByRelevanceExpression.Instance;
        }
    }
}