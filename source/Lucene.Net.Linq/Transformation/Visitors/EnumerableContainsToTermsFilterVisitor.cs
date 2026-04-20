using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Lucene.Net.Index;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Util;
using Lucene.Net.Queries;
using Lucene.Net.Search;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    /// <summary>
    /// Converts <see cref="InCollectionExpression"/> markers (produced by
    /// <see cref="SubQueryContainsVisitor"/>) into <see cref="TermsFilter"/>-backed
    /// Lucene queries. Runs after <see cref="QuerySourceReferencePropertyTransformingVisitor"/>
    /// so the field reference has been resolved to a <see cref="LuceneQueryFieldExpression"/>.
    ///
    /// Example:
    /// <code>
    /// var ids = new[] { "alice", "bob" };
    /// db.Search&lt;Note&gt;(n =&gt; ids.Contains(n.AuthorId));
    /// </code>
    ///
    /// Produces: <c>ConstantScoreQuery(TermsFilter([AuthorId:alice, AuthorId:bob]))</c>
    /// </summary>
    internal class EnumerableContainsToTermsFilterVisitor : LuceneExpressionVisitor
    {
        public override Expression Visit(Expression node)
        {
            if (node is InCollectionExpression inExpr)
            {
                // The field should now be a LuceneQueryFieldExpression
                // (transformed by QuerySourceReferencePropertyTransformingVisitor)
                if (inExpr.Field is LuceneQueryFieldExpression queryField)
                {
                    return BuildTermsFilter(queryField.FieldName, inExpr.Values);
                }

                // Field not yet resolved — leave as-is for now
                return node;
            }

            return base.Visit(node);
        }

        private static Expression BuildTermsFilter(string fieldName, IEnumerable values)
        {
            var terms = new List<Term>();
            foreach (var value in values)
            {
                if (value != null)
                    terms.Add(new Term(fieldName, value.ToString()));
            }

            if (terms.Count == 0)
            {
                // Empty collection → match nothing.
                // A BooleanQuery with a single MUST_NOT clause and no positive clause matches nothing.
                var matchNone = new BooleanQuery();
                matchNone.Add(new MatchAllDocsQuery(), Occur.MUST_NOT);
                return new LuceneQueryExpression(matchNone);
            }

            var termsFilter = new TermsFilter(terms);
            var query = new ConstantScoreQuery(termsFilter);
            return new LuceneQueryExpression(query);
        }
    }
}
