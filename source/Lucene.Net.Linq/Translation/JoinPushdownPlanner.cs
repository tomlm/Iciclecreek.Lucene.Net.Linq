using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Lucene.Net.Linq.Clauses;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Linq.Search;
using Lucene.Net.Search;
using Microsoft.Extensions.Logging;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ExpressionVisitors;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing;

namespace Lucene.Net.Linq.Translation
{
    /// <summary>
    /// What a query executor exposes so that another executor on the same
    /// index can use its query as one side of a join.
    /// </summary>
    internal interface IJoinSource : IFieldMappingInfoProvider, IQueryExecutor
    {
        Context JoinContext { get; }

        /// <summary>
        /// Translates a query model for this executor's document type.
        /// </summary>
        LuceneQueryModel PrepareJoinQuery(QueryModel queryModel);
    }

    /// <summary>
    /// Moves LINQ joins into Lucene where the query's shape allows it.
    /// <para>
    /// A join whose result uses only one of its two sides is a semi-join: the
    /// unused side only decides which documents of the used side are kept.
    /// Such a join is replaced by a <see cref="JoinFilterClause"/>, which the
    /// executor turns into a query-time join (<c>JoinUtil.CreateJoinQuery</c>)
    /// once it holds a searcher. The unused side's documents are never
    /// loaded, and paging, sorting and counting run inside Lucene.
    /// </para>
    /// <para>
    /// Joins that don't qualify are left in the query model for the in-memory
    /// join, and the reason is logged at debug level. When the keys and the
    /// inner sequence qualify but the join still cannot be replaced (the
    /// result uses both sides, a row could repeat, a side is ordered…), a
    /// <see cref="JoinFilterClause"/> is added in front of it, so the outer
    /// side is loaded only where the inner side matches.
    /// </para>
    /// </summary>
    internal sealed class JoinPushdownPlanner
    {
        private readonly IJoinSource self;
        private readonly ILogger log;

        public JoinPushdownPlanner(IJoinSource self, ILogger log)
        {
            this.self = self;
            this.log = log;
        }

        /// <summary>
        /// A query to run on another executor instead: the join's inner side
        /// is the one the result uses.
        /// </summary>
        internal sealed class Delegation
        {
            public Delegation(IQueryExecutor executor, QueryModel queryModel)
            {
                Executor = executor;
                QueryModel = queryModel;
            }

            public IQueryExecutor Executor { get; }
            public QueryModel QueryModel { get; }
        }

        /// <summary>
        /// Rewrites <paramref name="queryModel"/> in place for joins that can
        /// filter the outer side, or returns a <see cref="Delegation"/> when
        /// the query should run on the inner side's executor. Returns
        /// <c>null</c> when the caller should carry on with
        /// <paramref name="queryModel"/>.
        /// </summary>
        public Delegation Plan(QueryModel queryModel)
        {
            if (queryModel.BodyClauses.OfType<GroupJoinClause>().Any())
            {
                Skipped("group joins and outer joins are not pushed down");
                return null;
            }

            var joins = queryModel.BodyClauses.OfType<JoinClause>().ToList();
            if (joins.Count == 0) return null;

            if (!self.JoinContext.Settings.EnableJoinPushdown)
            {
                Skipped("join pushdown is disabled");
                return null;
            }

            // An outer source such as stories.Skip(1).Take(2) pages before the
            // join; a join filter would be applied ahead of that paging.
            if (queryModel.MainFromClause.FromExpression is SubQueryExpression)
            {
                Skipped("the outer source has paging or other operators that must run before the join");
                return null;
            }

            var inspection = Inspect(queryModel);

            if (inspection.HasSubQuery)
            {
                Skipped("the query contains a subquery");
                return null;
            }

            // A vector query picks its nearest neighbours before any filter applies.
            if (inspection.HasSimilar)
            {
                Skipped("the query has a vector (Similar) clause");
                return null;
            }

            foreach (var join in joins)
            {
                string reason;
                Delegation delegation;

                if (TryPushDown(queryModel, join, joins.Count, out delegation, out reason))
                {
                    if (delegation != null) return delegation;
                }
                else
                {
                    Skipped(reason);
                }
            }

            return null;
        }

