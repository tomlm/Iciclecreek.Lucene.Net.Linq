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
                perFieldAnalyzer.AddAnalyzer(((MemberExpression)fieldName.Body).Member.Name, analyzer);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Field name must be specified as a lambda to a member property, ex. doc => doc.FirstName", ex);
            }
        }
    }
}
