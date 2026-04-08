using System.Linq.Expressions;
using System.Reflection;
using Lucene.Net.Linq.Clauses.Expressions;
using Remotion.Linq;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    internal class LuceneExtensionMethodCallVisitor : MethodInfoMatchingVisitor
    {
        private static readonly MethodInfo AnyFieldMethod = Lucene.Net.Linq.Util.Reflection.MethodOf(() => LuceneMethods.AnyField<object>(null));
        private static readonly MethodInfo ScoreMethod = Lucene.Net.Linq.Util.Reflection.MethodOf(() => LuceneMethods.Score<object>(null));

        public LuceneExtensionMethodCallVisitor()
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