        private bool TryPushDown(QueryModel queryModel, JoinClause join, int joinCount, out Delegation delegation, out string reason)
        {
            delegation = null;

            var main = queryModel.MainFromClause;
            var joinIndex = queryModel.BodyClauses.IndexOf(join);
            var operators = queryModel.ResultOperators;

            // Count and Any never look at the rows, so what the select mentions is irrelevant to them.
            var rowsAreRead = !(operators.Count > 0
                && (operators[operators.Count - 1] is CountResultOperator
                    || operators[operators.Count - 1] is LongCountResultOperator
                    || (operators[operators.Count - 1] is AnyResultOperator))
                && operators.Take(operators.Count - 1).All(o => o is SkipResultOperator || o is TakeResultOperator));

            // A lone Any is also indifferent to how often a row repeats.
            var multiplicityMatters = !(operators.Count == 1 && operators[0] is AnyResultOperator);

            // Which sides does the rest of the query mention, apart from this join's own keys?
            var before = new SourceReferences();
            var after = new SourceReferences();

            for (var i = 0; i < queryModel.BodyClauses.Count; i++)
            {
                var clause = queryModel.BodyClauses[i];
                if (clause == join) continue;
                (i < joinIndex ? before : after).Collect(clause);
            }

            if (rowsAreRead) after.Collect(queryModel.SelectClause.Selector);
            foreach (var op in operators) after.Collect(op);

            var joinUsed = after.Contains(join) || before.Contains(join);
            var mainUsedAfter = after.Contains(main);

            var innerSequence = JoinInnerSequence.Resolve(join);
            var inner = innerSequence.Executor as IJoinSource;

            if (inner == null)
            {
                reason = "the inner sequence is not a Lucene query";
                return false;
            }

            if (!ReferenceEquals(inner.JoinContext, self.JoinContext))
            {
                reason = "the two sides use different searchers";
                return false;
            }

            // Both key selectors must be a plain mapped property: of the main source and of the joined source.
            var outerKey = GetKeyMapping(join.OuterKeySelector, main, self);
            var innerKey = GetKeyMapping(join.InnerKeySelector, join, inner);

            if (outerKey == null || innerKey == null)
            {
                reason = "a join key is composite, computed, not a mapped property, or belongs to another joined source";
                return false;
            }

            if (outerKey.JoinTermForm == JoinTermForm.None || innerKey.JoinTermForm == JoinTermForm.None)
            {
                var field = outerKey.JoinTermForm == JoinTermForm.None ? outerKey : innerKey;
                reason = "key field " + field.FieldName + " is not indexed as one string term per document (it is numeric, converted, multi-valued, analyzed into several terms, or lowercased with doc values)";
                return false;
            }

            if (outerKey.JoinTermForm != innerKey.JoinTermForm)
            {
                reason = "key fields " + outerKey.FieldName + " and " + innerKey.FieldName + " are indexed differently (one is case-sensitive, the other is not)";
                return false;
            }

            var innerModel = innerSequence.QueryModel;

            if (innerModel.ResultOperators.Count > 0 || innerModel.MainFromClause.FromExpression is SubQueryExpression)
            {
                reason = "the inner sequence has result operators (Take, Skip, …)";
                return false;
            }

            if (!IsIdentitySelect(innerModel))
            {
                reason = "the inner sequence has a projection";
                return false;
            }

            var innerInspection = Inspect(innerModel);

            if (innerModel.BodyClauses.OfType<JoinClause>().Any() || innerInspection.HasSubQuery)
            {
                reason = "the inner sequence has joins or subqueries of its own";
                return false;
            }

            if (innerInspection.HasSimilar)
            {
                reason = "the inner sequence has a vector (Similar) clause";
                return false;
            }

            if (TryReplaceJoin(queryModel, join, joinIndex, joinCount, inner, innerModel, outerKey, innerKey,
                joinUsed, mainUsedAfter, rowsAreRead, multiplicityMatters, out delegation, out reason))
            {
                return true;
            }

            // The join itself has to run in memory, but the keys qualify, so the
            // outer side can still be cut down to the documents that will join.
            if (TryNarrowOuterSide(queryModel, join, inner, outerKey, innerKey))
            {
                reason += "; narrowed: " + self.MappedType.Name + " is loaded only where " + inner.MappedType.Name + " matches";
            }

            return false;
        }

