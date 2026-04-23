using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Lucene.Net.Analysis;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.QueryParsers.Classic;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    /// <summary>
    /// Detects calls to <see cref="LuceneMethods.Query{T}(T, string)"/> and
    /// <see cref="LuceneMethods.Query(string, string)"/> and transforms them
    /// into <see cref="LuceneQueryExpression"/> nodes by parsing the query
    /// string with the Lucene <see cref="QueryParser"/>.
    /// </summary>
    internal class QueryMethodCallVisitor : MethodInfoMatchingVisitor
    {
        private static readonly MethodInfo QueryStringMethod =
            Util.Reflection.MethodOf<bool>(() => LuceneMethods.Query(null, null));

        private static readonly MethodInfo QueryObjectMethod =
            typeof(LuceneMethods).GetMethods()
                .First(m => m.Name == "Query" && m.IsGenericMethod);

        private readonly Analyzer analyzer;
        private readonly string defaultSearchProperty;

        internal QueryMethodCallVisitor(Analyzer analyzer, string defaultSearchProperty)
        {
            AddMethod(QueryStringMethod);
            AddMethod(QueryObjectMethod);
            this.analyzer = analyzer;
            this.defaultSearchProperty = defaultSearchProperty;
        }

        protected override Expression VisitSupportedMethodCallExpression(MethodCallExpression expression)
        {
            string fieldName;
            string queryText;

            if (expression.Method.IsGenericMethod)
            {
                // Object-level: LuceneMethods.Query<T>(obj, queryText) → default search property
                fieldName = defaultSearchProperty
                    ?? throw new InvalidOperationException(
                        "Cannot use Query<T>(obj, queryText) without a default search property. " +
                        "Either call Query on a specific string property, or mark a property with [Field(Default = true)].");
                queryText = EvaluateExpression<string>(expression.Arguments[1]);
            }
            else
            {
                // Property-level: LuceneMethods.Query(property, queryText)
                fieldName = ExtractFieldName(expression.Arguments[0]);
                queryText = EvaluateExpression<string>(expression.Arguments[1]);
            }

            if (string.IsNullOrEmpty(queryText))
            {
                throw new InvalidOperationException(
                    "Query() requires a non-null, non-empty query text.");
            }

            var parser = new QueryParser(Lucene.Net.Util.LuceneVersion.LUCENE_48, fieldName, analyzer);
            var query = parser.Parse(queryText);
            return new LuceneQueryExpression(query);
        }

        private string ExtractFieldName(Expression expression)
        {
            if (expression is LuceneQueryFieldExpression fieldExpr)
            {
                return fieldExpr.FieldName;
            }

            if (expression is MemberExpression memberExpr)
            {
                return memberExpr.Member.Name;
            }

            if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            {
                return ExtractFieldName(unary.Operand);
            }

            throw new NotSupportedException(
                $"Cannot extract field name from expression of type {expression.GetType().Name}. " +
                "Query() must be called on a mapped property.");
        }

        private T EvaluateExpression<T>(Expression expression)
        {
            if (expression is ConstantExpression constant)
            {
                return (T)constant.Value;
            }

            var lambda = Expression.Lambda(expression).Compile();
            return (T)lambda.DynamicInvoke();
        }
    }
}
