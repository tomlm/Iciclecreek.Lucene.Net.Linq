using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Lucene.Net.Linq.Util;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    internal abstract class MethodInfoMatchingVisitor : LuceneExpressionVisitor
    {
        private readonly HashSet<MethodInfo> methods = new HashSet<MethodInfo>();

        internal void AddMethod(MethodInfo method)
        {
            methods.Add(method.IsGenericMethod ? method.GetGenericMethodDefinition() : method);
        }

        protected override Expression VisitMethodCall(MethodCallExpression expression)
        {
            var method = expression.Method.IsGenericMethod
                             ? expression.Method.GetGenericMethodDefinition()
                             : expression.Method;

            if (!methods.Contains(method)) 
                return base.VisitMethodCall(expression);

            return VisitSupportedMethodCallExpression(expression);
        }

        protected abstract Expression VisitSupportedMethodCallExpression(MethodCallExpression expression);
    }
}