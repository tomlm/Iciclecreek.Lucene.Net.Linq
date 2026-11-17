using System.Linq.Expressions;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Clauses.Visitors;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    internal class FlagToBinaryConditionVisitor : LuceneCustomExpressionVisitor
    {
        private Expression parent;
        private bool negate;

        protected override Expression VisitBinary(BinaryExpression expression)
        {
            ExpressionType inverse;

            if (negate && expression.Type == typeof(bool) && IsRangeComparison(expression.NodeType))
            {
                // !(a > b) is not (a <= b): when a has no value or is NaN both are false, so the negation is true.
                // The comparison is kept and compared with false, which BinaryToQueryExpressionVisitor turns into
                // the same range, prohibited. The operands are values, not conditions.
                negate = false;

                try
                {
                    return Expression.MakeBinary(ExpressionType.Equal, VisitBinary(expression), Expression.Constant(false));
                }
                finally
                {
                    negate = true;
                }
            }

            if (negate && expression.Type == typeof(bool) && TryGetInverse(expression.NodeType, out inverse))
            {
                if (expression.NodeType == ExpressionType.AndAlso || expression.NodeType == ExpressionType.OrElse)
                {
                    // De Morgan: !(a && b) is (!a || !b), and !(a || b) is (!a && !b).
                    return Expression.MakeBinary(inverse, VisitNegated(expression.Left), VisitNegated(expression.Right));
                }

                // !(a == b) is (a != b), and !(a != b) is (a == b). The operands are values, not conditions.
                negate = false;

                try
                {
                    var comparison = (BinaryExpression) VisitBinary(expression);

                    return Expression.MakeBinary(inverse, comparison.Left, comparison.Right);
                }
                finally
                {
                    negate = true;
                }
            }

            var oldParent = parent;

            parent = expression;

            try
            {
                return base.VisitBinary(expression);
            }
            finally
            {
                parent = oldParent;
            }
        }

        /// <summary>
        /// Visits one side of a negated <c>&amp;&amp;</c> or <c>||</c>. A condition that cannot take the negation
        /// itself, like a method call, comes back unchanged and is compared with <c>false</c>.
        /// </summary>
        private Expression VisitNegated(Expression expression)
        {
            var oldParent = parent;

            parent = null;

            try
            {
                var result = Visit(expression);

                return Equals(result, expression) ? Expression.MakeBinary(ExpressionType.Equal, result, Expression.Constant(false)) : result;
            }
            finally
            {
                parent = oldParent;
            }
        }

        private static bool TryGetInverse(ExpressionType type, out ExpressionType inverse)
        {
            switch (type)
            {
                case ExpressionType.AndAlso:
                    inverse = ExpressionType.OrElse;
                    return true;
                case ExpressionType.OrElse:
                    inverse = ExpressionType.AndAlso;
                    return true;
                case ExpressionType.Equal:
                    inverse = ExpressionType.NotEqual;
                    return true;
                case ExpressionType.NotEqual:
                    inverse = ExpressionType.Equal;
                    return true;
            }

            inverse = type;
            return false;
        }

        private static bool IsRangeComparison(ExpressionType type)
        {
            return type == ExpressionType.GreaterThan || type == ExpressionType.GreaterThanOrEqual
                || type == ExpressionType.LessThan || type == ExpressionType.LessThanOrEqual;
        }

        protected override Expression VisitUnary(UnaryExpression expression)
        {
            if (expression.NodeType != ExpressionType.Not || expression.Type != typeof(bool))
            {
                return base.VisitUnary(expression);
            }

            negate = !negate;

            var operand = Visit(expression.Operand);

            negate = !negate;

            // An operand that did not take the negation itself is compared with false,
            // unless this negation cancels an enclosing one.
            if (Equals(operand, expression.Operand) && !negate)
            {
                return Expression.MakeBinary(ExpressionType.Equal, operand, Expression.Constant(false));
            }

            return operand;
        }

        protected override Expression VisitLuceneQueryFieldExpression(LuceneQueryFieldExpression expression)
        {
            if (expression.Type != typeof(bool) || IsAlreadyInEqualityExpression())
            {
                return base.VisitLuceneQueryFieldExpression(expression);
            }

            return Expression.MakeBinary(ExpressionType.Equal, expression, Expression.Constant(!negate));
        }

        private bool IsAlreadyInEqualityExpression()
        {
            if (!(parent is BinaryExpression)) return false;

            var binary = (BinaryExpression)parent;

            return binary.NodeType == ExpressionType.Equal || binary.NodeType == ExpressionType.NotEqual;
        }
    }
}