        /// <summary>
        /// Replaces the join with a filter when the result uses one side only.
        /// </summary>
        private bool TryReplaceJoin(QueryModel queryModel, JoinClause join, int joinIndex, int joinCount, IJoinSource inner, QueryModel innerModel,
            ITermKeyMappingInfo outerKey, ITermKeyMappingInfo innerKey, bool joinUsed, bool mainUsedAfter, bool rowsAreRead, bool multiplicityMatters,
            out Delegation delegation, out string reason)
        {
            delegation = null;

            // Once the join is a filter, the whole query runs in Lucene, so
            // every result operator must be one the Lucene path runs with
            // LINQ's meaning. The executor takes the others off the query
            // before it plans the join; this holds the planner to the same rule.
            var inMemoryTail = InMemoryRows.FindInMemoryTail(queryModel.ResultOperators);

            if (inMemoryTail >= 0)
            {
                reason = "result operator " + queryModel.ResultOperators[inMemoryTail].GetType().Name.Replace("ResultOperator", "") + " only runs on rows in memory";
                return false;
            }

            if (!joinUsed)
            {
                if (TryFilterOuterSide(queryModel, join, joinIndex, inner, innerModel, outerKey, innerKey, multiplicityMatters, out reason))
                {
                    return true;
                }

                if (mainUsedAfter || joinCount > 1)
                {
                    return false;
                }

                // Nothing reads either side's rows (a Count, or a select of
                // constants): the query can run on the inner side instead,
                // which needs the outer key to be the unique one.
                string ignored;
                return TryRunOnInnerSide(queryModel, join, joinIndex, inner, JoinInnerSequence.Resolve(join).QueryModel,
                    outerKey, innerKey, rowsAreRead, multiplicityMatters, out delegation, out ignored);
            }

            if (mainUsedAfter)
            {
                reason = "the result uses both sides of the join";
                return false;
            }

            if (joinCount > 1)
            {
                reason = "the result uses the inner side of one of several joins";
                return false;
            }

            return TryRunOnInnerSide(queryModel, join, joinIndex, inner, innerModel, outerKey, innerKey, rowsAreRead, multiplicityMatters, out delegation, out reason);
        }

        /// <summary>
        /// Keeps the join for the in-memory path but adds a filter in front of
        /// it, so only outer documents with a matching inner document are
        /// loaded. An inner join drops the others anyway, so the rows are the
        /// same whatever the keys' uniqueness; a filter on the inner side now
        /// cuts the outer load too.
        /// </summary>
        private bool TryNarrowOuterSide(QueryModel queryModel, JoinClause join, IJoinSource inner, ITermKeyMappingInfo outerKey, ITermKeyMappingInfo innerKey)
        {
            // A private copy: earlier attempts may have translated the first one.
            var innerModel = JoinInnerSequence.Resolve(join).QueryModel;

            if (innerModel.BodyClauses.Any(c => !(c is WhereClause) && !(c is TrackRetrievedDocumentsClause) && !(c is OrderByClause)))
            {
                return false;
            }

            string ignored;
            var fromQuery = PrepareFromQuery(inner, innerModel, innerKey.FieldName, out ignored);
            if (fromQuery == null) return false;

            queryModel.BodyClauses.Insert(queryModel.BodyClauses.IndexOf(join),
                new JoinFilterClause(new JoinFilter(fromQuery, innerKey.FieldName, outerKey.FieldName)));

            return true;
        }

        /// <summary>
        /// The result ignores the inner side: keep outer documents whose key
        /// appears among the inner side's keys.
        /// </summary>
        private bool TryFilterOuterSide(QueryModel queryModel, JoinClause join, int joinIndex, IJoinSource inner, QueryModel innerModel,
            ITermKeyMappingInfo outerKey, ITermKeyMappingInfo innerKey, bool multiplicityMatters, out string reason)
        {
            // Each outer row repeats once per matching inner document, which a filter cannot reproduce.
            if (multiplicityMatters && !IsSoleKey(inner, innerKey))
            {
                reason = "inner key " + innerKey.PropertyName + " is not the only key of " + inner.MappedType.Name + ", so an outer row may repeat";
                return false;
            }

            // The inner query only selects documents; anything else on it has no meaning in a filter.
            if (innerModel.BodyClauses.Any(c => !(c is WhereClause) && !(c is TrackRetrievedDocumentsClause) && !(c is OrderByClause)))
            {
                reason = "the inner sequence has clauses that cannot run as a join filter";
                return false;
            }

            var fromQuery = PrepareFromQuery(inner, innerModel, innerKey.FieldName, out reason);
            if (fromQuery == null) return false;

            queryModel.BodyClauses[joinIndex] = new JoinFilterClause(new JoinFilter(fromQuery, innerKey.FieldName, outerKey.FieldName));

            Report("pushed down: " + self.MappedType.Name + " filtered by " + inner.MappedType.Name
                + " on " + outerKey.FieldName + " = " + innerKey.FieldName);
            return true;
        }

