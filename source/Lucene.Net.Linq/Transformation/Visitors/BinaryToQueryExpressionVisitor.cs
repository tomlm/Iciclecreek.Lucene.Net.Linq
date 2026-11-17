using System;
using System.Linq.Expressions;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Search;
using Lucene.Net.Search;
using Lucene.Net.Linq.Util;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    internal class BinaryToQueryExpressionVisitor : LuceneExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression expression)
        {
            QueryType queryType;

            if (IsNegatedRangeComparison(expression))
            {
                // ([field] > value) == false is how FlagToBinaryConditionVisitor writes !([field] > value): the same
                // range, prohibited. Unlike the inverse range ([field] <= value), that also selects the documents
                // where the field has no value or holds NaN, as the negation does in C#.
                var range = (LuceneQueryPredicateExpression) VisitBinary((BinaryExpression) expression.Left);

                return new LuceneQueryPredicateExpression(range.QueryField, range.QueryPattern, Occur.MUST_NOT, range.QueryType);
            }

            if (!expression.NodeType.TryGetQueryType(out queryType))
            {
                return base.VisitBinary(expression);
            }

            var occur = Occur.MUST;
            if (expression.NodeType == ExpressionType.NotEqual)
            {
                occur = Occur.MUST_NOT;
            }

            LuceneQueryFieldExpression fieldExpression;
            Expression pattern;

            if (expression.Left is LuceneQueryFieldExpression)
            {
                fieldExpression = (LuceneQueryFieldExpression) expression.Left;
                pattern = expression.Right;
            }
            else if (expression.Right is LuceneQueryFieldExpression)
            {
                fieldExpression = (LuceneQueryFieldExpression) expression.Right;
                pattern = expression.Left;

                switch(queryType)
                {
                    case QueryType.GreaterThan:
                        queryType = QueryType.LessThan;
                        break;
                    case QueryType.LessThan:
                        queryType = QueryType.GreaterThan;
                        break;
                    case QueryType.GreaterThanOrEqual:
                        queryType = QueryType.LessThanOrEqual;
                        break;
                    case QueryType.LessThanOrEqual:
                        queryType = QueryType.GreaterThanOrEqual;
                        break;
                }
            }
            else
            {
                if (IsSimilar(expression.Left) || IsSimilar(expression.Right))
                {
                    throw new NotSupportedException(
                        "Similar() cannot be negated or compared with a value: there is no set of documents that are \"not similar\". " +
                        "Use Similar() to rank the documents the other conditions select.");
                }

                throw new NotSupportedException("Expected Left or Right to be LuceneQueryFieldExpression");
            }

            return new LuceneQueryPredicateExpression(fieldExpression, pattern, occur, queryType);
        }

        private static bool IsNegatedRangeComparison(BinaryExpression expression)
        {
            if (expression.NodeType != ExpressionType.Equal || !expression.Right.IsFalseConstant())
            {
                return false;
            }

            switch (expression.Left.NodeType)
            {
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                    return true;
            }

            return false;
        }

        private static bool IsSimilar(Expression expression)
        {
            return expression is LuceneVectorQueryExpression
                || (expression is MethodCallExpression call
                    && call.Method.DeclaringType == typeof(LuceneMethods)
                    && call.Method.Name == nameof(LuceneMethods.Similar));
        }
    }
}