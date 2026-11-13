using System;
using System.Collections.Generic;
using Iciclecreek.Lucene.Net.Vector;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;

namespace Lucene.Net.Linq.Search
{
    /// <summary>
    /// Matches the <see cref="K"/> documents nearest to a query vector among
    /// the documents a <see cref="Filter"/> accepts. The filter is applied
    /// first: a document it rejects is never one of the K.
    /// </summary>
    /// <remarks>
    /// When the filter accepts no more than <see cref="ExactLimit"/> documents,
    /// each of them is compared with the query vector and no vector index is
    /// built or read. Above that, the approximate index is searched for more
    /// neighbours than needed and the filter picks from them; if too few pass,
    /// the accepted documents are compared one by one after all. Runtimes
    /// without the approximate index always compare one by one.
    /// </remarks>
    internal class FilteredVectorQuery : Query
    {
        /// <summary>How many times the expected number of neighbours to ask the approximate index for.</summary>
        private const int Oversample = 2;

        public FilteredVectorQuery(string field, float[] queryVector, int k, Filter filter, VectorIndexOptions options, int exactLimit)
        {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            QueryVector = queryVector ?? throw new ArgumentNullException(nameof(queryVector));
            Filter = filter ?? throw new ArgumentNullException(nameof(filter));
            K = k;
            Options = options;
            ExactLimit = exactLimit;
        }

        public string Field { get; }
        public float[] QueryVector { get; }
        public int K { get; }
        public Filter Filter { get; }
        public VectorIndexOptions Options { get; }
        public int ExactLimit { get; }

        /// <summary>How the last search found its neighbours: "exact" or "index". For tests.</summary>
        internal string LastStrategy { get; private set; }

        /// <summary>How many documents the last search compared with the query vector one by one. For tests.</summary>
        internal int LastComparisons { get; private set; }

        public override Weight CreateWeight(IndexSearcher searcher)
        {
            return new FixedWeight(this, FindNearest(searcher));
        }

        private Dictionary<int, float> FindNearest(IndexSearcher searcher)
        {
            var reader = searcher.IndexReader;
            var accepted = AcceptedDocuments(reader);
            LastComparisons = 0;

            if (K <= 0 || accepted.Count == 0)
            {
                LastStrategy = "exact";
                return new Dictionary<int, float>();
            }

#if NET10_0_OR_GREATER
            if (accepted.Count > ExactLimit)
            {
                // Of the index's nearest neighbours only a share passes the filter,
                // so ask for proportionally more.
                long numDocs = reader.NumDocs;
                var wanted = accepted.Count >= numDocs
                    ? K
                    : (long)Math.Ceiling((double)K * numDocs / accepted.Count * Oversample);

                // Asking for as many neighbours as the filter accepts is no cheaper
                // than comparing those documents directly.
                if (wanted < accepted.Count)
                {
                    var knn = new KnnVectorQuery(Field, QueryVector, (int)wanted, reader, Options);
                    var hits = searcher.Search(knn, Filter, K).ScoreDocs;

                    if (hits.Length >= Math.Min(K, accepted.Count))
                    {
                        LastStrategy = "index";
                        var fromIndex = new Dictionary<int, float>(hits.Length);
                        foreach (var hit in hits)
                        {
                            fromIndex[hit.Doc] = hit.Score * Boost;
                        }
                        return fromIndex;
                    }
                }
            }
#endif

            LastStrategy = "exact";
            return CompareEach(reader, accepted);
        }

        /// <summary>Index-wide ids of the live documents the filter accepts, in order.</summary>
        private List<int> AcceptedDocuments(IndexReader reader)
        {
            var accepted = new List<int>();

            foreach (var leaf in reader.Leaves)
            {
                var set = Filter.GetDocIdSet(leaf, leaf.AtomicReader.LiveDocs);
                var iterator = set?.GetIterator();
                if (iterator == null) continue;

                int doc;
                while ((doc = iterator.NextDoc()) != DocIdSetIterator.NO_MORE_DOCS)
                {
                    accepted.Add(leaf.DocBase + doc);
                }
            }

            return accepted;
        }

