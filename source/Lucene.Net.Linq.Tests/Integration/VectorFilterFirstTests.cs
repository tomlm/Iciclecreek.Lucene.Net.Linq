using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Index;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Linq.Search;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    /// <summary>
    /// One dimension per known word, counting how often the text uses it. The
    /// similarity of two texts is then plain arithmetic, so tests can state
    /// exact rankings and run on every target framework without a model.
    /// </summary>
    internal class WordCountEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private static readonly string[] Words = { "cat", "dog", "star", "rain", "music", "code", "fish", "tree" };

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions options = null, CancellationToken cancellationToken = default)
        {
            var results = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var text in values)
            {
                var vector = new float[Words.Length];
                foreach (var word in text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var index = Array.IndexOf(Words, word);
                    if (index >= 0) vector[index]++;
                }
                results.Add(new Embedding<float>(vector));
            }
            return Task.FromResult(results);
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Every condition of a query narrows the documents before a vector is
    /// compared: conditions in the same or another <c>Where</c>, the document
    /// type, and joins that run inside Lucene. Each test runs twice: comparing
    /// the selected documents one by one, and (where the runtime has one)
    /// through the approximate index.
    /// </summary>
    [TestFixture(10000)]
    [TestFixture(0)]
    public class VectorFilterFirstTests
    {
        public class Clip
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string Id { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string Topic { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string OwnerId { get; set; }

            [Field, VectorField]
            public string Text { get; set; }

            [Field, VectorField]
            public string Caption { get; set; }

            [NumericField]
            public int Rank { get; set; }
        }

        /// <summary>Another type in the same index, with the same vector field name.</summary>
        public class Memo
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string Id { get; set; }

            [Field, VectorField]
            public string Text { get; set; }
        }

        public class Owner
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string Username { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string Country { get; set; }
        }

        private readonly int exactLimit;
        private RAMDirectory directory;
        private LuceneDataProvider provider;
        private readonly List<string> trace = new List<string>();

        public VectorFilterFirstTests(int exactLimit)
        {
            this.exactLimit = exactLimit;
        }

        // Similarity of each text to the query "cat":
        //   p1 1.000  p2 0.949  p3 0.894  s1 0.832  p4 0.707  p5 0.447  s2 0.316  the rest 0
        private static readonly Clip[] AllClips =
        {
            new Clip { Id = "p1", Topic = "pets", OwnerId = "alice", Text = "cat", Caption = "music", Rank = 1 },
            new Clip { Id = "p2", Topic = "pets", OwnerId = "alice", Text = "cat cat cat dog", Caption = "music", Rank = 2 },
            new Clip { Id = "p3", Topic = "pets", OwnerId = "bob", Text = "cat cat dog", Caption = "music", Rank = 3 },
            new Clip { Id = "p4", Topic = "pets", OwnerId = "bob", Text = "cat dog", Caption = "rain", Rank = 4 },
            new Clip { Id = "p5", Topic = "pets", OwnerId = "carol", Text = "cat dog dog", Caption = "music", Rank = 5 },
            new Clip { Id = "p6", Topic = "pets", OwnerId = "carol", Text = "dog", Caption = "music", Rank = 6 },
            new Clip { Id = "s1", Topic = "space", OwnerId = "dave", Text = "star star cat cat cat", Caption = "music", Rank = 7 },
            new Clip { Id = "s2", Topic = "space", OwnerId = "dave", Text = "star star star cat", Caption = "music", Rank = 8 },
            new Clip { Id = "s3", Topic = "space", OwnerId = "dave", Text = "star", Caption = "music", Rank = 9 },
            new Clip { Id = "f1", Topic = "filler", OwnerId = "nobody", Text = "fish", Caption = "music", Rank = 10 },
            new Clip { Id = "f2", Topic = "filler", OwnerId = "nobody", Text = "fish", Caption = "music", Rank = 11 },
            new Clip { Id = "f3", Topic = "filler", OwnerId = "nobody", Text = "fish", Caption = "music", Rank = 12 },
            new Clip { Id = "f4", Topic = "filler", OwnerId = "nobody", Text = "fish", Caption = "music", Rank = 13 },
            new Clip { Id = "f5", Topic = "filler", OwnerId = "nobody", Text = "fish", Caption = "music", Rank = 14 },
            new Clip { Id = "f6", Topic = "filler", OwnerId = "nobody", Text = "fish", Caption = "music", Rank = 15 },
        };

        [SetUp]
        public void SetUp()
        {
            directory = new RAMDirectory();
            provider = new LuceneDataProvider(directory, LuceneVersion.LUCENE_48);
            provider.Settings.EmbeddingGenerator = new WordCountEmbeddingGenerator();
            provider.Settings.VectorExactSearchLimit = exactLimit;
            provider.Settings.JoinPushdownTrace = trace.Add;
            trace.Clear();

            Add(AllClips);

            // Five memos as near to "cat" as anything can be. A search for clips must not see them.
            Add(Enumerable.Range(1, 5).Select(i => new Memo { Id = "m" + i, Text = "cat" }).ToArray());

            Add(new Owner { Username = "alice", Country = "DE" },
                new Owner { Username = "bob", Country = "DE" },
                new Owner { Username = "carol", Country = "US" },
                new Owner { Username = "dave", Country = "US" });
        }

        [TearDown]
        public void TearDown()
        {
            provider.Dispose();
            directory.Dispose();
        }

        private void Add<T>(params T[] items) where T : new()
        {
            using (var session = provider.OpenSession<T>())
            {
                session.Add(items);
                session.Commit();
            }
        }

        private IQueryable<Clip> Clips => provider.AsQueryable<Clip>();
        private IQueryable<Owner> Owners => provider.AsQueryable<Owner>();

        private static string[] Ids(IEnumerable<Clip> clips) => clips.Select(c => c.Id).ToArray();

        // ---- Similar with other conditions: they select, similarity ranks -------

        [Test]
        public void FilterInTheSamePredicate()
        {
            Assert.That(Ids(Clips.Where(c => c.Topic == "space" && c.Text.Similar("cat")).Take(2)), Is.EqualTo(new[] { "s1", "s2" }));
            Assert.That(Ids(Clips.Where(c => c.Text.Similar("cat") && c.Topic == "space")), Is.EqualTo(new[] { "s1", "s2", "s3" }));
        }

        [Test]
        public void FilterInAnEarlierWhere()
        {
            // The two nearest clips overall are p1 and p2. Picking those first and
            // filtering afterwards would leave nothing.
            Assert.That(Ids(Clips.Where(c => c.Topic == "space").Where(c => c.Text.Similar("cat")).Take(2)), Is.EqualTo(new[] { "s1", "s2" }));
        }

        [Test]
        public void FilterInALaterWhere()
        {
            Assert.That(Ids(Clips.Where(c => c.Text.Similar("cat")).Where(c => c.Topic == "space").Take(2)), Is.EqualTo(new[] { "s1", "s2" }));
        }

        [Test]
        public void FiltersSpreadOverSeveralWheres()
        {
            var ids = Ids(Clips.Where(c => c.Topic == "pets").Where(c => c.Text.Similar("cat")).Where(c => c.Rank >= 3).Where(c => c.OwnerId != "carol"));

            Assert.That(ids, Is.EqualTo(new[] { "p3", "p4" }));
        }

        [Test]
        public void FilterGivenAsALuceneQuery()
        {
            var ids = Ids(Clips.Where(new TermQuery(new Term("Topic", "space"))).Where(c => c.Text.Similar("cat")).Take(2));

            Assert.That(ids, Is.EqualTo(new[] { "s1", "s2" }));
        }

        [Test]
        public void FilterWithAlternatives()
        {
            var ids = Ids(Clips.Where(c => c.Topic == "space" || c.Topic == "filler").Where(c => c.Text.Similar("cat")).Take(3));

            Assert.That(ids.Take(2), Is.EqualTo(new[] { "s1", "s2" }));
            Assert.That(ids.Length, Is.EqualTo(3));
        }

        [Test]
        public void FilterThatOnlyExcludes()
        {
            var ids = Ids(Clips.Where(c => c.Text.Similar("cat") && c.Topic != "pets" && c.Topic != "filler"));

            Assert.That(ids, Is.EqualTo(new[] { "s1", "s2", "s3" }));
        }

        [Test]
        public void WithAFilter_NothingIsCutOff()
        {
            // Twelve clips are not about space. All of them come back, most similar first.
            var ids = Ids(Clips.Where(c => c.Topic != "space" && c.Text.Similar("cat")));

            Assert.That(ids.Length, Is.EqualTo(12));
            Assert.That(ids.Take(5), Is.EqualTo(new[] { "p1", "p2", "p3", "p4", "p5" }));
        }

        [Test]
        public void OnlyTheSelectedDocumentsAreScored()
        {
            var hits = -1;
            var ids = Ids(Clips.CaptureStatistics(s => hits = s.TotalHits).Where(c => c.Topic == "space").Where(c => c.Text.Similar("cat")).Take(1));

            Assert.That(ids, Is.EqualTo(new[] { "s1" }));
            Assert.That(hits, Is.EqualTo(3), "documents the search matched, each scored once");
        }

        [Test]
        public void OrderByAnotherField_KeepsTheFilter()
        {
            var ids = Ids(Clips.Where(c => c.Topic == "space").Where(c => c.Text.Similar("cat")).OrderByDescending(c => c.Rank));

            Assert.That(ids, Is.EqualTo(new[] { "s3", "s2", "s1" }));
        }

        [Test]
        public void CountAndAny()
        {
            Assert.That(Clips.Where(c => c.Topic == "space").Where(c => c.Text.Similar("cat")).Count(), Is.EqualTo(3));
            Assert.That(Clips.Where(c => c.Topic == "space").Any(c => c.Text.Similar("cat")), Is.True);
            Assert.That(Clips.Where(c => c.Topic == "nothing").Any(c => c.Text.Similar("cat")), Is.False);
        }

        [Test]
        public void TwoSimilarCalls_AddTheirScores()
        {
            // p4 is fourth for "cat" but the only clip captioned "rain".
            var ids = Ids(Clips.Where(c => c.Topic == "pets" && c.Text.Similar("cat") && c.Caption.Similar("rain")));

            Assert.That(ids.Length, Is.EqualTo(6));
            Assert.That(ids[0], Is.EqualTo("p4"));
            Assert.That(ids[1], Is.EqualTo("p1"));
        }

        [Test]
        public void TwoSimilarCalls_WithoutAFilter()
        {
            var ids = Ids(Clips.Where(c => c.Text.Similar("cat")).Where(c => c.Caption.Similar("rain")).Take(2));

            Assert.That(ids, Is.EqualTo(new[] { "p4", "p1" }));
        }

        // ---- Similar alone: the nearest K, chosen after the search filter -------

        [Test]
        public void Alone_TheDocumentTypeFiltersBeforeTheNearestAreChosen()
        {
            // The five memos are all as near as p1. They must not use up the three places.
            Assert.That(Ids(Clips.Where(c => c.Text.Similar("cat")).Take(3)), Is.EqualTo(new[] { "p1", "p2", "p3" }));
        }

        [Test]
        public void Alone_WithoutTake_ReturnsTen()
        {
            var ids = Ids(Clips.Where(c => c.Text.Similar("cat")));

            Assert.That(ids.Length, Is.EqualTo(10));
            Assert.That(ids.Take(7), Is.EqualTo(new[] { "p1", "p2", "p3", "s1", "p4", "p5", "s2" }));
        }

        [Test]
        public void Alone_SkipAndTake()
        {
            Assert.That(Ids(Clips.Where(c => c.Text.Similar("cat")).Skip(2).Take(3)), Is.EqualTo(new[] { "p3", "s1", "p4" }));
        }

        [Test]
        public void Alone_ComparesOnlyDocumentsOfTheType()
        {
            LuceneQueryStatistics stats = null;
            var ids = Ids(Clips.CaptureStatistics(s => stats = s).Where(c => c.Text.Similar("cat")).Take(3));

            Assert.That(ids, Is.EqualTo(new[] { "p1", "p2", "p3" }));

            var nearest = Find<FilteredVectorQuery>(stats.Query);
            Assert.That(nearest, Is.Not.Null, "query was " + stats.Query);

            if (nearest.LastStrategy == "exact")
            {
                // 15 clips; not the 5 memos, which have the same vector field.
                Assert.That(nearest.LastComparisons, Is.EqualTo(AllClips.Length));
            }

#if NET10_0_OR_GREATER
            Assert.That(nearest.LastStrategy, Is.EqualTo(exactLimit == 0 ? "index" : "exact"));
#else
            Assert.That(nearest.LastStrategy, Is.EqualTo("exact"));
#endif
        }

        [Test]
        public void Alone_WhenTheIndexsNearestAreAllOfAnotherType()
        {
            // 105 memos nearer than any clip but p1, and 75 clips. Above the limit
            // the approximate index is asked for a multiple of what is needed; here
            // every one of those is a memo, so the clips are compared one by one.
            Add(Enumerable.Range(6, 100).Select(i => new Memo { Id = "m" + i, Text = "cat" }).ToArray());
            Add(Enumerable.Range(7, 60).Select(i => new Clip { Id = "f" + i, Topic = "filler", OwnerId = "nobody", Text = "fish", Caption = "music", Rank = 100 + i }).ToArray());

            LuceneQueryStatistics stats = null;
            var ids = Ids(Clips.CaptureStatistics(s => stats = s).Where(c => c.Text.Similar("cat")).Skip(1).Take(3));

            Assert.That(ids, Is.EqualTo(new[] { "p2", "p3", "s1" }));

            var nearest = Find<FilteredVectorQuery>(stats.Query);
            Assert.That(nearest.LastStrategy, Is.EqualTo("exact"));
            Assert.That(nearest.LastComparisons, Is.EqualTo(75));
        }

        [Test]
        public void Alone_AfterADelete()
        {
            using (var session = provider.OpenSession<Clip>())
            {
                session.Delete(AllClips[0]);
                session.Commit();
            }

            Assert.That(Ids(Clips.Where(c => c.Text.Similar("cat")).Take(3)), Is.EqualTo(new[] { "p2", "p3", "s1" }));
        }

        // ---- Similar as an alternative, and negated -----------------------------

        [Test]
        public void SimilarAsAnAlternative_AddsTheNearestToTheOtherMatches()
        {
            // The filler clips, or the ten nearest to "cat" (which include every clip that mentions a cat).
            var ids = Ids(Clips.Where(c => c.Topic == "filler" || c.Text.Similar("cat")).Take(100));

            Assert.That(ids, Is.SupersetOf(new[] { "f1", "f2", "f3", "f4", "f5", "f6", "p1", "p2", "p3", "p4", "p5", "s1", "s2" }));
        }

        [Test]
        public void NegatedSimilar_Throws()
        {
            var error = Assert.Throws<NotSupportedException>(() => Clips.Where(c => c.Topic == "pets" && !c.Text.Similar("cat")).ToList());

            Assert.That(error.Message, Does.Contain("cannot be negated"));
        }

        // ---- joins ---------------------------------------------------------------

        [Test]
        public void JoinInsideLucene_NarrowsBeforeTheNearestAreChosen()
        {
            // Clips of American owners: s1, p5, s2, then p6 and s3. The two nearest overall are German.
            var ids = (from c in Clips.Where(x => x.Text.Similar("cat"))
                       join o in Owners.Where(x => x.Country == "US") on c.OwnerId equals o.Username
                       select c).Take(2).ToList().Select(c => c.Id).ToArray();

            Assert.That(trace, Has.Some.Contains("pushed down"));
            Assert.That(ids, Is.EqualTo(new[] { "s1", "p5" }));
        }

        [Test]
        public void JoinInsideLucene_SimilarOnTheJoinedSequence()
        {
            var ids = (from o in Owners.Where(x => x.Country == "US")
                       join c in Clips.Where(x => x.Text.Similar("cat")) on o.Username equals c.OwnerId
                       select c).Take(2).ToList().Select(c => c.Id).ToArray();

            Assert.That(trace, Has.Some.Contains("pushed down"));
            Assert.That(ids, Is.EqualTo(new[] { "s1", "p5" }));
        }

        [Test]
        public void JoinInsideLucene_WithAFilterAndSimilar()
        {
            var ids = (from c in Clips.Where(x => x.Topic == "pets").Where(x => x.Text.Similar("cat"))
                       join o in Owners.Where(x => x.Country == "US") on c.OwnerId equals o.Username
                       select c).ToList().Select(c => c.Id).ToArray();

            Assert.That(ids, Is.EqualTo(new[] { "p5", "p6" }));
        }

        [Test]
        public void Join_SimilarOnTheSideThatOnlyFilters_IsJoinedInMemory()
        {
            // The owners of the two clips nearest to "cat" (both alice's).
            var names = (from o in Owners
                         join c in Clips.Where(x => x.Text.Similar("cat")).Take(2) on o.Username equals c.OwnerId
                         select o.Username).ToList();

            Assert.That(trace, Has.None.Contains("pushed down"));
            Assert.That(names, Is.EqualTo(new[] { "alice", "alice" }));

            // Any() needs no rows, so the planner runs it on the clips with the owners as the filter.
            trace.Clear();
            var any = (from o in Owners.Where(x => x.Country == "US")
                       join c in Clips.Where(x => x.Text.Similar("cat")) on o.Username equals c.OwnerId
                       select o).Any();

            Assert.That(trace, Has.Some.Contains("Clip filtered by Owner"), string.Join("; ", trace));
            Assert.That(any, Is.True);
        }

        private static T Find<T>(Query query) where T : Query
        {
            if (query is T found) return found;

            if (query is BooleanQuery boolean)
            {
                foreach (var clause in boolean.GetClauses())
                {
                    var inner = Find<T>(clause.Query);
                    if (inner != null) return inner;
                }
            }

            return null;
        }
    }

    /// <summary>The shape changes <see cref="VectorQueryResolver"/> makes, without an index.</summary>
    [TestFixture]
    public class VectorQueryResolverTests
    {
        private static readonly float[] Vector = { 1f, 0f };

        private static BooleanQuery All(params Query[] queries)
        {
            var boolean = new BooleanQuery();
            foreach (var query in queries) boolean.Add(query, Occur.MUST);
            return boolean;
        }

        private static Query Term(string value) => new TermQuery(new Term("f", value));

        private static Query Resolve(Query query, Filter filter = null)
        {
            using (var directory = new RAMDirectory())
            {
                using (var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, new Lucene.Net.Analysis.Core.KeywordAnalyzer())))
                {
                    writer.Commit();
                }

                using (var reader = DirectoryReader.Open(directory))
                {
                    return VectorQueryResolver.Resolve(query, filter, reader, int.MaxValue, 0, _ => null, 10000);
                }
            }
        }

        [Test]
        public void QueryWithoutAVector_IsReturnedUnchanged()
        {
            var query = All(All(Term("a")), All(Term("b")));

            Assert.That(Resolve(query), Is.SameAs(query));
        }

        [Test]
        public void NestedRequiredGroups_BecomeOneSelection()
        {
            var query = All(All(Term("a")), All(new DeferredVectorQuery("v", Vector)), All(Term("b")));

            var resolved = Resolve(query);

            // The vector's own group is merged away; groups without a vector keep their shape.
            Assert.That(resolved, Is.InstanceOf<Iciclecreek.Lucene.Net.Vector.VectorScoreQuery>());
            Assert.That(resolved.ToString(), Does.Contain("subQuery=+(+f:a) +(+f:b),"));
        }

        [Test]
        public void GroupsWithoutAVector_KeepTheirShape()
        {
            var either = new BooleanQuery();
            either.Add(Term("a"), Occur.SHOULD);
            either.Add(Term("b"), Occur.SHOULD);

            var resolved = Resolve(All(either, All(Term("c")), new DeferredVectorQuery("v", Vector)));

            Assert.That(resolved.ToString(), Does.Contain("+(f:a f:b) +(+f:c)"));
        }

        [Test]
        public void OnlyExclusions_SelectFromEverything()
        {
            var query = new BooleanQuery();
            query.Add(new DeferredVectorQuery("v", Vector), Occur.MUST);
            query.Add(Term("a"), Occur.MUST_NOT);

            Assert.That(Resolve(query).ToString(), Does.Contain("-f:a +*:*"));
        }

        [Test]
        public void SimilarAlone_WithAFilter_IsFilteredBeforeTheNearestAreChosen()
        {
            var filter = new QueryWrapperFilter(Term("a"));

            var resolved = Resolve(All(new DeferredVectorQuery("v", Vector)), filter);

            var nearest = ((BooleanQuery)resolved).GetClauses().Single().Query;
            Assert.That(nearest, Is.InstanceOf<FilteredVectorQuery>());
            Assert.That(((FilteredVectorQuery)nearest).Filter, Is.SameAs(filter));
            Assert.That(((FilteredVectorQuery)nearest).K, Is.EqualTo(10));
        }

        [Test]
        public void NegatedVector_Throws()
        {
            var query = new BooleanQuery();
            query.Add(Term("a"), Occur.MUST);
            query.Add(new DeferredVectorQuery("v", Vector), Occur.MUST_NOT);

            Assert.Throws<NotSupportedException>(() => Resolve(query));
        }
    }
}
