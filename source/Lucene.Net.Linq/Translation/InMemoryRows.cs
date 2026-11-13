using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ExpressionVisitors;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing;

namespace Lucene.Net.Linq.Translation
{
    /// <summary>
    /// Applies parts of a query to rows that are already loaded, with LINQ to
    /// Objects semantics. A row is an array holding one object per query
    /// source (the main source, then each joined source), in the order of
    /// the <c>sources</c> list passed alongside it.
    /// </summary>
    internal static class InMemoryRows
    {
        /// <summary>
        /// Compiles an expression from a query model into a function of a row.
        /// </summary>
        public static Func<object[], TResult> Compile<TResult>(Expression body, IList<IQuerySource> sources)
        {
            if (SubQueryFinder.ContainsSubQuery(body))
            {
                throw new NotSupportedException("A sub-query cannot be evaluated on rows in memory: " + body);
            }

            var row = Expression.Parameter(typeof(object[]), "row");
            var mapping = new QuerySourceMapping();

            for (var i = 0; i < sources.Count; i++)
            {
                mapping.AddMapping(sources[i], Expression.Convert(Expression.ArrayIndex(row, Expression.Constant(i)), sources[i].ItemType));
            }

            body = ReferenceReplacingExpressionVisitor.ReplaceClauseReferences(body, mapping, throwOnUnmappedReferences: true);

            if (body.Type != typeof(TResult))
            {
                body = Expression.Convert(body, typeof(TResult));
            }

            return Expression.Lambda<Func<object[], TResult>>(body, row).Compile();
        }

        /// <summary>
        /// Applies <c>where</c> and <c>orderby</c> clauses to rows, in order.
        /// </summary>
        public static IEnumerable<object[]> Apply(IEnumerable<object[]> rows, IEnumerable<IBodyClause> clauses, IList<IQuerySource> sources)
        {
            foreach (var clause in clauses)
            {
                if (clause is WhereClause where)
                {
                    rows = rows.Where(Compile<bool>(where.Predicate, sources));
                }
                else if (clause is OrderByClause orderBy)
                {
                    // OrderBy is stable, so an earlier ordering survives as the tie-breaker.
                    IOrderedEnumerable<object[]> ordered = null;

                    foreach (var ordering in orderBy.Orderings)
                    {
                        var key = Compile<object>(ordering.Expression, sources);
                        var descending = ordering.OrderingDirection == OrderingDirection.Desc;

                        if (ordered == null)
                        {
                            ordered = descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
                        }
                        else
                        {
                            ordered = descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
                        }
                    }

                    rows = ordered ?? rows;
                }
                else
                {
                    throw new NotSupportedException("Clause " + clause.GetType().Name + " cannot be applied to rows in memory.");
                }
            }

            return rows;
        }

        /// <summary>
        /// Whether <paramref name="clause"/> mentions any of <paramref name="sources"/>.
        /// </summary>
        public static bool References(IBodyClause clause, ICollection<IQuerySource> sources)
        {
            var finder = new ReferenceFinder(sources);
            clause.TransformExpressions(e => { finder.Visit(e); return e; });
            return finder.Found;
        }

        /// <summary>
        /// The index of the first result operator that has to run on loaded
        /// rows because the Lucene query path does not implement it or would
        /// give a different answer, or -1 when there is none. That operator and
        /// everything after it are applied in memory.
        /// </summary>
        public static int FindInMemoryTail(IList<ResultOperatorBase> operators)
        {
            var paged = false;

            for (var i = 0; i < operators.Count; i++)
            {
                var op = operators[i];

                if (op is SumResultOperator || op is AverageResultOperator || op is DistinctResultOperator
                    || op is ContainsResultOperator || op is AllResultOperator
                    || op is AggregateResultOperator || op is AggregateFromSeedResultOperator)
                {
                    return i;
                }

                // Lucene finds a minimum or maximum by sorting the whole match set,
                // which ignores paging that came first.
                if (paged && (op is MinResultOperator || op is MaxResultOperator))
                {
                    return i;
                }

                paged |= op is SkipResultOperator || op is TakeResultOperator;
            }

            return -1;
        }

        private sealed class ReferenceFinder : RelinqExpressionVisitor
        {
            private readonly ICollection<IQuerySource> sources;

            public ReferenceFinder(ICollection<IQuerySource> sources)
            {
                this.sources = sources;
            }

            public bool Found { get; private set; }

            protected override Expression VisitQuerySourceReference(QuerySourceReferenceExpression expression)
            {
                Found |= sources.Contains(expression.ReferencedQuerySource);
                return expression;
            }

            protected override Expression VisitSubQuery(SubQueryExpression expression)
            {
                return expression;
            }
        }

        private sealed class SubQueryFinder : RelinqExpressionVisitor
        {
            private bool found;

            public static bool ContainsSubQuery(Expression expression)
            {
                var finder = new SubQueryFinder();
                finder.Visit(expression);
                return finder.found;
            }

            protected override Expression VisitQuerySourceReference(QuerySourceReferenceExpression expression)
            {
                return expression;
            }

            protected override Expression VisitSubQuery(SubQueryExpression expression)
            {
                found = true;
                return expression;
            }
        }
    }
}