        private Dictionary<int, float> CompareEach(IndexReader reader, List<int> accepted)
        {
            var queryNorm = VectorMath.Norm(QueryVector);
            var docs = new int[accepted.Count];
            var distances = new float[accepted.Count];
            var count = 0;
            var next = 0;
            var bytes = new BytesRef();

            foreach (var leaf in reader.Leaves)
            {
                var end = leaf.DocBase + leaf.AtomicReader.MaxDoc;
                var vectors = leaf.AtomicReader.GetBinaryDocValues(Field);

                for (; next < accepted.Count && accepted[next] < end; next++)
                {
                    if (vectors == null) continue;

                    vectors.Get(accepted[next] - leaf.DocBase, bytes);
                    if (bytes.Length == 0) continue;

                    var vector = VectorSerializer.FromBytesRef(bytes);
                    if (vector.Length != QueryVector.Length) continue;

                    docs[count] = accepted[next];
                    distances[count] = 1f - VectorMath.CosineSimilarity(QueryVector, vector, queryNorm);
                    count++;
                }
            }

            LastComparisons = count;

            // Nearest first; ties in index order.
            Array.Sort(distances, docs, 0, count);

            var keep = Math.Min(K, count);
            var nearest = new Dictionary<int, float>(keep);
            for (var i = 0; i < keep; i++)
            {
                // The score the vector library gives a neighbour at this distance.
                nearest[docs[i]] = new SearchResult(docs[i], distances[i]).Score * Boost;
            }
            return nearest;
        }

        public override string ToString(string field)
        {
            return $"FilteredVectorQuery(field={Field}, k={K}, filter={Filter})";
        }

        public override bool Equals(object obj)
        {
            return obj is FilteredVectorQuery other
                && Field == other.Field
                && K == other.K
                && Boost == other.Boost
                && ReferenceEquals(QueryVector, other.QueryVector)
                && Equals(Filter, other.Filter);
        }

        public override int GetHashCode()
        {
            return Field.GetHashCode() ^ K ^ Filter.GetHashCode();
        }

        private class FixedWeight : Weight
        {
            private readonly FilteredVectorQuery query;
            private readonly Dictionary<int, float> scores;

            public FixedWeight(FilteredVectorQuery query, Dictionary<int, float> scores)
            {
                this.query = query;
                this.scores = scores;
            }

            public override Query Query => query;

            public override Explanation Explain(AtomicReaderContext context, int doc)
            {
                return scores.TryGetValue(context.DocBase + doc, out var score)
                    ? new Explanation(score, query.ToString() + ", distance-based score")
                    : new Explanation(0f, "not one of the nearest documents");
            }

            public override float GetValueForNormalization() => 1f;

            public override void Normalize(float norm, float topLevelBoost)
            {
            }

            public override Scorer GetScorer(AtomicReaderContext context, IBits acceptDocs)
            {
                var inSegment = new List<KeyValuePair<int, float>>();
                var maxDoc = context.AtomicReader.MaxDoc;

                foreach (var pair in scores)
                {
                    var local = pair.Key - context.DocBase;
                    if (local < 0 || local >= maxDoc) continue;
                    if (acceptDocs != null && !acceptDocs.Get(local)) continue;
                    inSegment.Add(new KeyValuePair<int, float>(local, pair.Value));
                }

                if (inSegment.Count == 0) return null;

                inSegment.Sort((a, b) => a.Key.CompareTo(b.Key));
                return new FixedScorer(this, inSegment);
            }
        }

        private class FixedScorer : Scorer
        {
            private readonly List<KeyValuePair<int, float>> results;
            private int current = -1;

            public FixedScorer(Weight weight, List<KeyValuePair<int, float>> results) : base(weight)
            {
                this.results = results;
            }

            public override int DocID =>
                current < 0 ? -1 :
                current >= results.Count ? NO_MORE_DOCS :
                results[current].Key;

            public override float GetScore() => results[current].Value;

            public override int NextDoc()
            {
                current++;
                return current >= results.Count ? NO_MORE_DOCS : results[current].Key;
            }

            public override int Advance(int target)
            {
                while (++current < results.Count)
                {
                    if (results[current].Key >= target) return results[current].Key;
                }
                return NO_MORE_DOCS;
            }

            public override long GetCost() => results.Count;

            public override int Freq => 1;
        }
    }
}
