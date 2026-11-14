using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using Iciclecreek.Lucene.Net.Vector;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Facet;
using Lucene.Net.Facet.Range;
using Lucene.Net.Facet.SortedSet;
using Lucene.Net.Index;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Linq.ScalarResultHandlers;
using Lucene.Net.Linq.Search;
using Lucene.Net.Linq.Search.Function;
using Lucene.Net.Linq.Transformation;
using Lucene.Net.Linq.Translation;
using Lucene.Net.Linq.Util;
using Lucene.Net.Search;
using Microsoft.Extensions.Logging;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ExpressionVisitors;
using Remotion.Linq.Clauses.StreamedData;

namespace Lucene.Net.Linq
{
    internal class LuceneQueryExecutor<TDocument> : LuceneQueryExecutorBase<TDocument>
    {
        private readonly ObjectLookup<TDocument> newItem;
        private readonly IDocumentMapper<TDocument> mapper;
        private readonly IDocumentKeyConverter keyConverter;

        public LuceneQueryExecutor(Context context, ObjectLookup<TDocument> newItem, IDocumentMapper<TDocument> mapper)
            : base(context)
        {
            this.newItem = newItem;
            this.mapper = mapper;
            this.keyConverter = mapper as IDocumentKeyConverter;
        }

        protected override TDocument ConvertDocument(Document doc, IQueryExecutionContext context)
        {
            var mapperBase = mapper as DocumentMapperBase<TDocument>;
            if (mapperBase != null)
            {
                var typeString = doc.Get(TypeUtils.TYPE_FIELD);
                var actualType = TypeUtils.ResolveType(typeString);
                return mapperBase.CreateFromDocument(doc, context, actualType, newItem);
            }

            // Fall back for custom IDocumentMapper implementations
            var key = keyConverter?.ToKey(doc);
            var item = newItem(key);
            mapper.ToObject(doc, context, item);
            return item;
        }
        
        protected override TDocument ConvertDocumentForCustomBoost(Document doc)
        {
            return ConvertDocument(doc, new QueryExecutionContext());
        }

        protected override IDocumentKey GetDocumentKey(Document doc, IQueryExecutionContext context)
        {
            if (keyConverter != null)
            {
                return keyConverter.ToKey(doc);
            }

            var item = ConvertDocument(doc, context);

            return mapper.ToKey(item);
        }

        public override IFieldMappingInfo GetMappingInfo(string propertyName)
        {
            return mapper.GetMappingInfo(propertyName);
        }

        public override IEnumerable<string> AllProperties
        {
            get { return mapper.AllProperties; }
        }

        public override IEnumerable<string> IndexedProperties
        {
            get { return mapper.IndexedProperties; }
        }

        public override IEnumerable<string> KeyProperties
        {
            get { return mapper.KeyProperties; }
        }

        public override Query CreateMultiFieldQuery(string pattern)
        {
            return mapper.CreateMultiFieldQuery(pattern);
        }

        protected override void PrepareSearchSettings(IQueryExecutionContext context)
        {
            mapper.PrepareSearchSettings(context);
        }

        public override string DefaultSearchProperty => mapper.DefaultSearchProperty;

        protected override Analyzer MapperAnalyzer => mapper.Analyzer;
    }

    internal abstract class LuceneQueryExecutorBase<TDocument> : IQueryExecutor, IFieldMappingInfoProvider, IJoinSource
    {
        private readonly ILogger Log = Logging.CreateLogger(typeof(LuceneQueryExecutorBase<>));

        private readonly Context context;
        
        protected LuceneQueryExecutorBase(Context context)
        {
            this.context = context;
        }

        Context IJoinSource.JoinContext => context;

        LuceneQueryModel IJoinSource.PrepareJoinQuery(QueryModel queryModel) => PrepareQuery(queryModel);

        public T ExecuteScalar<T>(QueryModel queryModel)
        {
            // Parts of the query Lucene cannot run with LINQ's meaning are applied to loaded rows.
            var inMemory = TryExecuteInMemory(queryModel);
            if (inMemory != null)
            {
                return (T)((StreamedValue)inMemory).Value;
            }

            // Joins whose result uses one side run inside Lucene; this may hand
            // the query to the inner side's executor.
            var delegation = new JoinPushdownPlanner(this, Log).Plan(queryModel);
            if (delegation != null)
            {
                return delegation.Executor.ExecuteScalar<T>(delegation.QueryModel);
            }

            // Count, Any and other scalars over a remaining join are computed from the joined rows.
            if (queryModel.BodyClauses.OfType<JoinClause>().Any())
            {
                return (T)((StreamedValue)ExecuteJoinedInMemory(queryModel)).Value;
            }

            var watch = new Stopwatch();
            watch.Start();

            var luceneQueryModel = PrepareQuery(queryModel);

            var searcherHandle = CheckoutSearcher();

            using (searcherHandle)
            {
                var searcher = searcherHandle.Searcher;
                var skipResults = luceneQueryModel.SkipResults;
                var maxResults = Math.Min(luceneQueryModel.MaxResults, searcher.IndexReader.MaxDoc - skipResults);

                var filter = ResolveJoinFilters(luceneQueryModel, searcher);
                var resolvedQuery = ResolveVectorQueries(luceneQueryModel, filter, searcher.IndexReader);
                var executionContext = new QueryExecutionContext(searcher, resolvedQuery, filter);
                TopFieldDocs hits;

                TimeSpan elapsedPreparationTime;
                TimeSpan elapsedSearchTime;

                var facetsCollector = luceneQueryModel.FacetQueries.Count > 0 ? new FacetsCollector() : null;

                if (maxResults > 0)
                {
                    PrepareSearchSettings(executionContext);

                    Log.LogDebug("Executing scalar query: {Query}, Filter: {Filter}, MaxResults: {MaxResults}, Sort: {Sort}",
                        executionContext.Query, executionContext.Filter, maxResults, luceneQueryModel.Sort);

                    elapsedPreparationTime = watch.Elapsed;

                    hits = Search(searcher, executionContext.Query, executionContext.Filter, maxResults, luceneQueryModel.Sort, facetsCollector);

                    elapsedSearchTime = watch.Elapsed - elapsedPreparationTime;
                }
                else
                {
                    hits = new TopFieldDocs(0, new ScoreDoc[0], new SortField[0], 0);
                    elapsedPreparationTime = watch.Elapsed;
                    CollectFacetsOnly(searcher, executionContext.Query, executionContext.Filter, facetsCollector);
                    elapsedSearchTime = watch.Elapsed - elapsedPreparationTime;
                }

                ComputeFacets(luceneQueryModel, searcherHandle, facetsCollector);

                executionContext.Phase = QueryExecutionPhase.ConvertResults;
                executionContext.Hits = hits;

                var handler = ScalarResultHandlerRegistry.Instance.GetItem(luceneQueryModel.ResultSetOperator.GetType());

                var result = handler.Execute<T>(luceneQueryModel, hits);

                var elapsedRetrievalTime = watch.Elapsed - elapsedPreparationTime - elapsedSearchTime;
                RaiseStatisticsCallback(luceneQueryModel, executionContext, elapsedPreparationTime, elapsedSearchTime, elapsedRetrievalTime, 0, 0);

                return result;
            }
        }

