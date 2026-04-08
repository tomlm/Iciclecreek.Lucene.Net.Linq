using System;
using System.Linq.Expressions;
using Lucene.Net.Linq.Util;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    /// <summary>
    /// Replaces expressions like <c>(bool)(Constant(bool?))</c> with <c>Constant(bool?)</c>.
    /// </summary>
    internal class NoOpConvertExpressionRemovingVisitor : LuceneExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression expression)
        {
            var left = base.Visit(expression.Left);
            var right = base.Visit(expression.Right);

            if (ReferenceEquals(left, expression.Left) && ReferenceEquals(right, expression.Right))
            {
                return expression;
            }

            left = ConvertIfNecessary(left, right.Type);
            right = ConvertIfNecessary(right, left.Type);

            return Expression.MakeBinary(expression.NodeType, left, right);
        }

        private Expression ConvertIfNecessary(Expression expression, Type type)
        {
            var constant = expression as ConstantExpression;
            if (constant == null || expression.Type == type) return expression;

            if (type.IsEnum)
            {
                return Expression.Constant(Enum.ToObject(type, constant.Value));
            }

            return Expression.Constant(Convert.ChangeType(constant.Value, type));
        }

        protected override Expression VisitUnary(UnaryExpression expression)
        {
            if (expression.NodeType == ExpressionType.Convert)
            {
                // Strip the Convert wrapper unconditionally. The shim's
                // VisitUnary default rebuilds parent unary nodes
                // via Expression.MakeUnary (without BCL Update validation),
                // so type-changing rewrites no longer trip ValidateUnary.
                return base.Visit(expression.Operand);
            }

            return base.VisitUnary(expression);
        }
    }
}