        /// <summary>
        /// The result uses only the inner side: run the query on the inner
        /// side's executor, keeping inner documents whose key appears among
        /// the outer side's keys.
        /// </summary>
        private bool TryRunOnInnerSide(QueryModel queryModel, JoinClause join, int joinIndex, IJoinSource inner, QueryModel innerModel,
            ITermKeyMappingInfo outerKey, ITermKeyMappingInfo innerKey, bool rowsAreRead, bool multiplicityMatters,
            out Delegation delegation, out string reason)
        {
            delegation = null;

            var innerClauses = queryModel.BodyClauses.Skip(joinIndex + 1).ToList();

            // In a LINQ join the rows follow the outer side's order, and the
            // inner sequence's order only arranges the matches of one outer
            // row. Run on the inner side's query, that order would instead
            // arrange all the rows (or break all the ties of a later orderby),
            // so an ordered inner sequence is joined in memory.
            if (innerModel.BodyClauses.OfType<OrderByClause>().Any())
            {
                reason = "the inner sequence is ordered";
                return false;
            }

            if (multiplicityMatters && !IsSoleKey(self, outerKey))
            {
                reason = "outer key " + outerKey.PropertyName + " is not the only key of " + self.MappedType.Name + ", so an inner row may repeat";
                return false;
            }

            // Before the join, the outer side may only be filtered: its order
            // decides the order of the rows, and facets, boosts or statistics
            // on it cannot be produced from a filter.
            var outerClauses = queryModel.BodyClauses.Take(joinIndex).ToList();

            if (outerClauses.Any(c => !(c is WhereClause) && !(c is TrackRetrievedDocumentsClause)))
            {
                reason = "the outer side is ordered or has clauses that cannot run as a join filter";
                return false;
            }

            if (innerClauses.OfType<OrderByClause>().Any(o => o.Orderings.Any(x => OrdersByScore(x.Expression))))
            {
                reason = "the query orders the inner side by score";
                return false;
            }

            // The outer side becomes the "from" query: a copy of this query cut off at the join.
            var fromModel = queryModel.Clone();
            while (fromModel.BodyClauses.Count > joinIndex) fromModel.BodyClauses.RemoveAt(fromModel.BodyClauses.Count - 1);
            fromModel.ResultOperators.Clear();
            fromModel.SelectClause.Selector = new QuerySourceReferenceExpression(fromModel.MainFromClause);

            var fromQuery = PrepareFromQuery(self, fromModel, outerKey.FieldName, out reason);
            if (fromQuery == null) return false;

            // The rest of the query moves onto the inner side's own query.
            var toInner = new QuerySourceMapping();
            toInner.AddMapping(join, new QuerySourceReferenceExpression(innerModel.MainFromClause));
            Func<Expression, Expression> rewrite = e => ReferenceReplacingExpressionVisitor.ReplaceClauseReferences(e, toInner, throwOnUnmappedReferences: false);

            innerModel.BodyClauses.Add(new JoinFilterClause(new JoinFilter(fromQuery, outerKey.FieldName, innerKey.FieldName)));

            foreach (var clause in innerClauses)
            {
                clause.TransformExpressions(rewrite);
                innerModel.BodyClauses.Add(clause);
            }

            innerModel.SelectClause.Selector = rowsAreRead
                ? rewrite(queryModel.SelectClause.Selector)
                : new QuerySourceReferenceExpression(innerModel.MainFromClause);

            foreach (var op in queryModel.ResultOperators)
            {
                op.TransformExpressions(rewrite);
                innerModel.ResultOperators.Add(op);
            }

            innerModel.ResultTypeOverride = queryModel.ResultTypeOverride;

            Report("pushed down: " + inner.MappedType.Name + " filtered by " + self.MappedType.Name
                + " on " + innerKey.FieldName + " = " + outerKey.FieldName);

            delegation = new Delegation(inner, innerModel);
            return true;
        }

        /// <summary>
        /// The Lucene query matching one side's documents, or <c>null</c> with
        /// a <paramref name="reason"/> when it cannot be run as a plain query.
        /// </summary>
        private static Query PrepareFromQuery(IJoinSource source, QueryModel model, string keyField, out string reason)
        {
            var prepared = source.PrepareJoinQuery(model);

            if (ContainsVectorQuery(prepared.Query))
            {
                reason = "the side that would become a filter has a vector (Similar) clause";
                return null;
            }

            reason = null;

            var combined = new BooleanQuery();
            combined.Add(prepared.Query, Occur.MUST);

            if (prepared.Filter != null)
            {
                combined.Add(new ConstantScoreQuery(prepared.Filter), Occur.MUST);
            }

            // A document without the key joins nothing. The join reads keys
            // through FieldCache, which reports a missing key as an empty term.
            combined.Add(new ConstantScoreQuery(new FieldValueFilter(keyField)), Occur.MUST);

            return combined;
        }

