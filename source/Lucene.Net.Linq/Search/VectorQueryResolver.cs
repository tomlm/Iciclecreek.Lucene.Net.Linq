using System;
using System.Collections.Generic;
using System.Linq;
using Iciclecreek.Lucene.Net.Vector;
using Lucene.Net.Index;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Search;

namespace Lucene.Net.Linq.Search
{
    /// <summary>
    /// Turns the <see cref="DeferredVectorQuery"/> placeholders of a translated
    /// query into queries Lucene can run, so that every other condition narrows
    /// the documents before any vector is compared.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>Similar with other conditions</b> (<c>a &amp;&amp; x.Similar(q)</c>, or the
    ///     same spread over several <c>Where</c> calls): the other conditions select the
    ///     documents and each one is scored by its similarity. Nothing is cut off; a
    ///     <c>Take</c> keeps the most similar.</item>
    ///   <item><b>Similar alone</b>: the K nearest documents, K being the <c>Take</c>
    ///     (plus <c>Skip</c>) or 10. The search filter (document type, joins run inside
    ///     Lucene) is applied before the K are chosen.</item>
    ///   <item><b>Similar under an OR</b>: the K nearest documents, as one alternative.</item>
    /// </list>
    /// </remarks>
    internal static class VectorQueryResolver
    {
        private const int DefaultK = 10;

        public static bool ContainsVectorQuery(Query query)
        {
            if (query is DeferredVectorQuery) return true;

            return query is BooleanQuery boolean && boolean.GetClauses().Any(c => ContainsVectorQuery(c.Query));
        }

        /// <param name="query">The translated query.</param>
        /// <param name="filter">The filter the search runs with, or <c>null</c>.</param>
        /// <param name="reader">The reader the search runs against.</param>
        /// <param name="maxResults">The query's <c>Take</c>, or <see cref="int.MaxValue"/>.</param>
        /// <param name="skipResults">The query's <c>Skip</c>.</param>
        /// <param name="lookup">Finds the mapping of a vector field, for its index options.</param>
        /// <param name="exactLimit">See <see cref="LuceneDataProviderSettings.VectorExactSearchLimit"/>.</param>
        public static Query Resolve(Query query, Filter filter, IndexReader reader, int maxResults, int skipResults,
            Func<string, IVectorFieldMappingInfo> lookup, int exactLimit)
        {
            if (!ContainsVectorQuery(query)) return query;

            var k = DefaultK;
            if (maxResults > 0 && maxResults < int.MaxValue)
            {
                k = (int)Math.Min((long)maxResults + Math.Max(skipResults, 0), int.MaxValue - 1);
            }

            return new Resolution(filter, reader, k, lookup, exactLimit).Resolve(Flatten(query));
        }

        /// <summary>
        /// Merges a required group of conditions into its parent, so that
        /// <c>Where(a).Where(x.Similar(q))</c> reads the same as
        /// <c>Where(a &amp;&amp; x.Similar(q))</c>. Only groups that hold a vector
        /// clause are touched; the rest of the query keeps its shape and scores.
        /// </summary>
        internal static Query Flatten(Query query)
        {
            if (!(query is BooleanQuery boolean)) return query;

            var flat = new BooleanQuery { Boost = boolean.Boost, MinimumNumberShouldMatch = boolean.MinimumNumberShouldMatch };

            foreach (var clause in boolean.GetClauses())
            {
                var child = Flatten(clause.Query);

                if (clause.Occur == Occur.MUST && child is BooleanQuery group && CanMerge(group))
                {
                    var inner = group.GetClauses();
                    if (inner.All(c => c.Occur != Occur.SHOULD))
                    {
                        foreach (var c in inner) flat.Add(c.Query, c.Occur);
                        continue;
                    }

                    if (inner.Length == 1)
                    {
                        flat.Add(inner[0].Query, Occur.MUST);
                        continue;
                    }
                }

                flat.Add(child, clause.Occur);
            }

            return flat;
        }

        private static bool CanMerge(BooleanQuery group)
        {
            return group.Boost == 1f && group.MinimumNumberShouldMatch == 0 && ContainsVectorQuery(group);
        }

        private class Resolution
        {
            private readonly Filter filter;
            private readonly IndexReader reader;
            private readonly int k;
            private readonly Func<string, IVectorFieldMappingInfo> lookup;
            private readonly int exactLimit;

            public Resolution(Filter filter, IndexReader reader, int k, Func<string, IVectorFieldMappingInfo> lookup, int exactLimit)
            {
                this.filter = filter;
                this.reader = reader;
                this.k = k;
                this.lookup = lookup;
                this.exactLimit = exactLimit;
            }

            public Query Resolve(Query query)
            {
                if (query is DeferredVectorQuery vector) return Nearest(vector);

                if (!(query is BooleanQuery boolean)) return query;

                var clauses = boolean.GetClauses();

                if (clauses.Any(c => c.Occur == Occur.MUST_NOT && c.Query is DeferredVectorQuery))
                {
                    throw new NotSupportedException(
                        "Similar() cannot be negated: there is no set of documents that are \"not similar\". " +
                        "Use Similar() to rank the documents the other conditions select.");
                }

                var vectors = clauses.Where(c => c.Occur == Occur.MUST && c.Query is DeferredVectorQuery)
                    .Select(c => (DeferredVectorQuery)c.Query).ToList();
                var others = clauses.Where(c => !(c.Occur == Occur.MUST && c.Query is DeferredVectorQuery)).ToList();

                if (vectors.Count == 0 || (vectors.Count == 1 && others.Count == 0))
                {
                    // Nothing here to rank by: resolve whatever the clauses hold.
                    var resolved = new BooleanQuery { Boost = boolean.Boost, MinimumNumberShouldMatch = boolean.MinimumNumberShouldMatch };
                    foreach (var clause in clauses)
                    {
                        resolved.Add(Resolve(clause.Query), clause.Occur);
                    }
                    return resolved;
                }

                // The other conditions select the documents; similarity scores them.
                var selection = Selection(others, boolean.MinimumNumberShouldMatch);

                if (vectors.Count == 1)
                {
                    return new VectorScoreQuery(selection, vectors[0].Field, vectors[0].QueryVector) { Boost = boolean.Boost };
                }

                // Several Similar() calls: the same selection, scored by each and summed.
                var summed = new BooleanQuery { Boost = boolean.Boost };
                foreach (var each in vectors)
                {
                    summed.Add(new VectorScoreQuery(selection, each.Field, each.QueryVector), Occur.MUST);
                }
                return summed;
            }

            private Query Selection(List<BooleanClause> others, int minimumNumberShouldMatch)
            {
                var selection = new BooleanQuery { MinimumNumberShouldMatch = minimumNumberShouldMatch };

                foreach (var clause in others)
                {
                    selection.Add(Resolve(clause.Query), clause.Occur);
                }

                // Next to a required Similar(), optional conditions only added to the
                // score and exclusions only removed documents. Without it they need
                // something to select from.
                if (others.All(c => c.Occur != Occur.MUST))
                {
                    selection.Add(new MatchAllDocsQuery(), Occur.MUST);
                }

                return selection;
            }

            private Query Nearest(DeferredVectorQuery vector)
            {
                var info = lookup(vector.Field);

                if (filter == null)
                {
                    return vector.Resolve(reader, k, info);
                }

                var options = new VectorIndexOptions
                {
                    M = info?.HnswM ?? 16,
                    EfSearch = info?.HnswEfSearch ?? 50,
                };
                return new FilteredVectorQuery(vector.Field, vector.QueryVector, k, filter, options, exactLimit);
            }
        }
    }
}
