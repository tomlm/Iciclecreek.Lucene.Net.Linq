using System;
using System.Linq.Expressions;
using Lucene.Net.Analysis;
using Lucene.Net.Linq.Analysis;

namespace Lucene.Net.Linq
{
    /// <summary>
    /// Provides extensions to built-in Lucene.Net Analyzer classes.
    /// </summary>
    public static class AnalyzerExtensions
    {
        /// <summary>
        /// Defines an analyzer to use for the specified field in a strongly-typed manner.
        /// </summary>
        public static void AddAnalyzer<TDocument>(this PerFieldAnalyzer perFieldAnalyzer, Expression<Func<TDocument, object>> fieldName, Analyzer analyzer)
        {
            try
            {
                // Value-typed properties produce a Convert(MemberExpression) wrapper
                // when bound to Func<T, object>; unwrap to find the member.
                var body = fieldName.Body;
                if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
                {
                    body = unary.Operand;
                }
                perFieldAnalyzer.AddAnalyzer(((MemberExpression)body).Member.Name, analyzer);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Field name must be specified as a lambda to a member property, ex. doc => doc.FirstName", ex);
            }
        }
    }
}