        private static bool ContainsVectorQuery(Query query)
        {
            if (query is DeferredVectorQuery) return true;

            return query is BooleanQuery boolean && boolean.GetClauses().Any(c => ContainsVectorQuery(c.Query));
        }

        /// <summary>
        /// The mapping of a key selector of the form <c>source.Property</c>.
        /// </summary>
        private static ITermKeyMappingInfo GetKeyMapping(Expression keySelector, IQuerySource source, IFieldMappingInfoProvider provider)
        {
            if (!(keySelector is MemberExpression member)
                || !(member.Expression is QuerySourceReferenceExpression reference)
                || reference.ReferencedQuerySource != source)
            {
                return null;
            }

            try
            {
                return provider.GetMappingInfo(member.Member.Name) as ITermKeyMappingInfo;
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        /// Whether <paramref name="key"/> is the only key property of its type
        /// (constant <c>[DocumentKey]</c> fields aside), which is what makes a
        /// key value identify at most one document.
        /// </summary>
        private static bool IsSoleKey(IFieldMappingInfoProvider provider, ITermKeyMappingInfo key)
        {
            var keys = provider.KeyProperties
                .Where(name => !IsConstantKey(provider.GetMappingInfo(name)))
                .ToList();

            return keys.Count == 1 && keys[0] == key.PropertyName;
        }

        private static bool IsConstantKey(IFieldMappingInfo mapping)
        {
            var type = mapping.GetType();
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DocumentKeyFieldMapper<>);
        }

        private static bool IsIdentitySelect(QueryModel model)
        {
            return model.SelectClause.Selector is QuerySourceReferenceExpression reference
                && reference.ReferencedQuerySource == model.MainFromClause;
        }

        private static bool OrdersByScore(Expression expression)
        {
            return expression is MethodCallExpression call && call.Method.DeclaringType == typeof(LuceneMethods) && call.Method.Name == nameof(LuceneMethods.Score);
        }

        private static SourceReferences Inspect(QueryModel model)
        {
            var references = new SourceReferences();
            foreach (var clause in model.BodyClauses) references.Collect(clause);
            references.Collect(model.SelectClause.Selector);
            foreach (var op in model.ResultOperators) references.Collect(op);
            return references;
        }

        private void Skipped(string reason)
        {
            Report("skipped: " + reason);
        }

        private void Report(string message)
        {
            log.LogDebug("Join pushdown {Message}", message);
            self.JoinContext.Settings.JoinPushdownTrace?.Invoke(message);
        }

        /// <summary>
        /// Collects the query sources that expressions refer to.
        /// </summary>
        private sealed class SourceReferences : RelinqExpressionVisitor
        {
            private readonly HashSet<IQuerySource> sources = new HashSet<IQuerySource>();

            public bool HasSubQuery { get; private set; }

            public bool HasSimilar { get; private set; }

            public bool Contains(IQuerySource source) => sources.Contains(source);

            public void Collect(Expression expression)
            {
                if (expression != null) Visit(expression);
            }

            public void Collect(IBodyClause clause)
            {
                // Another join's inner sequence and inner key only concern that join.
                if (clause is JoinClause join)
                {
                    Collect(join.OuterKeySelector);
                    return;
                }

                clause.TransformExpressions(Keep);
            }

            public void Collect(ResultOperatorBase resultOperator)
            {
                resultOperator.TransformExpressions(Keep);
            }

            private Expression Keep(Expression expression)
            {
                Collect(expression);
                return expression;
            }

            protected override Expression VisitQuerySourceReference(QuerySourceReferenceExpression expression)
            {
                sources.Add(expression.ReferencedQuerySource);
                return expression;
            }

            protected override Expression VisitSubQuery(SubQueryExpression expression)
            {
                HasSubQuery = true;
                return expression;
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method.DeclaringType == typeof(LuceneMethods) && node.Method.Name == nameof(LuceneMethods.Similar))
                {
                    HasSimilar = true;
                }

                return base.VisitMethodCall(node);
            }
        }
    }
}
