using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Lucene.Net.Index;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Util;
using Lucene.Net.Queries;
using Lucene.Net.Search;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    /// <summary>
    /// Replaces subqueries like {[doc].Tags => Contains("c")} with BinaryExpressions like ([doc].Tags == "c").
    /// Also handles the "IN" pattern: {["a","b"] => Contains([doc].Field)} → TermsFilter.
    /// </summary>
    internal class SubQueryContainsVisitor : LuceneExpressionVisitor
    {
        protected override Expression VisitSubQuery(SubQueryExpression expression)
        {
            var operators = expression.QueryModel.ResultOperators;

            if (operators.Count == 1 && operators[0] is ContainsResultOperator)
            {
                var op = (ContainsResultOperator) operators[0];

                var fromExpression = expression.QueryModel.MainFromClause.FromExpression;
                var item = op.Item;

                // "IN" pattern: constant_collection.Contains(field)
                // The FromExpression is the collection, the Item is a field reference
                // (either a direct QuerySourceReferenceExpression or a MemberExpression on one).
                if (IsConstantCollection(fromExpression, out var values) && ContainsQuerySourceReference(item))
                {
                    // Return a marker expression — the field name isn't resolved yet
                    // (QuerySourceReferencePropertyTransformingVisitor runs later).
                    // Wrap as a method call that EnumerableContainsToTermsFilterVisitor will pick up.
                    return new InCollectionExpression(item, values);
                }

                // Original pattern: field.Contains(constant) → field == constant
                var field = fromExpression;
                var pattern = item;
                if (pattern.Type.IsPrimitive)
                {
                    pattern = Expression.Constant(((ConstantExpression)pattern).Value, typeof(object));
                }

                return Expression.MakeBinary(ExpressionType.Equal, field, pattern);
            }

            return base.VisitSubQuery(expression);
        }

        private static bool ContainsQuerySourceReference(Expression expression)
        {
            if (expression is QuerySourceReferenceExpression) return true;
            if (expression is MemberExpression member) return ContainsQuerySourceReference(member.Expression);
            if (expression is UnaryExpression unary) return ContainsQuerySourceReference(unary.Operand);
            return false;
        }

        private static bool IsConstantCollection(Expression expression, out IEnumerable values)
        {
            values = null;
            if (expression is ConstantExpression constant && constant.Value is IEnumerable enumerable && !(constant.Value is string))
            {
                values = enumerable;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Marker expression representing "field IN (values)".
    /// Produced by SubQueryContainsVisitor, consumed by EnumerableContainsToTermsFilterVisitor
    /// after the field reference has been resolved to a LuceneQueryFieldExpression.
    /// </summary>
    internal class InCollectionExpression : Expression
    {
        public Expression FieldExpression { get; }
        public IEnumerable Values { get; }

        internal InCollectionExpression(Expression field, IEnumerable values)
        {
            FieldExpression = field;
            Values = values;
        }

        public override ExpressionType NodeType => ExpressionType.Extension;
        public override System.Type Type => typeof(bool);
        public override bool CanReduce => false;

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            var newField = visitor.Visit(FieldExpression);
            if (newField != FieldExpression)
                return new InCollectionExpression(newField, Values);
            return this;
        }
    }
}
