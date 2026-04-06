using System.Linq.Expressions;
using Lucene.Net.Linq.Clauses.Expressions;
using Remotion.Linq;
using Remotion.Linq.Parsing.ExpressionVisitors.Transformation;

namespace Lucene.Net.Linq.Transformation
{
    internal class AllowSpecialCharactersExpressionTransformer : IExpressionTransformer<MethodCallExpression>
    {
        public Expression Transform(MethodCallExpression expression)
        {
            if (expression.Method.Name != global::Remotion.Linq.Utilities.ReflectionUtility.GetMethod(() => LuceneMethods.AllowSpecialCharacters<object>(null)).Name)
            {
                return expression;
            }

            return new AllowSpecialCharactersExpression(expression.Arguments[0]);
        }

        public ExpressionType[] SupportedExpressionTypes
        {
            get { return new[] {ExpressionType.Call}; }
        }
    }
}