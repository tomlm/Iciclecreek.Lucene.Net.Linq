using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Lucene.Net.Linq.Clauses.Expressions;
using Microsoft.Extensions.AI;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    /// <summary>
    /// Detects calls to <see cref="LuceneMethods.Similar"/> and transforms them
    /// into <see cref="LuceneVectorQueryExpression"/> nodes.
    /// </summary>
    internal class SimilarMethodCallVisitor : MethodInfoMatchingVisitor
    {
        private static readonly MethodInfo SimilarStringMethod =
            Util.Reflection.MethodOf<bool>(() => LuceneMethods.Similar(null, null));

        private static readonly MethodInfo SimilarObjectMethod =
            typeof(LuceneMethods).GetMethods()
                .First(m => m.Name == "Similar" && m.IsGenericMethod);

        private readonly IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;
        private readonly string defaultSearchProperty;

        internal SimilarMethodCallVisitor(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, string defaultSearchProperty = null)
        {
            AddMethod(SimilarStringMethod);
            AddMethod(SimilarObjectMethod);
            this.embeddingGenerator = embeddingGenerator;
            this.defaultSearchProperty = defaultSearchProperty;
        }

        protected override Expression VisitSupportedMethodCallExpression(MethodCallExpression expression)
        {
            if (embeddingGenerator == null)
            {
                throw new InvalidOperationException(
                    "Cannot use Similar() without configuring an IEmbeddingGenerator on LuceneDataProviderSettings.EmbeddingGenerator.");
            }

            string fieldName;
            string queryText;

            if (expression.Method.IsGenericMethod)
            {
                // Object-level: LuceneMethods.Similar<T>(obj, queryText) → default search property
                fieldName = defaultSearchProperty
                    ?? throw new InvalidOperationException(
                        "Cannot use Similar<T>(obj, queryText) without a default search property. " +
                        "Either call Similar on a specific string property, or ensure the mapped type has a key or indexed property.");
                queryText = EvaluateExpression<string>(expression.Arguments[1]);
            }
            else
            {
                // Property-level: LuceneMethods.Similar(property, queryText)
                fieldName = ExtractFieldName(expression.Arguments[0]);
                queryText = EvaluateExpression<string>(expression.Arguments[1]);
            }

            if (string.IsNullOrEmpty(queryText))
            {
                throw new InvalidOperationException(
                    "Similar() requires a non-null, non-empty query text.");
            }

            var vector = GenerateEmbedding(queryText);
            var vectorFieldName = fieldName + "_vector";

            return new LuceneVectorQueryExpression(vectorFieldName, vector);
        }

        private string ExtractFieldName(Expression expression)
        {
            // Walk through any transformations to find the original field name.
            // The expression might be a QuerySourceReferencePropertyTransformingVisitor result
            // (LuceneQueryFieldExpression) or a MemberExpression.
            if (expression is LuceneQueryFieldExpression fieldExpr)
            {
                return fieldExpr.FieldName;
            }

            if (expression is MemberExpression memberExpr)
            {
                return memberExpr.Member.Name;
            }

            // Unwrap conversions
            if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            {
                return ExtractFieldName(unary.Operand);
            }

            throw new NotSupportedException(
                $"Cannot extract field name from expression of type {expression.GetType().Name}. " +
                "Similar() must be called on a mapped property.");
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

        private float[] GenerateEmbedding(string text)
        {
            var result = Task.Run(() => embeddingGenerator.GenerateAsync(new[] { text }))
                .GetAwaiter().GetResult();

            if (result == null || result.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Embedding generator returned no results for text: '{text}'");
            }

            return result[0].Vector.ToArray();
        }
    }
}
