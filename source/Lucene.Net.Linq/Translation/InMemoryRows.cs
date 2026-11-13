using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Lucene.Net.Linq.Mapping;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;
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
            var row = Expression.Parameter(typeof(object[]), "row");

            body = Bind(body, sources, row);

            if (body.Type != typeof(TResult))
            {
                body = Expression.Convert(body, typeof(TResult));
            }

            return Expression.Lambda<Func<object[], TResult>>(body, row).Compile();
        }

        /// <summary>
        /// Rewrites an expression from a query model so that it reads its query
        /// sources from <paramref name="row"/> and evaluates sub-queries over
        /// in-memory collections with LINQ to Objects.
        /// </summary>
        private static Expression Bind(Expression body, IList<IQuerySource> sources, ParameterExpression row)
        {
            var binder = new RowBinder();

            for (var i = 0; i < sources.Count; i++)
            {
                binder.Map(sources[i], Expression.Convert(Expression.ArrayIndex(row, Expression.Constant(i)), sources[i].ItemType));
            }

            return binder.Visit(body);
        }

        /// <summary>
        /// Applies <c>where</c> and <c>orderby</c> clauses to rows, in order.
        /// <paramref name="mappingOf"/> gives the field mapping of an ordering
        /// key that is a mapped property, so that strings are ordered the way
        /// a Lucene sort on that field orders them.
        /// </summary>
        public static IEnumerable<object[]> Apply(IEnumerable<object[]> rows, IEnumerable<IBodyClause> clauses, IList<IQuerySource> sources,
            Func<Expression, ITermKeyMappingInfo> mappingOf = null)
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
                        var mapping = ordering.Expression.Type == typeof(string) ? mappingOf?.Invoke(ordering.Expression) : null;

                        if (mapping != null)
                        {
                            var value = key;
                            key = r => mapping.ConvertToSortKey(value(r));
                        }

                        // Strings are compared by ordinal, as a Lucene sort compares terms.
                        if (ordered == null)
                        {
                            ordered = descending ? rows.OrderByDescending(key, SortKeyComparer.Instance) : rows.OrderBy(key, SortKeyComparer.Instance);
                        }
                        else
                        {
                            ordered = descending ? ordered.ThenByDescending(key, SortKeyComparer.Instance) : ordered.ThenBy(key, SortKeyComparer.Instance);
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
        /// Applies result operators to rows whose items are produced by
        /// <paramref name="selector"/>. <c>All</c> and <c>Aggregate</c> are
        /// evaluated on the rows themselves: their lambdas are written in terms
        /// of the query sources, which cannot be recovered from a projected
        /// item (<c>Select(s =&gt; s.Rank).All(r =&gt; r &gt; 0)</c>).
        /// </summary>
        public static IStreamedData ApplyOperators(IEnumerable<object[]> rows, Expression selector, IList<IQuerySource> sources,
            StreamedSequenceInfo itemInfo, IList<ResultOperatorBase> operators)
        {
            var next = 0;

            // Operators that keep the rows apart, so a later All or Aggregate still has them.
            for (; next < operators.Count; next++)
            {
                var op = operators[next];

                if (op is SkipResultOperator skip)
                {
                    rows = rows.Skip(skip.GetConstantCount());
                }
                else if (op is TakeResultOperator take)
                {
                    rows = rows.Take(take.GetConstantCount());
                }
                else if (op is DistinctResultOperator)
                {
                    // The first row of each distinct item, in order, as Distinct yields them.
                    rows = rows.GroupBy(Compile<object>(selector, sources)).Select(g => g.First());
                }
                else if (op is AllResultOperator all)
                {
                    return new StreamedValue(rows.All(Compile<bool>(all.Predicate, sources)), new StreamedScalarValueInfo(typeof(bool)));
                }
                else if (op is AggregateResultOperator aggregate)
                {
                    var item = Compile<object>(selector, sources);
                    var step = CompileStep(aggregate.Func, sources);

                    using (var enumerator = rows.GetEnumerator())
                    {
                        if (!enumerator.MoveNext())
                        {
                            throw new InvalidOperationException("Sequence contains no elements");
                        }

                        var accumulated = item(enumerator.Current);

                        while (enumerator.MoveNext())
                        {
                            accumulated = step(accumulated, enumerator.Current);
                        }

                        return new StreamedValue(accumulated, new StreamedScalarValueInfo(aggregate.Func.Body.Type));
                    }
                }
                else if (op is AggregateFromSeedResultOperator fromSeed)
                {
                    var step = CompileStep(fromSeed.Func, sources);
                    var accumulated = Expression.Lambda<Func<object>>(Expression.Convert(fromSeed.Seed, typeof(object))).Compile()();

                    foreach (var row in rows)
                    {
                        accumulated = step(accumulated, row);
                    }

                    if (fromSeed.OptionalResultSelector == null)
                    {
                        return new StreamedValue(accumulated, new StreamedScalarValueInfo(fromSeed.Func.Body.Type));
                    }

                    return new StreamedValue(fromSeed.OptionalResultSelector.Compile().DynamicInvoke(accumulated),
                        new StreamedScalarValueInfo(fromSeed.OptionalResultSelector.Body.Type));
                }
                else
                {
                    break;
                }
            }

            IStreamedData data;

            try
            {
                data = (IStreamedData)ProjectMethod.MakeGenericMethod(itemInfo.ResultItemType).Invoke(null, new object[] { rows, selector, sources, itemInfo });
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }

            for (; next < operators.Count; next++)
            {
                data = operators[next].ExecuteInMemory(data);
            }

            return data;
        }

        private static readonly MethodInfo ProjectMethod =
            typeof(InMemoryRows).GetMethod(nameof(Project), BindingFlags.Static | BindingFlags.NonPublic);

        private static StreamedSequence Project<T>(IEnumerable<object[]> rows, Expression selector, IList<IQuerySource> sources, StreamedSequenceInfo info)
        {
            return new StreamedSequence(rows.Select(Compile<T>(selector, sources)).AsQueryable(), info);
        }

        /// <summary>
        /// Compiles the function of an Aggregate, whose one parameter is the
        /// accumulated value and whose body reads the next item from the query
        /// sources, into a function of the accumulated value and a row.
        /// </summary>
        private static Func<object, object[], object> CompileStep(LambdaExpression func, IList<IQuerySource> sources)
        {
            var accumulated = Expression.Parameter(typeof(object), "accumulated");
            var row = Expression.Parameter(typeof(object[]), "row");
            var body = Bind(Expression.Invoke(func, Expression.Convert(accumulated, func.Parameters[0].Type)), sources, row);

            return Expression.Lambda<Func<object, object[], object>>(Expression.Convert(body, typeof(object)), accumulated, row).Compile();
        }

        /// <summary>
        /// Whether <paramref name="clause"/> mentions any of <paramref name="sources"/>.
        /// </summary>
        public static bool References(IBodyClause clause, ICollection<IQuerySource> sources)
        {
            var finder = new ReferenceFinder(sources);
            clause.TransformExpressions(finder.Keep);
            return finder.Found;
        }

        /// <summary>
        /// Whether <paramref name="expression"/> mentions any of <paramref name="sources"/>.
        /// </summary>
        public static bool References(Expression expression, ICollection<IQuerySource> sources)
        {
            var finder = new ReferenceFinder(sources);
            finder.Visit(expression);
            return finder.Found;
        }

        /// <summary>
        /// Whether <paramref name="resultOperator"/> mentions any of <paramref name="sources"/>.
        /// </summary>
        public static bool References(ResultOperatorBase resultOperator, ICollection<IQuerySource> sources)
        {
            var finder = new ReferenceFinder(sources);
            resultOperator.TransformExpressions(finder.Keep);
            return finder.Found;
        }

        /// <summary>
        /// The result operators the Lucene query path implements.
        /// </summary>
        public static bool RunsInLucene(ResultOperatorBase op)
        {
            return op is TakeResultOperator || op is SkipResultOperator
                || op is FirstResultOperator || op is LastResultOperator || op is SingleResultOperator
                || op is MinResultOperator || op is MaxResultOperator
                || op is CountResultOperator || op is LongCountResultOperator || op is AnyResultOperator;
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

                // Sum, Average, Distinct, Contains, All, Aggregate, DefaultIfEmpty, Cast…:
                // the Lucene path has no handler for these and would ignore the operator or fail on it.
                if (!RunsInLucene(op))
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

            public Expression Keep(Expression expression)
            {
                Visit(expression);
                return expression;
            }

            protected override Expression VisitQuerySourceReference(QuerySourceReferenceExpression expression)
            {
                Found |= sources.Contains(expression.ReferencedQuerySource);
                return expression;
            }

            protected override Expression VisitSubQuery(SubQueryExpression expression)
            {
                // ids.Contains(s.Id) is a sub-query that mentions s.
                expression.QueryModel.TransformExpressions(Keep);
                return expression;
            }
        }

        /// <summary>
        /// Orders strings by ordinal, as Lucene orders terms, and everything
        /// else by its default comparer. Nulls come first.
        /// </summary>
        private sealed class SortKeyComparer : IComparer<object>
        {
            public static readonly SortKeyComparer Instance = new SortKeyComparer();

            public int Compare(object x, object y)
            {
                if (x is string || y is string)
                {
                    return string.CompareOrdinal(x as string, y as string);
                }

                return Comparer<object>.Default.Compare(x, y);
            }
        }

        /// <summary>
        /// Replaces query source references with values read from a row, and
        /// sub-queries over in-memory collections (<c>ids.Contains(s.Id)</c>,
        /// <c>s.Tags.Any(t =&gt; …)</c>) with the LINQ to Objects calls they
        /// stand for.
        /// </summary>
        private sealed class RowBinder : RelinqExpressionVisitor
        {
            private readonly Dictionary<IQuerySource, Expression> values = new Dictionary<IQuerySource, Expression>();

            public void Map(IQuerySource source, Expression value)
            {
                values[source] = value;
            }

            protected override Expression VisitQuerySourceReference(QuerySourceReferenceExpression expression)
            {
                if (!values.TryGetValue(expression.ReferencedQuerySource, out var value))
                {
                    throw new NotSupportedException("Query source " + expression + " is not available on rows in memory.");
                }

                return value;
            }

            protected override Expression VisitSubQuery(SubQueryExpression expression)
            {
                var model = expression.QueryModel;
                var from = model.MainFromClause;
                var sequence = Visit(from.FromExpression);

                // Only a sub-query over a collection that is at hand can be evaluated here.
                if (sequence is ConstantExpression constant && constant.Value is IQueryable queryable && queryable.Provider is QueryProviderBase)
                {
                    throw NotSupported(expression, "its source is another Lucene query");
                }

                var itemType = from.ItemType;

                if (!typeof(IEnumerable<>).MakeGenericType(itemType).IsAssignableFrom(sequence.Type))
                {
                    if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(sequence.Type))
                    {
                        throw NotSupported(expression, "its source is not a collection");
                    }

                    sequence = Expression.Call(typeof(Enumerable), nameof(Enumerable.Cast), new[] { itemType }, sequence);
                }

                var item = Expression.Parameter(itemType, from.ItemName);
                values[from] = item;

                foreach (var clause in model.BodyClauses)
                {
                    if (!(clause is WhereClause where))
                    {
                        throw NotSupported(expression, "it has a " + clause.GetType().Name);
                    }

                    sequence = Expression.Call(typeof(Enumerable), nameof(Enumerable.Where), new[] { itemType },
                        sequence, Expression.Lambda(Visit(where.Predicate), item));
                }

                var selector = model.SelectClause.Selector;

                if (!(selector is QuerySourceReferenceExpression reference && reference.ReferencedQuerySource == from))
                {
                    sequence = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select), new[] { itemType, selector.Type },
                        sequence, Expression.Lambda(Visit(selector), item));
                    itemType = selector.Type;
                }

                var result = sequence;

                for (var i = 0; i < model.ResultOperators.Count; i++)
                {
                    var op = model.ResultOperators[i];

                    if (i < model.ResultOperators.Count - 1)
                    {
                        throw NotSupported(expression, "it has more than one result operator");
                    }

                    if (op is ContainsResultOperator contains)
                    {
                        result = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { itemType }, sequence, Visit(contains.Item));
                    }
                    else if (op is AnyResultOperator)
                    {
                        result = Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), new[] { itemType }, sequence);
                    }
                    else if (op is AllResultOperator all && itemType == from.ItemType)
                    {
                        // The predicate reads the sub-query's source, so it could not follow a Select.
                        result = Expression.Call(typeof(Enumerable), nameof(Enumerable.All), new[] { itemType }, sequence, Expression.Lambda(Visit(all.Predicate), item));
                    }
                    else if (op is CountResultOperator)
                    {
                        result = Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), new[] { itemType }, sequence);
                    }
                    else
                    {
                        throw NotSupported(expression, "it has a " + op.GetType().Name);
                    }
                }

                return result.Type == expression.Type ? result : Expression.Convert(result, expression.Type);
            }

            private static NotSupportedException NotSupported(SubQueryExpression expression, string reason)
            {
                return new NotSupportedException("A sub-query cannot be evaluated on rows in memory (" + reason + "): " + expression);
            }
        }
    }
}