        public T ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var sequence = ExecuteCollection<T>(queryModel);

            return returnDefaultWhenEmpty ? sequence.SingleOrDefault() : sequence.Single();
        }

        public class ItemHolder
        {
            public TDocument Current { get; set; }
        }

        public IEnumerable<T> ExecuteCollection<T>(QueryModel queryModel)
        {
            // Parts of the query Lucene cannot run with LINQ's meaning are applied to loaded rows.
            var inMemory = TryExecuteInMemory(queryModel);
            if (inMemory != null)
            {
                if (inMemory is StreamedSequence rows)
                {
                    foreach (var item in rows.Sequence)
                        yield return (T)item;
                }
                else
                {
                    yield return (T)((StreamedValue)inMemory).Value;
                }
                yield break;
            }

            // Joins whose result uses one side run inside Lucene: the planner either
            // turns them into filters on this query or hands the query to the inner
            // side's executor. Joins it leaves in the model are joined in memory below.
            var delegation = new JoinPushdownPlanner(this, Log).Plan(queryModel);
            if (delegation != null)
            {
                foreach (var item in delegation.Executor.ExecuteCollection<T>(delegation.QueryModel))
                    yield return item;
                yield break;
            }

            if (queryModel.BodyClauses.OfType<JoinClause>().Any())
            {
                var joined = ExecuteJoinedInMemory(queryModel);

                if (joined is StreamedSequence sequence)
                {
                    foreach (var item in sequence.Sequence)
                        yield return (T)item;
                }
                else
                {
                    // First, Single, Min… arrive here through ExecuteSingle.
                    yield return (T)((StreamedValue)joined).Value;
                }
                yield break;
            }

            var watch = new Stopwatch();
            watch.Start();

            var itemHolder = new ItemHolder();

            var currentItemExpression = Expression.Property(Expression.Constant(itemHolder), "Current");

            var luceneQueryModel = PrepareQuery(queryModel);

            var mapping = new QuerySourceMapping();
            mapping.AddMapping(queryModel.MainFromClause, currentItemExpression);
            queryModel.TransformExpressions(e => ReferenceReplacingExpressionVisitor.ReplaceClauseReferences(e, mapping, throwOnUnmappedReferences: true));

            var projection = GetProjector<T>(queryModel);
            var projector = projection.Compile();

            var searcherHandle = CheckoutSearcher();

            using (searcherHandle)
            {
                var searcher = searcherHandle.Searcher;
                var skipResults = luceneQueryModel.SkipResults;
                var maxResults = Math.Min(luceneQueryModel.MaxResults, searcher.IndexReader.MaxDoc - skipResults);
                var filter = ResolveJoinFilters(luceneQueryModel, searcher);
                var query = ResolveVectorQueries(luceneQueryModel, filter, searcher.IndexReader);

                var scoreFunction = luceneQueryModel.GetCustomScoreFunction<TDocument>();
                if (scoreFunction != null)
                {
                    query = new DelegatingCustomScoreQuery<TDocument>(query, ConvertDocumentForCustomBoost, scoreFunction);
                }

                var executionContext = new QueryExecutionContext(searcher, query, filter);

                PrepareSearchSettings(executionContext);

                Log.LogDebug("Executing collection query: {Query}, Filter: {Filter}, MaxResults: {MaxResults}, Skip: {Skip}, Sort: {Sort}",
                    executionContext.Query, executionContext.Filter, maxResults, skipResults, luceneQueryModel.Sort);

                var elapsedPreparationTime = watch.Elapsed;
                // Lucene 4.8's IndexSearcher.Search throws ArgumentException
                // when nDocs is 0 (TopFieldCollector.Create requires
                // numHits > 0). Short-circuit on empty result sets so callers
                // like Min/Max see an empty enumerable rather than a crash.
                TopFieldDocs hits;
                // Facets are counted against the same (vector-resolved) query,
                // collected in the same search pass as the hits.
                var facetsCollector = luceneQueryModel.FacetQueries.Count > 0 ? new FacetsCollector() : null;
                if (maxResults + skipResults <= 0)
                {
                    hits = new TopFieldDocs(0, new ScoreDoc[0], new SortField[0], 0);
                    CollectFacetsOnly(searcher, executionContext.Query, executionContext.Filter, facetsCollector);
                }
                else
                {
                    hits = Search(searcher, executionContext.Query, executionContext.Filter, maxResults + skipResults, luceneQueryModel.Sort, facetsCollector);
                }
                var elapsedSearchTime = watch.Elapsed - elapsedPreparationTime;

                ComputeFacets(luceneQueryModel, searcherHandle, facetsCollector);

                executionContext.Phase = QueryExecutionPhase.ConvertResults;
                executionContext.Hits = hits;

                if (luceneQueryModel.Last)
                {
                    // The last hit, unless Skip has already passed it (or there is none).
                    var last = hits.ScoreDocs.Length - 1;
                    if (last < Math.Max(skipResults, 0)) yield break;
                    skipResults = last;
                }

                var tracker = luceneQueryModel.DocumentTracker as IRetrievedDocumentTracker<TDocument>;
                var retrievedDocuments = 0;

                foreach (var p in EnumerateHits(hits, executionContext, searcher, tracker, itemHolder, skipResults, projector))
                {
                    yield return p;
                    retrievedDocuments++;
                }

                var elapsedRetrievalTime = watch.Elapsed - elapsedSearchTime - elapsedPreparationTime;
                RaiseStatisticsCallback(luceneQueryModel, executionContext, elapsedPreparationTime, elapsedSearchTime, elapsedRetrievalTime, skipResults, retrievedDocuments);
            }
        }

