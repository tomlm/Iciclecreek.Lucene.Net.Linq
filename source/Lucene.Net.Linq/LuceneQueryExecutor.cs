using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using Lucene.Net.Documents;
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
    }

    internal abstract class LuceneQueryExecutorBase<TDocument> : IQueryExecutor, IFieldMappingInfoProvider
    {
        private readonly ILogger Log = Logging.CreateLogger(typeof(LuceneQueryExecutorBase<>));

        private readonly Context context;
        
        protected LuceneQueryExecutorBase(Context context)
        {
            this.context = context;
        }

        public T ExecuteScalar<T>(QueryModel queryModel)
        {
            var watch = new Stopwatch();
            watch.Start();

            var luceneQueryModel = PrepareQuery(queryModel);

            var searcherHandle = CheckoutSearcher();

            using (searcherHandle)
            {
                var searcher = searcherHandle.Searcher;
                var skipResults = luceneQueryModel.SkipResults;
                var maxResults = Math.Min(luceneQueryModel.MaxResults, searcher.IndexReader.MaxDoc - skipResults);

                var resolvedQuery = ResolveVectorQueries(luceneQueryModel.Query, searcher.IndexReader, luceneQueryModel.MaxResults);
                var executionContext = new QueryExecutionContext(searcher, resolvedQuery, luceneQueryModel.Filter);
                TopFieldDocs hits;

                TimeSpan elapsedPreparationTime;
                TimeSpan elapsedSearchTime;

                if (maxResults > 0)
                {
                    PrepareSearchSettings(executionContext);

                    Log.LogDebug("Executing scalar query: {Query}, Filter: {Filter}, MaxResults: {MaxResults}, Sort: {Sort}",
                        executionContext.Query, executionContext.Filter, maxResults, luceneQueryModel.Sort);

                    elapsedPreparationTime = watch.Elapsed;

                    hits = searcher.Search(executionContext.Query, executionContext.Filter, maxResults, luceneQueryModel.Sort);

                    elapsedSearchTime = watch.Elapsed - elapsedPreparationTime;
                }
                else
                {
                    hits = new TopFieldDocs(0, new ScoreDoc[0], new SortField[0], 0);
                    elapsedPreparationTime = watch.Elapsed;
                    elapsedSearchTime = TimeSpan.Zero;
                }

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
            // If the query contains joins, materialize both sides and join in memory.
            // Lucene has no relational join — we execute each side as a separate search,
            // then use LINQ-to-Objects Enumerable.Join with compiled key/result selectors.
            var joinClauses = queryModel.BodyClauses.OfType<JoinClause>().ToList();
            if (joinClauses.Count > 0)
            {
                foreach (var item in ExecuteJoinedCollection<T>(queryModel, joinClauses))
                    yield return item;
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
                var query = ResolveVectorQueries(luceneQueryModel.Query, searcher.IndexReader, luceneQueryModel.MaxResults);

                var scoreFunction = luceneQueryModel.GetCustomScoreFunction<TDocument>();
                if (scoreFunction != null)
                {
                    query = new DelegatingCustomScoreQuery<TDocument>(query, ConvertDocumentForCustomBoost, scoreFunction);
                }

                var executionContext = new QueryExecutionContext(searcher, query, luceneQueryModel.Filter);

                PrepareSearchSettings(executionContext);

                Log.LogDebug("Executing collection query: {Query}, Filter: {Filter}, MaxResults: {MaxResults}, Skip: {Skip}, Sort: {Sort}",
                    executionContext.Query, executionContext.Filter, maxResults, skipResults, luceneQueryModel.Sort);

                var elapsedPreparationTime = watch.Elapsed;
                // Lucene 4.8's IndexSearcher.Search throws ArgumentException
                // when nDocs is 0 (TopFieldCollector.Create requires
                // numHits > 0). Short-circuit on empty result sets so callers
                // like Min/Max see an empty enumerable rather than a crash.
                TopFieldDocs hits;
                if (maxResults + skipResults <= 0)
                {
                    hits = new TopFieldDocs(0, new ScoreDoc[0], new SortField[0], 0);
                }
                else
                {
                    hits = searcher.Search(executionContext.Query, executionContext.Filter, maxResults + skipResults, luceneQueryModel.Sort);
                }
                var elapsedSearchTime = watch.Elapsed - elapsedPreparationTime;

                executionContext.Phase = QueryExecutionPhase.ConvertResults;
                executionContext.Hits = hits;

                if (luceneQueryModel.Last)
                {
                    skipResults = hits.ScoreDocs.Length - 1;
                    if (skipResults < 0) yield break;
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
        /// Handles queries with join clauses by materializing both sides and performing
        /// an in-memory join. The outer side (main from clause) is executed as a normal
        /// Lucene query. The inner side (join's InnerSequence) is evaluated and materialized.
        /// Key selectors and the result projection are compiled from the query model's
        /// expression trees.
        /// </summary>
        private IEnumerable<T> ExecuteJoinedCollection<T>(QueryModel queryModel, List<JoinClause> joinClauses)
        {
            // Save references before mutating the query model
            var mainFromClause = queryModel.MainFromClause;
            var selectSelector = queryModel.SelectClause.Selector;

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

                // Extract distinct outer key values for semi-join pushdown
                var outerKeys = runningResults.Select(outerKeySelector).Where(k => k != null).Distinct().ToList();

                // Materialize inner side
                var innerResults = MaterializeInnerWithSemiJoin(joinClause, outerKeys);
                Log.LogDebug("Join[{Index}]: inner query returned {Count} results (from {KeyCount} outer keys)",
                    i, innerResults.Length, outerKeys.Count);

                // Compile inner key selector
                var innerParam = Expression.Parameter(typeof(object), "inner");
                var innerKeyBody = ReplaceSingleReference(joinClause.InnerKeySelector, joinClause,
                    Expression.Convert(innerParam, joinClause.ItemType));
                var innerKeySelector = Expression.Lambda<Func<object, object>>(
                    Expression.Convert(innerKeyBody, typeof(object)), innerParam).Compile();

                // Join: append inner to each tuple
                var capturedInner = innerResults; // capture for closure
                runningResults = runningResults.Join(
                    capturedInner,
                    outerKeySelector,
                    innerKeySelector,
                    (tuple, inner) => tuple.Append(inner).ToArray());
            }

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
        /// Materializes the inner side of a join, using a TermsFilter to only fetch
        /// documents whose join key matches one of the outer key values (semi-join pushdown).
        /// </summary>
        private object[] MaterializeInnerWithSemiJoin(JoinClause joinClause, List<object> outerKeys)
        {
            if (outerKeys.Count == 0)
                return Array.Empty<object>();

            var innerObj = Expression.Lambda<Func<object>>(
                Expression.Convert(joinClause.InnerSequence, typeof(object)))
                .Compile()();

            if (innerObj is IQueryable innerQueryable)
            {
                var innerKeyFieldName = ExtractFieldName(joinClause.InnerKeySelector);
                if (innerKeyFieldName != null)
                {
                    var terms = outerKeys.Select(k => new Term(innerKeyFieldName, k.ToString())).ToList();
                    var termsFilter = new Lucene.Net.Queries.TermsFilter(terms);
                    var filterQuery = new ConstantScoreQuery(termsFilter);
                    Log.LogDebug("Join semi-join pushdown: TermsFilter on {Field} with {Count} keys",
                        innerKeyFieldName, terms.Count);

                    var filteredQueryable = LuceneMethods.Where((dynamic)innerQueryable, filterQuery);
                    return ((IEnumerable<object>)Enumerable.Cast<object>((dynamic)filteredQueryable)).ToArray();
                }
                return innerQueryable.Cast<object>().ToArray();
            }

            if (innerObj is System.Collections.IEnumerable enumerable)
                return enumerable.Cast<object>().ToArray();

            return Array.Empty<object>();
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

        private ISearcherHandle CheckoutSearcher()
        {
            return context.CheckoutSearcher();
        }

        private LuceneQueryModel PrepareQuery(QueryModel queryModel)
        {
            QueryModelTransformer.TransformQueryModel(queryModel, context.Settings.EmbeddingGenerator);

            var builder = new QueryModelTranslator(this, context);
            builder.Build(queryModel);

            Log.LogDebug("Lucene query: {Model}", builder.Model);

            return builder.Model;
        }

        /// <summary>
        /// Walks a query tree and resolves vector queries.
        /// <list type="bullet">
        ///   <item><b>Pure vector query</b>: resolved via HNSW KNN with K = maxResults (from Take).</item>
        ///   <item><b>Hybrid query</b> (vector + filter predicates): the filter predicates
        ///     execute first and matching documents are ranked by cosine similarity
        ///     against the query vector — no HNSW needed.</item>
        /// </list>
        /// </summary>
        internal Query ResolveVectorQueries(Query query, IndexReader reader, int maxResults)
        {
            if (query is DeferredVectorQuery deferred)
            {
                // Pure vector query — use HNSW on NET10, brute-force cosine on downlevel
                var vectorFieldInfo = LookupVectorFieldInfo(deferred.Field);
                return deferred.Resolve(reader, maxResults, vectorFieldInfo);
            }

            if (query is BooleanQuery booleanQuery)
            {
                var clauses = booleanQuery.GetClauses();

                // Check for hybrid: vector + non-vector clauses
                DeferredVectorQuery vectorClause = null;
                BooleanQuery filterClauses = null;

                foreach (var clause in clauses)
                {
                    if (clause.Query is DeferredVectorQuery dkv)
                    {
                        vectorClause = dkv;
                    }
                    else
                    {
                        if (filterClauses == null) filterClauses = new BooleanQuery();
                        filterClauses.Add(clause);
                    }
                }

                if (vectorClause != null && filterClauses != null)
                {
                    // Hybrid: compose vector query with filter clauses in a BooleanQuery.
                    var vectorFieldInfo = LookupVectorFieldInfo(vectorClause.Field);
                    var resolvedVector = vectorClause.Resolve(reader, maxResults, vectorFieldInfo);
                    var hybrid = new BooleanQuery();
                    hybrid.Add(resolvedVector, Occur.MUST);
                    foreach (var fc in filterClauses.GetClauses())
                        hybrid.Add(fc);
                    return hybrid;
                }

                // No hybrid — recurse into nested BooleanQueries
                var resolved = new BooleanQuery();
                foreach (var clause in clauses)
                {
                    var resolvedQuery = ResolveVectorQueries(clause.Query, reader, maxResults);
                    resolved.Add(resolvedQuery, clause.Occur);
                }
                resolved.Boost = booleanQuery.Boost;
                return resolved;
            }

            return query;
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
    }
}
