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
        // Every Similar overload: on a property or on the object, given a text or a vector.
        private static readonly MethodInfo[] SimilarMethods =
            typeof(LuceneMethods).GetMethods()
                .Where(m => m.Name == nameof(LuceneMethods.Similar))
                .ToArray();

        private readonly IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;
        private readonly string defaultSearchProperty;

        internal SimilarMethodCallVisitor(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, string defaultSearchProperty = null)
        {
            foreach (var method in SimilarMethods)
                AddMethod(method);
            this.embeddingGenerator = embeddingGenerator;
            this.defaultSearchProperty = defaultSearchProperty;
        }

        protected override Expression VisitSupportedMethodCallExpression(MethodCallExpression expression)
        {
            string fieldName;
            if (expression.Method.IsGenericMethod)
            {
                // Object-level: LuceneMethods.Similar<T>(obj, ...) → default search property
                fieldName = defaultSearchProperty
                    ?? throw new InvalidOperationException(
                        "Cannot use Similar<T>(obj, ...) without a default search property. " +
                        "Either call Similar on a specific string property, or ensure the mapped type has a key or indexed property.");
            }
            else
            {
                // Property-level: LuceneMethods.Similar(property, ...)
                fieldName = ExtractFieldName(expression.Arguments[0]);
            }

            float[] vector;
            if (expression.Arguments[1].Type == typeof(float[]))
            {
                // A vector already computed: searched as it is, with nothing embedded.
                vector = EvaluateExpression<float[]>(expression.Arguments[1]);
                if (vector == null || vector.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Similar() requires a non-null, non-empty vector.");
                }
            }
            else
            {
                var queryText = EvaluateExpression<string>(expression.Arguments[1]);
                if (string.IsNullOrEmpty(queryText))
                {
                    throw new InvalidOperationException(
                        "Similar() requires a non-null, non-empty query text.");
                }
                if (embeddingGenerator == null)
                {
                    throw new InvalidOperationException(
                        "Cannot use Similar() with a query text without configuring an IEmbeddingGenerator on LuceneDataProviderSettings.EmbeddingGenerator. " +
                        "Similar() with a vector needs none.");
                }
                vector = GenerateEmbedding(queryText);
            }

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
