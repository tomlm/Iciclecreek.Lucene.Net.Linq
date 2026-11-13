using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;

namespace Lucene.Net.Linq.Translation
{
    /// <summary>
    /// The inner sequence of a join: either a Lucene query (its executor and a
    /// private copy of its query model) or a local in-memory sequence.
    /// </summary>
    internal sealed class JoinInnerSequence
    {
        private JoinInnerSequence(IQueryExecutor executor, QueryModel queryModel, IEnumerable local)
        {
            Executor = executor;
            QueryModel = queryModel;
            Local = local;
        }

        /// <summary>The executor of the inner Lucene query; <c>null</c> for a local sequence.</summary>
        public IQueryExecutor Executor { get; }

        /// <summary>The inner query, free to modify; <c>null</c> for a local sequence.</summary>
        public QueryModel QueryModel { get; }

        /// <summary>A sequence that is not a Lucene query; otherwise <c>null</c>.</summary>
        public IEnumerable Local { get; }

        /// <summary>
        /// Whether the inner query returns its documents as they are, so that a
        /// key selector's property is a property of the mapped type.
        /// </summary>
        public bool ReturnsDocuments
        {
            get
            {
                return QueryModel != null
                    && QueryModel.SelectClause.Selector is QuerySourceReferenceExpression reference
                    && reference.ReferencedQuerySource == QueryModel.MainFromClause;
            }
        }

        public static JoinInnerSequence Resolve(JoinClause join)
        {
            // A bare queryable is a constant; one with its own Where, Take… is a
            // sub-query, which cannot be compiled and evaluated like a constant.
            if (join.InnerSequence is SubQueryExpression subQuery)
            {
                // The queryable at the bottom, under any nested paging (authors.Take(5).Where(...)).
                var source = subQuery.QueryModel.MainFromClause.FromExpression;
                while (source is SubQueryExpression nested)
                {
                    source = nested.QueryModel.MainFromClause.FromExpression;
                }

                var root = Evaluate(source) as IQueryable;

                if (root?.Provider is QueryProviderBase rootProvider)
                {
                    return new JoinInnerSequence(rootProvider.Executor, subQuery.QueryModel.Clone(), null);
                }

                throw new NotSupportedException("The inner sequence of a join must be a Lucene query or an in-memory sequence: " + join.InnerSequence);
            }

            var value = Evaluate(join.InnerSequence);

            if (value is IQueryable queryable && queryable.Provider is QueryProviderBase provider)
            {
                return new JoinInnerSequence(provider.Executor, provider.GenerateQueryModel(queryable.Expression), null);
            }

            return new JoinInnerSequence(null, null, value as IEnumerable ?? new object[0]);
        }

        private static object Evaluate(Expression expression)
        {
            return Expression.Lambda<Func<object>>(Expression.Convert(expression, typeof(object))).Compile()();
        }
    }
}
