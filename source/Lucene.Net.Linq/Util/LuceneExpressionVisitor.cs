using System;
using System.Linq.Expressions;
using Remotion.Linq.Parsing;

namespace Lucene.Net.Linq.Util
{
    /// <summary>
    /// Common base class for Lucene.Net.Linq's expression-tree visitors.
    /// Derives from <see cref="RelinqExpressionVisitor"/> (re-linq's wrapper
    /// around <see cref="ExpressionVisitor"/>) and overrides
    /// <see cref="ExpressionVisitor.VisitUnary"/> to bypass the BCL's strict
    /// child-type validation.
    /// </summary>
    /// <remarks>
    /// re-linq 1.x's ExpressionVisitor recreated unary nodes via
    /// <see cref="Expression.MakeUnary(ExpressionType, Expression, Type, System.Reflection.MethodInfo)"/>
    /// without going through the BCL <c>ExpressionVisitor</c>'s Update +
    /// ValidateUnary path. The BCL validation throws when a transforming
    /// visitor (such as <c>NoOpConvertExpressionRemovingVisitor</c>) replaces
    /// an operand with one of a different type. We bypass that by
    /// reconstructing manually here, the way re-linq 1.x did.
    /// </remarks>
    internal abstract class LuceneExpressionVisitor : RelinqExpressionVisitor
    {
        protected override Expression VisitUnary(UnaryExpression node)
        {
            var newOperand = Visit(node.Operand);
            if (ReferenceEquals(newOperand, node.Operand)) return node;
            try
            {
                return Expression.MakeUnary(node.NodeType, newOperand, node.Type, node.Method);
            }
            catch (InvalidOperationException)
            {
                // MakeUnary itself rejected the rewrite (some unary operators
                // require a particular operand type that the rebuild can't
                // satisfy). Fall back to the original to keep the visitor
                // pipeline running.
                return node;
            }
        }
    }
}
