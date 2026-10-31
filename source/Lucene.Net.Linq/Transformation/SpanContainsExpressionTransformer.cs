using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Remotion.Linq.Parsing.ExpressionVisitors.Transformation;

namespace Lucene.Net.Linq.Transformation
{
    /// <summary>
    /// Rewrites <c>MemoryExtensions.Contains(op_Implicit(collection), value)</c> into
    /// <c>Enumerable.Contains(collection, value)</c>.
    ///
    /// With C# 14 first-class spans, <c>array.Contains(x)</c> inside an expression tree binds to
    /// <c>MemoryExtensions.Contains(ReadOnlySpan&lt;T&gt;, T)</c> through an implicit array-to-span
    /// conversion. re-linq only recognizes <c>Enumerable.Contains</c>, so without this rewrite both
    /// the "IN" pattern (<c>ids.Contains(x.Id)</c>) and multi-valued fields
    /// (<c>x.Tags.Contains("a")</c>) went untranslated and the filter was silently dropped.
    /// </summary>
    internal class SpanContainsExpressionTransformer : IExpressionTransformer<MethodCallExpression>
    {
        private static readonly MethodInfo EnumerableContains = typeof(Enumerable).GetMethods()
            .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

        public ExpressionType[] SupportedExpressionTypes
        {
            get { return new[] { ExpressionType.Call }; }
        }

        public Expression Transform(MethodCallExpression expression)
        {
            var method = expression.Method;
            if (method.Name != "Contains" || method.DeclaringType?.FullName != "System.MemoryExtensions" || !method.IsGenericMethod)
            {
                return expression;
            }

            // Contains(span, value) or Contains(span, value, comparer: null) — a real comparer can't be translated.
            var args = expression.Arguments;
            if (args.Count < 2 || args.Count > 3)
            {
                return expression;
            }
            if (args.Count == 3 && !(args[2] is ConstantExpression comparer && comparer.Value == null))
            {
                return expression;
            }

            var collection = UnwrapSpanConversion(args[0]);
            if (collection == null)
            {
                return expression;
            }

            var elementType = method.GetGenericArguments()[0];
            if (!typeof(IEnumerable<>).MakeGenericType(elementType).IsAssignableFrom(collection.Type))
            {
                return expression;
            }

            return Expression.Call(EnumerableContains.MakeGenericMethod(elementType), collection, args[1]);
        }

        /// <summary>
        /// Returns the collection behind an implicit span conversion (<c>op_Implicit(x)</c>, or a
        /// <c>Convert</c> node to a span type), or null when <paramref name="expression"/> isn't one.
        /// </summary>
        private static Expression UnwrapSpanConversion(Expression expression)
        {
            if (expression is MethodCallExpression call && call.Method.Name == "op_Implicit"
                && call.Arguments.Count == 1 && IsSpanType(call.Type))
            {
                return StripConvert(call.Arguments[0]);
            }

            if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert && IsSpanType(unary.Type))
            {
                return StripConvert(unary.Operand);
            }

            return null;
        }

        private static Expression StripConvert(Expression expression)
        {
            // e.g. op_Implicit(Convert(x.Links, String[])) — a no-op conversion to the array type.
            while (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert
                   && unary.Type.IsAssignableFrom(unary.Operand.Type))
            {
                expression = unary.Operand;
            }
            return expression;
        }

        private static bool IsSpanType(Type type)
        {
            if (!type.IsGenericType) return false;
            var definition = type.GetGenericTypeDefinition();
            return definition.FullName == "System.ReadOnlySpan`1" || definition.FullName == "System.Span`1";
        }
    }
}