        /// <summary>
        /// Runs the parts of a query that Lucene cannot run with LINQ's meaning
        /// on rows that are already loaded, and returns the result; returns
        /// <c>null</c> when the whole query can go to Lucene as it is.
        /// <list type="bullet">
        ///   <item>Result operators the Lucene path does not implement (Sum,
        ///     Average, Distinct, Contains, All, DefaultIfEmpty…), and Min/Max
        ///     after paging: the query up to that operator runs normally and
        ///     the rest is applied to its rows.</item>
        ///   <item>A source that is itself a query which cannot be folded into
        ///     this one (<c>query.Take(10).Where(…)</c>,
        ///     <c>query.Select(…).Distinct().OrderBy(…)</c>,
        ///     <c>(a join b).Take(10).Where(…)</c>): the source runs first, by
        ///     these same rules, and the clauses, select and result operators
        ///     written after it are applied to its rows. Folded into one
        ///     Lucene query they would filter or sort before the paging, or
        ///     lose the Distinct or the join.</item>
        /// </list>
        /// </summary>
        private IStreamedData TryExecuteInMemory(QueryModel queryModel)
        {
            var operators = queryModel.ResultOperators;
            var tailStart = InMemoryRows.FindInMemoryTail(operators);

            if (tailStart >= 0)
            {
                var tail = operators.Skip(tailStart).ToList();
                while (operators.Count > tailStart) operators.RemoveAt(operators.Count - 1);

                // The override names the type of the whole query's result, which the shortened query no longer produces.
                queryModel.ResultTypeOverride = null;

                var info = (StreamedSequenceInfo)queryModel.GetOutputDataInfo();

                if (tail.Any(o => o is Remotion.Linq.Clauses.ResultOperators.AllResultOperator
                    || o is Remotion.Linq.Clauses.ResultOperators.AggregateResultOperator
                    || o is Remotion.Linq.Clauses.ResultOperators.AggregateFromSeedResultOperator))
                {
                    // The lambdas of All and Aggregate read the query sources, which a
                    // selected item no longer holds (Select(s => s.Rank).All(r => r > 0)).
                    // So the query returns the sources that the select and the tail read,
                    // and the select is applied to those rows along with the tail.
                    var selector = queryModel.SelectClause.Selector;
                    var rowSources = new IQuerySource[] { queryModel.MainFromClause }
                        .Concat(queryModel.BodyClauses.OfType<JoinClause>())
                        .Where(s => InMemoryRows.References(selector, new[] { s }) || tail.Any(o => InMemoryRows.References(o, new[] { s })))
                        .ToList();

                    queryModel.SelectClause.Selector = Expression.NewArrayInit(typeof(object),
                        rowSources.Select(s => Expression.Convert(new Remotion.Linq.Clauses.Expressions.QuerySourceReferenceExpression(s), typeof(object))));

                    return InMemoryRows.ApplyOperators(ExecuteCollection<object[]>(queryModel), selector, rowSources, info, tail);
                }

                IStreamedData data = (IStreamedData)InvokeGeneric(RunAsSequenceMethod, info.ResultItemType, queryModel, info);

                foreach (var op in tail)
                {
                    data = op.ExecuteInMemory(data);
                }

                return data;
            }

            var subQuery = queryModel.MainFromClause.FromExpression as Remotion.Linq.Clauses.Expressions.SubQueryExpression;

            if (subQuery == null) return null;

            var clauses = queryModel.BodyClauses;
            var onlyFiltersAndOrderings = clauses.All(c => c is WhereClause || c is OrderByClause);

            if (ContainsJoin(subQuery.QueryModel))
            {
                if (!onlyFiltersAndOrderings)
                {
                    throw new NotSupportedException("Only where, orderby, select and result operators can follow a join that is used as a source: " + queryModel);
                }
            }
            else if (!onlyFiltersAndOrderings || FoldsIntoOneSearch(queryModel))
            {
                return null;
            }

            var sourceInfo = (StreamedSequenceInfo)subQuery.QueryModel.GetOutputDataInfo();
            var sourceRows = (StreamedSequence)InvokeGeneric(RunAsSequenceMethod, sourceInfo.ResultItemType, subQuery.QueryModel, sourceInfo);
            var sources = new IQuerySource[] { queryModel.MainFromClause };

            // Rows that are this executor's documents are ordered as their fields sort in Lucene.
            var providers = new Dictionary<IQuerySource, IFieldMappingInfoProvider>();
            if (queryModel.MainFromClause.ItemType == typeof(TDocument))
            {
                providers[queryModel.MainFromClause] = this;
            }

            var filtered = InMemoryRows.Apply(sourceRows.Sequence.Cast<object>().Select(o => new[] { o }), clauses, sources,
                e => GetTermKeyMapping(e, providers));

            return InMemoryRows.ApplyOperators(filtered, queryModel.SelectClause.Selector, sources, queryModel.SelectClause.GetOutputDataInfo(), operators);
        }

        /// <summary>
        /// Whether a query whose source is a sub-query gives the same result
        /// when the sub-query's clauses and result operators are merged into
        /// it and the whole runs as one Lucene search, which is what
        /// <see cref="PrepareQuery"/> does. Checked through every level of
        /// nesting, because the merge moves the innermost operators outwards.
        /// </summary>
        private static bool FoldsIntoOneSearch(QueryModel model)
        {
            if (!(model.MainFromClause.FromExpression is Remotion.Linq.Clauses.Expressions.SubQueryExpression subQuery)) return true;

            var source = subQuery.QueryModel;

            if (ContainsJoin(source) || !FoldsIntoOneSearch(source)) return false;

            // A where or orderby would run ahead of the source's Skip or Take.
            if (IsPaged(source) && model.BodyClauses.Any(c => c is WhereClause || c is OrderByClause)) return false;

            // The source's operators end up in front of this query's: neither may
            // need loaded rows (Distinct, DefaultIfEmpty…), and a Min or Max here
            // must not follow paging there (Take(2).Min(s => s.Rank)).
            return InMemoryRows.FindInMemoryTail(OperatorsInnermostFirst(model)) < 0;
        }

