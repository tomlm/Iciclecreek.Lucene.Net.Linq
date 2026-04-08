using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Lucene.Net.Linq.Util
{
    /// <summary>
    /// Small helper that extracts a <see cref="MethodInfo"/> from a lambda
    /// expression body. Replaces the historical use of
    /// <c>Remotion.Linq.Utilities.ReflectionUtility.GetMethod</c>, which was
    /// removed in re-linq 2.x.
    /// </summary>
    internal static class Reflection
    {
        public static MethodInfo MethodOf<T>(Expression<Func<T>> expression)
            => ((MethodCallExpression)expression.Body).Method;

        public static MethodInfo MethodOf(Expression<Action> expression)
            => ((MethodCallExpression)expression.Body).Method;
    }
}