        private static List<ResultOperatorBase> OperatorsInnermostFirst(QueryModel model)
        {
            var operators = model.MainFromClause.FromExpression is Remotion.Linq.Clauses.Expressions.SubQueryExpression nested
                ? OperatorsInnermostFirst(nested.QueryModel)
                : new List<ResultOperatorBase>();

            operators.AddRange(model.ResultOperators);
            return operators;
        }

        private static bool ContainsJoin(QueryModel model)
        {
            return model.BodyClauses.OfType<JoinClause>().Any()
                || (model.MainFromClause.FromExpression is Remotion.Linq.Clauses.Expressions.SubQueryExpression nested && ContainsJoin(nested.QueryModel));
        }

        private static bool IsPaged(QueryModel model)
        {
            return model.ResultOperators.Any(o => o is Remotion.Linq.Clauses.ResultOperators.TakeResultOperator || o is Remotion.Linq.Clauses.ResultOperators.SkipResultOperator)
                || (model.MainFromClause.FromExpression is Remotion.Linq.Clauses.Expressions.SubQueryExpression nested && IsPaged(nested.QueryModel));
        }

        private static readonly System.Reflection.MethodInfo RunAsSequenceMethod =
            typeof(LuceneQueryExecutorBase<TDocument>).GetMethod(nameof(RunAsSequence),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private StreamedSequence RunAsSequence<T>(QueryModel queryModel, StreamedSequenceInfo info)
        {
            return new StreamedSequence(ExecuteCollection<T>(queryModel).AsQueryable(), info);
        }

        /// <summary>
        /// Calls a generic method of this executor for a type only known at run
        /// time, surfacing the method's own exception rather than the wrapper.
        /// </summary>
        private object InvokeGeneric(System.Reflection.MethodInfo method, Type typeArgument, params object[] arguments)
        {
            try
            {
                return method.MakeGenericMethod(typeArgument).Invoke(this, arguments);
            }
            catch (System.Reflection.TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        /// <summary>
        /// Runs a query that has join clauses and applies its result operators
        /// (Skip, Take, Count, Any, First…) to the joined rows, as LINQ defines
        /// them, rather than to the outer query before the join.
        /// </summary>
        private IStreamedData ExecuteJoinedInMemory(QueryModel queryModel)
        {
            var joinClauses = queryModel.BodyClauses.OfType<JoinClause>().ToList();

            // Taken off the model so the outer query runs without them.
            var resultOperators = queryModel.ResultOperators.ToList();
            queryModel.ResultOperators.Clear();

            // Captured before ExecuteJoinedRows rewrites the select clause; the
            // result operators resolve their expressions against it.
            var rowInfo = queryModel.SelectClause.GetOutputDataInfo();

            var data = (IStreamedData)InvokeGeneric(ExecuteJoinedRowsMethod, rowInfo.ResultItemType, queryModel, joinClauses, rowInfo);

            foreach (var resultOperator in resultOperators)
            {
                data = resultOperator.ExecuteInMemory(data);
            }

            return data;
        }

        private static readonly System.Reflection.MethodInfo ExecuteJoinedRowsMethod =
            typeof(LuceneQueryExecutorBase<TDocument>).GetMethod(nameof(ExecuteJoinedRows),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private StreamedSequence ExecuteJoinedRows<T>(QueryModel queryModel, List<JoinClause> joinClauses, StreamedSequenceInfo rowInfo)
        {
            return new StreamedSequence(ExecuteJoinedCollection<T>(queryModel, joinClauses).AsQueryable(), rowInfo);
        }

        /// <summary>
        /// Joins in memory. The outer side (main from clause) runs as a normal
        /// Lucene query; each join's inner sequence is then loaded, restricted to
        /// the keys of the outer rows, and joined with LINQ to Objects. Key
        /// selectors and the result projection are compiled from the query
        /// model's expression trees.
        /// </summary>
        private IEnumerable<T> ExecuteJoinedCollection<T>(QueryModel queryModel, List<JoinClause> joinClauses)
        {
            // Save references before mutating the query model
            var mainFromClause = queryModel.MainFromClause;
            var selectSelector = queryModel.SelectClause.Selector;

            // Mapping providers for the main source and each joined source, used
            // to look keys up and compare them the way their fields are indexed.
            var keyProviders = new Dictionary<IQuerySource, IFieldMappingInfoProvider> { { mainFromClause, this } };

            var innerSequences = joinClauses.Select(JoinInnerSequence.Resolve).ToList();

            // A where or orderby written after the first join that mentions a joined
            // source cannot go into the outer side's Lucene query. A where that reads
            // one joined source only goes into that source's own query; the others
            // are applied to the joined rows. Once one orderby is applied there,
            // every later orderby is too, so that the last one written stays the
            // primary one.
            var rowSources = new List<IQuerySource> { mainFromClause };
            rowSources.AddRange(joinClauses);

            var joinedSources = new HashSet<IQuerySource>(joinClauses);
            var rowClauses = new List<IBodyClause>();
            var orderingRows = false;

            foreach (var clause in queryModel.BodyClauses.Skip(queryModel.BodyClauses.IndexOf(joinClauses[0]) + 1).ToList())
            {
                if (!(clause is WhereClause) && !(clause is OrderByClause)) continue;

                if (clause is WhereClause where && TryMoveToInnerQuery(where, mainFromClause, joinClauses, innerSequences))
                {
                    queryModel.BodyClauses.Remove(clause);
                }
                else if (InMemoryRows.References(clause, joinedSources) || (clause is OrderByClause && orderingRows))
                {
                    rowClauses.Add(clause);
                    queryModel.BodyClauses.Remove(clause);
                    orderingRows |= clause is OrderByClause;
                }
            }

            // Strip join clauses so PrepareQuery only sees WHERE/ORDER/etc.
            foreach (var jc in joinClauses)
                queryModel.BodyClauses.Remove(jc);

            // Rewrite SelectClause to just return the outer document (identity projection)
            queryModel.SelectClause.Selector = new Remotion.Linq.Clauses.Expressions.QuerySourceReferenceExpression(mainFromClause);

            // 1. Execute the outer query
            var outerResults = ExecuteCollection<TDocument>(queryModel).ToList();
            Log.LogDebug("Join: outer query ({Type}) returned {Count} results", typeof(TDocument).Name, outerResults.Count);

            // 2. For each join clause, materialize the inner side with semi-join pushdown,
            //    then perform the join. Each join narrows the running result set.
            //    We track results as object[] tuples (flattened) and build the final
            //    projection at the end.
            //
            //    For a single join:  outer.Join(inner, outerKey, innerKey, (o,i) => result)
            //    For N joins we chain: result_0 = outer
            //                          result_1 = result_0.Join(inner_1, ...)
            //                          result_N = result_{N-1}.Join(inner_N, ...)

            // Start with outer results wrapped as object arrays (tuple of 1)
            IEnumerable<object[]> runningResults = outerResults.Select(o => new object[] { o });

            for (int i = 0; i < joinClauses.Count; i++)
            {
                var joinClause = joinClauses[i];

                Log.LogDebug("Join[{Index}]: inner type={InnerType}, outer key={OuterKey}, inner key={InnerKey}",
                    i, joinClause.ItemType.Name, joinClause.OuterKeySelector, joinClause.InnerKeySelector);

                // Compile the outer key selector for this join.
                // For the first join, the outer key references mainFromClause.
                // For subsequent joins, Re-linq references the previous join clause
                // (which we've already materialized at position [i] in the tuple).
                // We resolve the key by replacing the source reference with the
                // appropriate tuple element access.
                var tupleParam = Expression.Parameter(typeof(object[]), "t");
                var outerKeyBody = joinClause.OuterKeySelector;
                // Replace mainFromClause reference with tuple[0] (the outer document)
                outerKeyBody = ReplaceSingleReference(outerKeyBody, mainFromClause,
                    Expression.Convert(Expression.ArrayIndex(tupleParam, Expression.Constant(0)), typeof(TDocument)));
                // Replace any previous join clause references with their tuple positions
                for (int j = 0; j < i; j++)
                    outerKeyBody = ReplaceSingleReference(outerKeyBody, joinClauses[j],
                        Expression.Convert(Expression.ArrayIndex(tupleParam, Expression.Constant(j + 1)), joinClauses[j].ItemType));

                var outerKeySelector = Expression.Lambda<Func<object[], object>>(
                    Expression.Convert(outerKeyBody, typeof(object)), tupleParam).Compile();

                // Resolve the inner sequence and find how each key is indexed.
                var innerSequence = innerSequences[i];
                if (innerSequence.ReturnsDocuments && innerSequence.Executor is IFieldMappingInfoProvider innerProvider)
                {
                    keyProviders[joinClause] = innerProvider;
                }

                var outerKeyMapping = GetTermKeyMapping(joinClause.OuterKeySelector, keyProviders);
                var innerKeyMapping = GetTermKeyMapping(joinClause.InnerKeySelector, keyProviders);

                // String keys are compared by the term they are indexed as, so the
                // join follows the fields' configuration (a default-mapped field
                // is case-insensitive) exactly as a query on those fields does.
                // When only one side is indexed as written, the terms of equal
                // values can differ ("bob" and "Bob"), so the values are compared
                // and the exact side's case sensitivity decides.
                var compareAsTerms = joinClause.OuterKeySelector.Type == typeof(string)
                    && ComparesAsTerms(outerKeyMapping, innerKeyMapping);

                if (compareAsTerms)
                {
                    var rawOuterKeySelector = outerKeySelector;
                    outerKeySelector = tuple => ToTermKey(outerKeyMapping, rawOuterKeySelector(tuple));
                }

                // Extract distinct outer key values for semi-join pushdown
                var outerKeys = runningResults.Select(outerKeySelector).Where(k => k != null).Distinct().ToList();

                // Materialize inner side
                var innerResults = MaterializeInnerWithSemiJoin(joinClause, innerSequence, innerKeyMapping, outerKeys);
                Log.LogDebug("Join[{Index}]: inner query returned {Count} results (from {KeyCount} outer keys)",
                    i, innerResults.Length, outerKeys.Count);

                // Compile inner key selector
                var innerParam = Expression.Parameter(typeof(object), "inner");
                var innerKeyBody = ReplaceSingleReference(joinClause.InnerKeySelector, joinClause,
                    Expression.Convert(innerParam, joinClause.ItemType));
                var innerKeySelector = Expression.Lambda<Func<object, object>>(
                    Expression.Convert(innerKeyBody, typeof(object)), innerParam).Compile();

                if (compareAsTerms)
                {
                    var rawInnerKeySelector = innerKeySelector;
                    innerKeySelector = inner => ToTermKey(innerKeyMapping, rawInnerKeySelector(inner));
                }

                // Join: append inner to each tuple
                var capturedInner = innerResults; // capture for closure
                runningResults = runningResults.Join(
                    capturedInner,
                    outerKeySelector,
                    innerKeySelector,
                    (tuple, inner) => tuple.Append(inner).ToArray());
            }

            // Strings are ordered as their fields sort in Lucene, so the order is
            // the one the same orderby gives when the join runs inside Lucene.
            runningResults = InMemoryRows.Apply(runningResults, rowClauses, rowSources, e => GetTermKeyMapping(e, keyProviders));

            // 3. Build the final result selector from the original SelectClause.
            //    Replace all source references with tuple element access.
            var finalTupleParam = Expression.Parameter(typeof(object[]), "t");
            var selectorBody = selectSelector;
            selectorBody = ReplaceSingleReference(selectorBody, mainFromClause,
                Expression.Convert(Expression.ArrayIndex(finalTupleParam, Expression.Constant(0)), typeof(TDocument)));
            for (int i = 0; i < joinClauses.Count; i++)
                selectorBody = ReplaceSingleReference(selectorBody, joinClauses[i],
                    Expression.Convert(Expression.ArrayIndex(finalTupleParam, Expression.Constant(i + 1)), joinClauses[i].ItemType));

            var resultSelector = Expression.Lambda<Func<object[], T>>(selectorBody, finalTupleParam).Compile();

            return runningResults.Select(resultSelector);
        }

        /// <summary>
        /// Moves a <c>where</c> that reads exactly one joined source into that
        /// source's own Lucene query. Such a filter keeps or drops an inner
        /// document whatever it joins, so the joined rows are the same, and it
        /// then means what it means when the join runs inside Lucene (it
        /// follows the field's analyzer and case handling) instead of being
        /// compared as C# values on the joined rows.
        /// </summary>
        private static bool TryMoveToInnerQuery(WhereClause where, IQuerySource main, List<JoinClause> joinClauses, List<JoinInnerSequence> innerSequences)
        {
            if (InMemoryRows.References(where, new[] { main })) return false;

            var read = joinClauses.Where(j => InMemoryRows.References(where, new IQuerySource[] { j })).ToList();
            if (read.Count != 1) return false;

            var inner = innerSequences[joinClauses.IndexOf(read[0])];

            // A local sequence has no query; a projection hides the documents; and
            // a filter added to a paged query would run ahead of its Take or Skip.
            if (inner.QueryModel == null || !inner.ReturnsDocuments || inner.QueryModel.ResultOperators.Count > 0) return false;

            var toInner = new QuerySourceMapping();
            toInner.AddMapping(read[0], new Remotion.Linq.Clauses.Expressions.QuerySourceReferenceExpression(inner.QueryModel.MainFromClause));
            where.TransformExpressions(e => ReferenceReplacingExpressionVisitor.ReplaceClauseReferences(e, toInner, throwOnUnmappedReferences: false));

            inner.QueryModel.BodyClauses.Add(where);
            return true;
        }

        /// <summary>
        /// Materializes the inner side of a join, using a TermsFilter to only fetch
        /// documents whose join key matches one of the outer key values (semi-join pushdown).
        /// </summary>
        private object[] MaterializeInnerWithSemiJoin(JoinClause joinClause, JoinInnerSequence inner, ITermKeyMappingInfo innerKeyMapping, List<object> outerKeys)
        {
            if (outerKeys.Count == 0)
                return Array.Empty<object>();

            if (inner.Local != null)
                return inner.Local.Cast<object>().ToArray();

            {
                var innerModel = inner.QueryModel;
                List<Term> terms = null;
                string innerKeyFieldName;

                if (innerKeyMapping != null)
                {
                    // Look keys up by the mapped field name and the term each key
                    // is indexed as (converted, analyzed, or prefix-coded).
                    innerKeyFieldName = innerKeyMapping.FieldName;
                    terms = new List<Term>(outerKeys.Count);

                    foreach (var key in outerKeys)
                    {
                        var text = innerKeyMapping.ConvertToTermText(key);

                        if (text == null)
                        {
                            // A key with no single-term form cannot be looked up; load the whole inner side.
                            Log.LogDebug("Join semi-join pushdown skipped: key {Key} has no single indexed term in {Field}", key, innerKeyFieldName);
                            terms = null;
                            break;
                        }

                        terms.Add(new Term(innerKeyFieldName, text));
                    }
                }
                else
                {
                    // A mapper that cannot name its indexed term: look the key up as written.
                    innerKeyFieldName = inner.ReturnsDocuments ? ExtractFieldName(joinClause.InnerKeySelector) : null;

                    if (innerKeyFieldName != null)
                    {
                        terms = outerKeys.Select(k => new Term(innerKeyFieldName, k.ToString())).ToList();
                    }
                }

                // The key filter must not run ahead of the inner sequence's own
                // Take or Skip, so with those the whole inner sequence is loaded.
                if (terms != null && innerModel.ResultOperators.Count == 0)
                {
                    Query filterQuery = new ConstantScoreQuery(new Lucene.Net.Queries.TermsFilter(terms));
                    Log.LogDebug("Join semi-join pushdown: TermsFilter on {Field} with {Count} keys",
                        innerKeyFieldName, terms.Count);

                    // Same clause LuceneMethods.Where(query) produces.
                    var source = innerModel.MainFromClause;
                    innerModel.BodyClauses.Add(new WhereClause(Expression.Call(
                        MatchesMethod.MakeGenericMethod(source.ItemType),
                        Expression.Constant(filterQuery, typeof(Query)),
                        new Remotion.Linq.Clauses.Expressions.QuerySourceReferenceExpression(source))));
                }

                var itemType = innerModel.SelectClause.GetOutputDataInfo().ResultItemType;
                var rows = (System.Collections.IEnumerable)ExecuteCollectionMethod.MakeGenericMethod(itemType)
                    .Invoke(inner.Executor, new object[] { innerModel });

                return rows.Cast<object>().ToArray();
            }
        }

        private static readonly System.Reflection.MethodInfo MatchesMethod =
            typeof(LuceneMethods).GetMethod(nameof(LuceneMethods.Matches), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        private static readonly System.Reflection.MethodInfo ExecuteCollectionMethod =
            typeof(IQueryExecutor).GetMethod(nameof(IQueryExecutor.ExecuteCollection));

        /// <summary>
        /// The mapping of a join key that is a plain mapped property of one of
        /// the joined sources (<c>a.Key</c>), or <c>null</c> for composite or
        /// computed keys and for mappers that cannot name their indexed term.
        /// </summary>
        private static ITermKeyMappingInfo GetTermKeyMapping(Expression keySelector, Dictionary<IQuerySource, IFieldMappingInfoProvider> providers)
        {
            while (keySelector is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            {
                keySelector = unary.Operand;
            }

            if (!(keySelector is MemberExpression member)
                || !(member.Expression is Remotion.Linq.Clauses.Expressions.QuerySourceReferenceExpression source)
                || !providers.TryGetValue(source.ReferencedQuerySource, out var provider))
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
        /// Whether two string join keys are compared by their indexed terms
        /// rather than by their values: both must be mapped, and either both
        /// or neither indexed as written.
        /// </summary>
        private static bool ComparesAsTerms(ITermKeyMappingInfo outerKey, ITermKeyMappingInfo innerKey)
        {
            if (outerKey == null || innerKey == null) return false;

            return (outerKey.JoinTermForm == JoinTermForm.Exact) == (innerKey.JoinTermForm == JoinTermForm.Exact);
        }

        /// <summary>
        /// The key as the term it is indexed as; the key itself when it has none.
        /// </summary>
        private static object ToTermKey(ITermKeyMappingInfo mapping, object key)
        {
            return key == null ? null : mapping.ConvertToTermText(key) ?? key;
        }

        /// <summary>
        /// Extract the property/field name from a key selector expression.
        /// Handles expressions like: {[source].PropertyName} → "PropertyName"
        /// </summary>
        private static string ExtractFieldName(Expression expression)
        {
            if (expression is MemberExpression member)
                return member.Member.Name;
            if (expression is UnaryExpression unary)
                return ExtractFieldName(unary.Operand);
            return null;
        }

        /// <summary>
        /// Replace QuerySourceReferenceExpression nodes that reference the given
        /// source with the replacement expression. Other references are left as-is.
        /// </summary>
        private static Expression ReplaceSingleReference(Expression expression, IQuerySource source, Expression replacement)
        {
            var mapping = new QuerySourceMapping();
            mapping.AddMapping(source, replacement);
            return ReferenceReplacingExpressionVisitor.ReplaceClauseReferences(expression, mapping, throwOnUnmappedReferences: false);
        }

        private void RaiseStatisticsCallback(LuceneQueryModel luceneQueryModel, QueryExecutionContext executionContext, TimeSpan elapsedPreparationTime, TimeSpan elapsedSearchTime, TimeSpan elapsedRetrievalTime, int skipResults, int retrievedDocuments)
        {
            var statistics = new LuceneQueryStatistics(executionContext.Query,
                executionContext.Filter,
                luceneQueryModel.Sort,
                elapsedPreparationTime,
                elapsedSearchTime,
                elapsedRetrievalTime,
                executionContext.Hits.TotalHits,
                skipResults, retrievedDocuments);
            luceneQueryModel.RaiseCaptureQueryStatistics(statistics);
        }

        private IEnumerable<T> EnumerateHits<T>(TopDocs hits, QueryExecutionContext executionContext, IndexSearcher searcher, IRetrievedDocumentTracker<TDocument> tracker, ItemHolder itemHolder, int skipResults, Func<TDocument, T> projector)
        {
            for (var i = skipResults; i < hits.ScoreDocs.Length; i++)
            {
                executionContext.CurrentHit = i;
                executionContext.CurrentScoreDoc = hits.ScoreDocs[i];

                var docNum = hits.ScoreDocs[i].Doc;
                var document = searcher.Doc(docNum);

                if (tracker == null)
                {
                    itemHolder.Current = ConvertDocument(document, executionContext);
                    yield return projector(itemHolder.Current);
                    continue;
                }

                var key = GetDocumentKey(document, executionContext);

                if (tracker.IsMarkedForDeletion(key))
                {
                    continue;
                }

                TDocument item;
                if (!tracker.TryGetTrackedDocument(key, out item))
                {
                    item = ConvertDocument(document, executionContext);
                    tracker.TrackDocument(key, item, document);
                }

                itemHolder.Current = item;
                yield return projector(itemHolder.Current);
            }
        }

        /// <summary>
        /// Builds the filter for the search: the query model's own filter plus
        /// one query-time join per join that was pushed into Lucene. Creating a
        /// join runs its "from" query against <paramref name="searcher"/>, which
        /// is why this happens here rather than during translation, and why the
        /// same searcher serves the join and the search.
        /// </summary>
        private Filter ResolveJoinFilters(LuceneQueryModel luceneQueryModel, IndexSearcher searcher)
        {
            var filter = luceneQueryModel.Filter;

            foreach (var join in luceneQueryModel.JoinFilters)
            {
                // ScoreMode.None: the join only selects documents. As a filter it
                // leaves the scores of the returned side untouched.
                var joinQuery = Lucene.Net.Search.Join.JoinUtil.CreateJoinQuery(
                    join.FromField, false, join.ToField, join.FromQuery, searcher, Lucene.Net.Search.Join.ScoreMode.None);

                Log.LogDebug("Join filter: {Join}", join);

                if (filter == null)
                {
                    filter = new QueryWrapperFilter(joinQuery);
                    continue;
                }

                var both = new BooleanQuery();
                both.Add(new ConstantScoreQuery(filter), Occur.MUST);
                both.Add(joinQuery, Occur.MUST);
                filter = new QueryWrapperFilter(both);
            }

            return filter;
        }

        /// <summary>
        /// Runs the search, also feeding <paramref name="facetsCollector"/> in the
        /// same pass when facets were requested. Document scores are computed
        /// either way (they are off by default for a sorted search), so
        /// <c>[QueryScore]</c> properties are filled with and without facets.
        /// </summary>
        private static TopFieldDocs Search(IndexSearcher searcher, Query query, Filter filter, int n, Sort sort, FacetsCollector facetsCollector)
        {
            if (facetsCollector == null)
            {
                return searcher.Search(query, filter, n, sort, doDocScores: true, doMaxScore: false);
            }

            return (TopFieldDocs)FacetsCollector.Search(searcher, query, filter, n, sort, true, false, facetsCollector);
        }

        /// <summary>
        /// Collects facet matches when no hits are needed (e.g. <c>Take(0)</c>
        /// or an empty index), where a top-N search cannot run.
        /// </summary>
        private static void CollectFacetsOnly(IndexSearcher searcher, Query query, Filter filter, FacetsCollector facetsCollector)
        {
            if (facetsCollector != null)
            {
                searcher.Search(query, filter, facetsCollector);
            }
        }

        private void ComputeFacets(LuceneQueryModel luceneQueryModel, ISearcherHandle searcherHandle, FacetsCollector facetsCollector)
        {
            if (facetsCollector == null) return;

            // Value counts for every dimension come from one pass over the
            // matching documents; build them once and share across requests.
            SortedSetDocValuesReaderState state = null;
            SortedSetDocValuesFacetCounts valueCounts = null;
            var valueCountsBuilt = false;

            foreach (var facetQuery in luceneQueryModel.FacetQueries)
            {
                var results = new Dictionary<string, IReadOnlyList<FacetValue>>(StringComparer.Ordinal);

                foreach (var spec in facetQuery.Specs)
                {
                    var info = GetFacetMappingInfo(spec.PropertyName);

                    if (spec.Ranges != null)
                    {
                        results[spec.PropertyName] = CountRanges(info, spec, facetsCollector);
                        continue;
                    }

                    if (!valueCountsBuilt)
                    {
                        state = searcherHandle.FacetState;
                        valueCounts = state != null ? new SortedSetDocValuesFacetCounts(state, facetsCollector) : null;
                        valueCountsBuilt = true;
                    }

                    results[spec.PropertyName] = CountValues(info, spec, state, valueCounts);
                }

                facetQuery.Sink.SetResults(results);
            }
        }

        private IFacetFieldMappingInfo GetFacetMappingInfo(string propertyName)
        {
            IFieldMappingInfo info;

            try
            {
                info = GetMappingInfo(propertyName);
            }
            catch (KeyNotFoundException)
            {
                info = null;
            }

            if (info is IFacetFieldMappingInfo facetInfo && facetInfo.Facet)
            {
                return facetInfo;
            }

            throw new InvalidOperationException(string.Format(
                "Property {0} of {1} is not mapped as a facet. Mark it with [Field(Facet = true)] or [NumericField(Facet = true)], or call .Facet() in a fluent mapping.",
                propertyName, MappedType));
        }

        private static IReadOnlyList<FacetValue> CountValues(IFacetFieldMappingInfo info, FacetSpec spec, SortedSetDocValuesReaderState state, SortedSetDocValuesFacetCounts counts)
        {
            if (state == null || counts == null) return Array.Empty<FacetValue>();

            var dimension = info.FieldName;

            // GetTopChildren throws for a dimension that was never indexed.
            var ordRange = state.GetOrdRange(dimension);
            if (ordRange == null) return Array.Empty<FacetValue>();

            // GetTopChildren sizes a priority queue by topN, so "all values"
            // means the number of distinct values in the dimension.
            var distinctValues = ordRange.End - ordRange.Start + 1;
            var top = spec.Top.HasValue ? Math.Min(spec.Top.Value, distinctValues) : distinctValues;

            var result = counts.GetTopChildren(top, dimension);
            if (result == null) return Array.Empty<FacetValue>();

            return result.LabelValues.Select(lv => new FacetValue(lv.Label, (int)lv.Value)).ToList();
        }

        private static IReadOnlyList<FacetValue> CountRanges(IFacetFieldMappingInfo info, FacetSpec spec, FacetsCollector facetsCollector)
        {
            var field = info.RangeFacetField;

            if (field == null)
            {
                throw new NotSupportedException(string.Format(
                    "Property {0} does not support range facets. Range facets need a single-valued numeric, DateTime or DateTimeOffset property mapped with Facet = true.",
                    info.PropertyName));
            }

            Lucene.Net.Facet.Facets counts;

            if (info.RangeFacetValueType == NumericFieldType.INT64)
            {
                var ranges = spec.Ranges.Select(r => new Int64Range(r.Label,
                    r.Min == null ? long.MinValue : (long)info.ConvertToRangeFacetValue(r.Min), r.Min == null || r.MinInclusive,
                    r.Max == null ? long.MaxValue : (long)info.ConvertToRangeFacetValue(r.Max), r.Max == null || r.MaxInclusive)).ToArray();

                counts = new Int64RangeFacetCounts(field, facetsCollector, ranges);
            }
            else if (info.RangeFacetValueType == NumericFieldType.DOUBLE)
            {
                var ranges = spec.Ranges.Select(r => new DoubleRange(r.Label,
                    r.Min == null ? double.NegativeInfinity : Convert.ToDouble(info.ConvertToRangeFacetValue(r.Min)), r.Min == null || r.MinInclusive,
                    r.Max == null ? double.PositiveInfinity : Convert.ToDouble(info.ConvertToRangeFacetValue(r.Max)), r.Max == null || r.MaxInclusive)).ToArray();

                counts = new DoubleRangeFacetCounts(field, facetsCollector, ranges);
            }
            else
            {
                // Reading a column with the wrong decoder would return wrong counts silently.
                throw new NotSupportedException(string.Format(
                    "Range facet field {0} of property {1} is encoded as {2}. Range counts read INT64 or DOUBLE columns only.",
                    field, info.PropertyName, info.RangeFacetValueType));
            }

            var result = counts.GetTopChildren(spec.Ranges.Count, field);

            return result.LabelValues.Select(lv => new FacetValue(lv.Label, (int)lv.Value)).ToList();
        }

        private ISearcherHandle CheckoutSearcher()
        {
            return context.CheckoutSearcher();
        }

        private LuceneQueryModel PrepareQuery(QueryModel queryModel)
        {
            QueryModelTransformer.TransformQueryModel(queryModel, context.Settings.EmbeddingGenerator, DefaultSearchProperty, MapperAnalyzer);

            var builder = new QueryModelTranslator(this, context);
            builder.Build(queryModel);

            Log.LogDebug("Lucene query: {Model}", builder.Model);

            return builder.Model;
        }

        /// <summary>
        /// Replaces the vector (Similar) clauses of the query with queries Lucene
        /// can run. The filter is passed along so that it narrows the documents
        /// before nearest neighbours are chosen. See <see cref="VectorQueryResolver"/>.
        /// </summary>
        internal Query ResolveVectorQueries(LuceneQueryModel luceneQueryModel, Filter filter, IndexReader reader)
        {
            return VectorQueryResolver.Resolve(luceneQueryModel.Query, filter, reader,
                luceneQueryModel.MaxResults, luceneQueryModel.SkipResults,
                LookupVectorFieldInfo, context.Settings.VectorExactSearchLimit);
        }

        private Mapping.IVectorFieldMappingInfo LookupVectorFieldInfo(string vectorFieldName)
        {
            // Vector field is named "{Property}_vector"; strip suffix to find the property.
            var propertyName = vectorFieldName.EndsWith("_vector")
                ? vectorFieldName.Substring(0, vectorFieldName.Length - "_vector".Length)
                : vectorFieldName;
            var mapping = GetMappingInfo(propertyName);
            return mapping as Mapping.IVectorFieldMappingInfo;
        }

        protected virtual Expression<Func<TDocument, T>> GetProjector<T>(QueryModel queryModel)
        {
            return Expression.Lambda<Func<TDocument, T>>(queryModel.SelectClause.Selector, Expression.Parameter(typeof(TDocument)));
        }

        public Type MappedType => typeof(TDocument);

        public abstract IFieldMappingInfo GetMappingInfo(string propertyName);
        public abstract IEnumerable<string> AllProperties { get; }
        public abstract IEnumerable<string> IndexedProperties { get; }
        public abstract IEnumerable<string> KeyProperties { get; }
        public abstract Query CreateMultiFieldQuery(string pattern);

        protected abstract IDocumentKey GetDocumentKey(Document doc, IQueryExecutionContext context);
        protected abstract TDocument ConvertDocument(Document doc, IQueryExecutionContext context);
        protected abstract TDocument ConvertDocumentForCustomBoost(Document doc);
        protected abstract void PrepareSearchSettings(IQueryExecutionContext context);
        public virtual string DefaultSearchProperty => null;
        protected virtual Analyzer MapperAnalyzer => null;
    }
}
