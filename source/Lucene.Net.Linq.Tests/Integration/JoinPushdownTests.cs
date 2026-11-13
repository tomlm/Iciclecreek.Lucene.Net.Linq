using System;
using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Store;
using Lucene.Net.Util;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    /// <summary>
    /// Joins whose result uses one side run inside Lucene. Every test runs its
    /// query with pushdown on and off and expects the same rows, and checks
    /// which path was taken.
    /// </summary>
    [TestFixture]
    public class JoinPushdownTests
    {
        public class Writer
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string Username { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string Country { get; set; }

            [Field]
            public string Nick { get; set; }                       // default: lowercased

            [Field(DocValues = true)]
            public string NickWithDocValues { get; set; }          // lowercased term, original-case column

            [Field(Analyzer = typeof(StandardAnalyzer))]
            public string Bio { get; set; }                        // several terms

            [NumericField]
            public int Number { get; set; }
        }

        public class Story
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string StoryId { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string WriterId { get; set; }

            // Not named SectionId: a session replaces documents by key field and
            // value, whatever their type, so Section's key must not match stories.
            [Field(IndexMode.NotAnalyzed)]
            public string InSection { get; set; }

            [Field]
            public string WriterNick { get; set; }                 // default: lowercased

            [Field(Analyzer = typeof(StandardAnalyzer))]
            public string Summary { get; set; }

            [NumericField]
            public int WriterNumber { get; set; }

            [NumericField]
            public int Rank { get; set; }
        }

        public class Section
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string SectionId { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string Label { get; set; }
        }

        private RAMDirectory directory;
        private LuceneDataProvider provider;
        private List<string> trace;

        private IQueryable<Writer> Writers => provider.AsQueryable<Writer>();
        private IQueryable<Story> Stories => provider.AsQueryable<Story>();
        private IQueryable<Section> Sections => provider.AsQueryable<Section>();

        [SetUp]
        public void SetUp()
        {
            directory = new RAMDirectory();
            provider = new LuceneDataProvider(directory, LuceneVersion.LUCENE_48);
            trace = new List<string>();
            provider.Settings.JoinPushdownTrace = trace.Add;

            Add(new Writer { Username = "alice", Country = "DE", Nick = "Ally", NickWithDocValues = "Ally", Bio = "alice writes", Number = 1 },
                new Writer { Username = "bob", Country = "US", Nick = "bobby", NickWithDocValues = "bobby", Bio = "bob writes", Number = 2 },
                new Writer { Username = "carol", Country = "DE", Nick = "caz", NickWithDocValues = "caz", Bio = "carol writes", Number = 3 });

            Add(new Story { StoryId = "s1", WriterId = "alice", InSection = "tech", WriterNick = "ally", Summary = "alice writes", WriterNumber = 1, Rank = 5 },
                new Story { StoryId = "s2", WriterId = "bob", InSection = "life", WriterNick = "BOBBY", Summary = "bob writes", WriterNumber = 2, Rank = 4 },
                new Story { StoryId = "s3", WriterId = "alice", InSection = "life", WriterNick = "ALLY", Summary = "alice writes", WriterNumber = 1, Rank = 3 },
                new Story { StoryId = "s4", WriterId = "nobody", InSection = "tech", WriterNick = "nobody", Summary = "nobody writes", WriterNumber = 9, Rank = 2 },
                new Story { StoryId = "s5", WriterId = "bob", InSection = "none", WriterNick = "bobby", Summary = "bob writes", WriterNumber = 2, Rank = 1 });

            Add(new Section { SectionId = "tech", Label = "Technology" },
                new Section { SectionId = "life", Label = "Lifestyle" });
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

        /// <summary>
        /// Runs <paramref name="query"/> with pushdown on, checks the trace, runs
        /// it again with pushdown off, and returns the first result after
        /// checking that both are the same.
        /// </summary>
        private TResult BothPaths<TResult>(Func<TResult> query, string expectedTrace, bool ordered = false)
        {
            provider.Settings.EnableJoinPushdown = true;
            trace.Clear();
            var pushed = query();
            var messages = trace.ToList();

            Assert.That(messages, Has.Some.Contains(expectedTrace), "pushdown trace");

            provider.Settings.EnableJoinPushdown = false;
            trace.Clear();
            var inMemory = query();

            Assert.That(trace, Has.All.Contains("disabled"));
            provider.Settings.EnableJoinPushdown = true;

            if (pushed is System.Collections.IEnumerable && !(pushed is string))
            {
                Assert.That(pushed, ordered ? (NUnit.Framework.Constraints.IResolveConstraint)Is.EqualTo(inMemory) : Is.EquivalentTo((System.Collections.IEnumerable)inMemory),
                    "pushed-down rows vs in-memory rows");
            }
            else
            {
                Assert.That(pushed, Is.EqualTo(inMemory), "pushed-down result vs in-memory result");
            }

            return pushed;
        }

        private const string Pushed = "pushed down";

        // ---- one-sided results --------------------------------------------------

        [Test]
        public void OuterSideSelected_ManyToOne()
        {
            var ids = BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s.StoryId).ToList(), Pushed);

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
        }

        [Test]
        public void OuterSideSelected_WholeDocuments()
        {
            var stories = BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s).ToList()
                .Select(s => s.StoryId + ":" + s.Rank).ToList(), Pushed);

            Assert.That(stories, Is.EquivalentTo(new[] { "s1:5", "s2:4", "s3:3", "s5:1" }));
        }

        [Test]
        public void InnerSideSelected_OneToMany()
        {
            var ids = BothPaths(() => (from w in Writers join s in Stories on w.Username equals s.WriterId select s.StoryId).ToList(), Pushed);

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
        }

        [Test]
        public void InnerSideFilter_NarrowsTheOuterSide()
        {
            var ids = BothPaths(() => (from s in Stories
                                       join w in Writers.Where(w => w.Country == "DE") on s.WriterId equals w.Username
                                       select s.StoryId).ToList(), Pushed);

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s3" }));
        }

        [Test]
        public void OuterSideFilter_WhenInnerSideSelected()
        {
            var ids = BothPaths(() => (from w in Writers.Where(w => w.Country == "US")
                                       join s in Stories on w.Username equals s.WriterId
                                       select s.StoryId).ToList(), Pushed);

            Assert.That(ids, Is.EquivalentTo(new[] { "s2", "s5" }));
        }

        [Test]
        public void WhereAfterTheJoin_OnTheSelectedOuterSide()
        {
            var ids = BothPaths(() => (from s in Stories
                                       join w in Writers on s.WriterId equals w.Username
                                       where s.InSection == "life"
                                       select s.StoryId).ToList(), Pushed);

            Assert.That(ids, Is.EquivalentTo(new[] { "s2", "s3" }));
        }

        [Test]
        public void Count_OverAJoin()
        {
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s).Count(), Pushed), Is.EqualTo(4));
            Assert.That(BothPaths(() => (from w in Writers join s in Stories on w.Username equals s.WriterId select s).Count(), Pushed), Is.EqualTo(4));
        }

        [Test]
        public void Count_IgnoresWhatTheSelectMentions()
        {
            var count = BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select new { s.StoryId, w.Country }).Count(), Pushed);

            Assert.That(count, Is.EqualTo(4));
        }

        [Test]
        public void Any_OverAJoin()
        {
            Assert.That(BothPaths(() => (from s in Stories.Where(x => x.StoryId == "s4") join w in Writers on s.WriterId equals w.Username select s).Any(), Pushed), Is.False);
            Assert.That(BothPaths(() => (from s in Stories.Where(x => x.StoryId == "s1") join w in Writers on s.WriterId equals w.Username select s).Any(), Pushed), Is.True);
        }

        [Test]
        public void Any_DoesNotNeedAUniqueKey()
        {
            // Story.WriterId is not unique, which would rule out a row-producing query.
            Assert.That(BothPaths(() => (from w in Writers join s in Stories on w.Username equals s.WriterId select w).Any(), Pushed), Is.True);
        }

        [Test]
        public void SkipTakeAndOrderBy_RunOnJoinedRows()
        {
            // s4 has the fourth rank but no writer, so it must not use up a slot.
            var ids = BothPaths(() => (from s in Stories
                                       join w in Writers on s.WriterId equals w.Username
                                       orderby s.Rank descending
                                       select s.StoryId).Skip(1).Take(3).ToList(), Pushed, ordered: true);

            Assert.That(ids, Is.EqualTo(new[] { "s2", "s3", "s5" }));
        }

        [Test]
        public void First_OnAJoin()
        {
            var id = BothPaths(() => (from s in Stories.OrderBy(x => x.Rank)
                                      join w in Writers on s.WriterId equals w.Username
                                      select s.StoryId).First(), Pushed);

            Assert.That(id, Is.EqualTo("s5"));
        }

        [Test]
        public void InnerSideSelected_OrderedAndPagedInLucene()
        {
            // The in-memory path cannot order by the inner side, so this is checked against literals.
            trace.Clear();

            var ids = (from w in Writers
                       join s in Stories on w.Username equals s.WriterId
                       orderby s.Rank
                       select s.StoryId).Take(2).ToList();

            Assert.That(trace, Has.Some.Contains(Pushed));
            Assert.That(ids, Is.EqualTo(new[] { "s5", "s3" }));
        }

        [Test]
        public void DefaultMappedKeys_JoinCaseInsensitively()
        {
            var ids = BothPaths(() => (from s in Stories join w in Writers on s.WriterNick equals w.Nick select s.StoryId).ToList(),
                "is not the only key");   // Nick is not Writer's key, so rows could repeat

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));

            var any = BothPaths(() => (from s in Stories.Where(x => x.StoryId == "s3") join w in Writers on s.WriterNick equals w.Nick select s).Any(), Pushed);

            Assert.That(any, Is.True, "ALLY joins Ally on lowercased fields");
        }

        // ---- chained joins ------------------------------------------------------

        [Test]
        public void ThreeWayJoin_BothJoinsPushedDown()
        {
            var ids = BothPaths(() => (from s in Stories
                                       join w in Writers on s.WriterId equals w.Username
                                       join c in Sections on s.InSection equals c.SectionId
                                       select s.StoryId).ToList(), Pushed);

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3" }));

            trace.Clear();
            (from s in Stories
             join w in Writers on s.WriterId equals w.Username
             join c in Sections on s.InSection equals c.SectionId
             select s.StoryId).ToList();

            Assert.That(trace.Count(m => m.Contains(Pushed)), Is.EqualTo(2), "both joins run inside Lucene");
        }

        [Test]
        public void ThreeWayJoin_PushesTheUnusedSideAndJoinsTheRestInMemory()
        {
            var rows = BothPaths(() => (from s in Stories
                                        join w in Writers.Where(w => w.Country == "DE") on s.WriterId equals w.Username
                                        join c in Sections on s.InSection equals c.SectionId
                                        select new { s.StoryId, c.Label }).ToList().Select(r => r.StoryId + ":" + r.Label).ToList(), Pushed);

            Assert.That(rows, Is.EquivalentTo(new[] { "s1:Technology", "s3:Lifestyle" }));
        }

        [Test]
        public void ThreeWayJoin_ReportsTheJoinItLeavesInMemory()
        {
            trace.Clear();

            (from s in Stories
             join w in Writers on s.WriterId equals w.Username
             join c in Sections on s.InSection equals c.SectionId
             select new { s.StoryId, c.Label }).ToList();

            Assert.That(trace, Has.Some.Contains(Pushed));
            Assert.That(trace, Has.Some.Contains("uses both sides"));
        }

        // ---- results that use both sides ----------------------------------------

        [Test]
        public void BothSidesUsed_InnerFilterNarrowsTheOuterLoad()
        {
            var outerHits = -1;

            Func<List<string>> query = () => (from s in Stories.CaptureStatistics(stats => outerHits = stats.TotalHits)
                                              join w in Writers.Where(w => w.Country == "DE") on s.WriterId equals w.Username
                                              select new { s.StoryId, w.Username }).ToList().Select(r => r.StoryId + ":" + r.Username).ToList();

            var rows = BothPaths(query, "narrowed");

            Assert.That(rows, Is.EquivalentTo(new[] { "s1:alice", "s3:alice" }));

            provider.Settings.EnableJoinPushdown = false;
            query();
            Assert.That(outerHits, Is.EqualTo(5), "in memory, every story is loaded");

            provider.Settings.EnableJoinPushdown = true;
            query();
            Assert.That(outerHits, Is.EqualTo(2), "narrowed, only stories by German writers are loaded");
        }

        [Test]
        public void BothSidesUsed_OneToMany_RowsRepeat()
        {
            var rows = BothPaths(() => (from w in Writers
                                        join s in Stories on w.Username equals s.WriterId
                                        select new { w.Username, s.StoryId }).ToList().Select(r => r.Username + ":" + r.StoryId).ToList(), "narrowed");

            Assert.That(rows, Is.EquivalentTo(new[] { "alice:s1", "alice:s3", "bob:s2", "bob:s5" }));
        }

        [Test]
        public void BothSidesUsed_PagingAndOrder()
        {
            var rows = BothPaths(() => (from s in Stories
                                        join w in Writers on s.WriterId equals w.Username
                                        orderby s.Rank descending
                                        select new { s.StoryId, w.Country }).Skip(1).Take(2).ToList().Select(r => r.StoryId + ":" + r.Country).ToList(),
                "narrowed", ordered: true);

            Assert.That(rows, Is.EqualTo(new[] { "s2:US", "s3:DE" }));
        }

        [Test]
        public void BothSidesUsed_ThreeWay_NarrowsByEachJoin()
        {
            var outerHits = -1;

            var rows = BothPaths(() => (from s in Stories.CaptureStatistics(stats => outerHits = stats.TotalHits)
                                        join w in Writers on s.WriterId equals w.Username
                                        join c in Sections on s.InSection equals c.SectionId
                                        select new { s.StoryId, w.Country, c.Label }).ToList()
                .Select(r => r.StoryId + ":" + r.Country + ":" + r.Label).ToList(), "narrowed");

            Assert.That(rows, Is.EquivalentTo(new[] { "s1:DE:Technology", "s2:US:Lifestyle", "s3:DE:Lifestyle" }));

            (from s in Stories.CaptureStatistics(stats => outerHits = stats.TotalHits)
             join w in Writers on s.WriterId equals w.Username
             join c in Sections on s.InSection equals c.SectionId
             select new { s.StoryId, w.Country, c.Label }).ToList();

            Assert.That(outerHits, Is.EqualTo(3), "s4 has no writer and s5 has no section");
        }

        [Test]
        public void BothSidesUsed_NotNarrowedWhenKeysDoNotQualify()
        {
            trace.Clear();

            (from s in Stories join w in Writers on s.WriterNumber equals w.Number select new { s.StoryId, w.Country }).ToList();

            Assert.That(trace, Has.None.Contains("narrowed"));
        }

        [Test]
        public void BothSidesUsed_NotNarrowedWhenInnerSequenceIsPaged()
        {
            var count = BothPaths(() => (from s in Stories join w in Writers.Take(2) on s.WriterId equals w.Username select new { s.StoryId, w.Country }).ToList().Count,
                "result operators");

            Assert.That(trace, Has.None.Contains("narrowed"));
            Assert.That(count, Is.GreaterThan(0));
        }

        // ---- reload -------------------------------------------------------------

        [Test]
        public void AfterSearcherReload_JoinSeesNewDocumentsOnBothSides()
        {
            BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s.StoryId).ToList(), Pushed);

            // Each commit reloads the searcher.
            Add(new Writer { Username = "nobody", Country = "FR" });
            Add(new Story { StoryId = "s6", WriterId = "carol", InSection = "tech", Rank = 6 });

            var ids = BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s.StoryId).ToList(), Pushed);

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s4", "s5", "s6" }));
        }

        [Test]
        public void SessionQuery_AsEitherSide()
        {
            using (var stories = provider.OpenSession<Story>())
            using (var writers = provider.OpenSession<Writer>())
            {
                var outer = BothPaths(() => (from s in stories.Query() join w in writers.Query() on s.WriterId equals w.Username select s).ToList()
                    .Select(s => s.StoryId).ToList(), Pushed);

                var inner = BothPaths(() => (from w in writers.Query() join s in stories.Query() on w.Username equals s.WriterId select s).ToList()
                    .Select(s => s.StoryId).ToList(), Pushed);

                Assert.That(outer, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
                Assert.That(inner, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
            }
        }

        // ---- fallbacks ----------------------------------------------------------

        [Test]
        public void Fallback_ResultUsesBothSides()
        {
            var rows = BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select new { s.StoryId, w.Country })
                .ToList().Select(r => r.StoryId + ":" + r.Country).ToList(), "uses both sides");

            Assert.That(rows, Is.EquivalentTo(new[] { "s1:DE", "s2:US", "s3:DE", "s5:US" }));
        }

        [Test]
        public void Fallback_OtherSideKeyIsNotUnique_KeepsRepeatedRows()
        {
            var names = BothPaths(() => (from w in Writers join s in Stories on w.Username equals s.WriterId select w.Username).ToList(),
                "is not the only key");

            Assert.That(names, Is.EquivalentTo(new[] { "alice", "alice", "bob", "bob" }));
        }

        [Test]
        public void Fallback_CompositeKey()
        {
            var ids = BothPaths(() => (from s in Stories
                                       join w in Writers on new { K = s.WriterId, N = s.WriterNumber } equals new { K = w.Username, N = w.Number }
                                       select s.StoryId).ToList(), "composite, computed");

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
        }

        [Test]
        public void Fallback_ComputedKey()
        {
            var ids = BothPaths(() => (from s in Stories join w in Writers on s.WriterId.ToUpper() equals w.Username.ToUpper() select s.StoryId).ToList(),
                "composite, computed");

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
        }

        [Test]
        public void Fallback_NumericKey()
        {
            var ids = BothPaths(() => (from s in Stories join w in Writers on s.WriterNumber equals w.Number select s.StoryId).ToList(),
                "not indexed as one string term");

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
        }

        [Test]
        public void Fallback_KeyAnalyzedIntoSeveralTerms()
        {
            BothPaths(() => (from s in Stories join w in Writers on s.Summary equals w.Bio select s.StoryId).ToList(),
                "not indexed as one string term");
        }

        [Test]
        public void Fallback_LowercasedKeyWithDocValues()
        {
            var any = BothPaths(() => (from s in Stories.Where(x => x.StoryId == "s3") join w in Writers on s.WriterNick equals w.NickWithDocValues select s).Any(),
                "not indexed as one string term");

            Assert.That(any, Is.True);
        }

        [Test]
        public void Fallback_KeysIndexedDifferently()
        {
            BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Nick select s.StoryId).ToList(), "indexed differently");
        }

        [Test]
        public void Fallback_InnerSequenceHasResultOperators()
        {
            var ids = BothPaths(() => (from s in Stories
                                       join w in Writers.OrderBy(x => x.Username).Take(2) on s.WriterId equals w.Username
                                       select s.StoryId).ToList(), "result operators");

            // alice and bob are the first two writers; carol has no stories anyway.
            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
            Assert.That(trace, Has.None.Contains("narrowed"));
        }

        [Test]
        public void Fallback_OuterSideOrdered_WhenInnerSideSelected()
        {
            var ids = BothPaths(() => (from w in Writers.OrderByDescending(x => x.Username)
                                       join s in Stories on w.Username equals s.WriterId
                                       select s.StoryId).ToList(), "outer side is ordered", ordered: true);

            Assert.That(ids.Take(2), Is.EquivalentTo(new[] { "s2", "s5" }), "bob's stories come first");
        }

        [Test]
        public void Fallback_DifferentSearchers()
        {
            using (var otherDirectory = new RAMDirectory())
            using (var other = new LuceneDataProvider(otherDirectory, LuceneVersion.LUCENE_48))
            {
                using (var session = other.OpenSession<Writer>())
                {
                    session.Add(new Writer { Username = "alice", Country = "DE" });
                    session.Commit();
                }

                var ids = BothPaths(() => (from s in Stories join w in other.AsQueryable<Writer>() on s.WriterId equals w.Username select s.StoryId).ToList(),
                    "different searchers");

                Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s3" }));
            }
        }

        [Test]
        public void Fallback_InnerSequenceIsNotALuceneQuery()
        {
            var local = new[] { new Writer { Username = "bob" } };

            var ids = BothPaths(() => (from s in Stories join w in local on s.WriterId equals w.Username select s.StoryId).ToList(),
                "not a Lucene query");

            Assert.That(ids, Is.EquivalentTo(new[] { "s2", "s5" }));
        }

        [Test]
        public void Fallback_GroupJoin_IsReportedAndBehavesAsBefore()
        {
            trace.Clear();

            Assert.Throws<InvalidOperationException>(() =>
                (from s in Stories join w in Writers on s.WriterId equals w.Username into ws select new { s.StoryId, Count = ws.Count() }).ToList());

            Assert.That(trace, Has.Some.Contains("group joins and outer joins"));
        }

        [Test]
        public void Fallback_OuterSourcePagedBeforeTheJoin()
        {
            // Rank order is s5, s4, s3, s2, s1: the page is s4 and s3, and only s3 has a writer.
            var ids = BothPaths(() => (from s in Stories.OrderBy(x => x.Rank).Skip(1).Take(2)
                                       join w in Writers on s.WriterId equals w.Username
                                       select s.StoryId).ToList(), "outer source has paging", ordered: true);

            Assert.That(ids, Is.EqualTo(new[] { "s3" }));
            Assert.That(trace, Has.None.Contains("narrowed"));

            // The same when the inner side is the one returned: only alice's stories.
            var inner = BothPaths(() => (from w in Writers.OrderBy(x => x.Username).Take(1)
                                         join s in Stories on w.Username equals s.WriterId
                                         select s.StoryId).ToList(), "outer source has paging");

            Assert.That(inner, Is.EquivalentTo(new[] { "s1", "s3" }));
        }

        [Test]
        public void Count_AfterTakeAndSkip()
        {
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s).Take(2).Count(), Pushed), Is.EqualTo(2));
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s).Skip(1).Take(2).Count(), Pushed), Is.EqualTo(2));
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s).Skip(3).Take(2).Count(), Pushed), Is.EqualTo(1));

            // The same handler serves queries without a join.
            Assert.That(Stories.Take(2).Count(), Is.EqualTo(2));
            Assert.That(Stories.Skip(4).Take(3).Count(), Is.EqualTo(1));
        }

        [Test]
        public void Count_AfterSkipPastWhatWasTaken_IsZero()
        {
            // Skipping more rows than were taken leaves none; the count must not go negative.
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s).Take(2).Skip(3).Count(), Pushed), Is.EqualTo(0));
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s).Take(3).Skip(1).Count(), Pushed), Is.EqualTo(2));
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s).Skip(9).Count(), Pushed), Is.EqualTo(0));

            // Without a join.
            Assert.That(Stories.Take(2).Skip(3).Count(), Is.EqualTo(0));
            Assert.That(Stories.Take(2).Skip(9).LongCount(), Is.EqualTo(0L));
            Assert.That(Stories.Take(3).Skip(1).Count(), Is.EqualTo(2));
            Assert.That(Stories.Skip(1).Take(3).Skip(1).Count(), Is.EqualTo(2));
            Assert.That(Stories.Take(0).Count(), Is.EqualTo(0));
            Assert.That(Stories.Skip(9).Count(), Is.EqualTo(0));
            Assert.That(Stories.Take(9).LongCount(), Is.EqualTo(5L));
            Assert.That(Sections.Where(c => c.SectionId == "none").Take(2).Count(), Is.EqualTo(0));
        }

        [Test]
        public void Fallback_MinOrMaxAfterPaging()
        {
            // Joined ranks are 5, 4, 3, 1. Lucene would sort by the value and
            // answer for all rows (1 and 5), ignoring the page.
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username orderby s.Rank descending select s.Rank).Take(2).Min(),
                "Min after Skip or Take only runs on rows in memory"), Is.EqualTo(4));

            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username orderby s.Rank select s.Rank).Skip(1).Take(2).Max(),
                "Max after Skip or Take only runs on rows in memory"), Is.EqualTo(4));

            // On their own they run in Lucene.
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s.Rank).Min(), Pushed), Is.EqualTo(1));
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s.Rank).Max(), Pushed), Is.EqualTo(5));
        }

        [Test]
        public void Last_AfterSkip()
        {
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username orderby s.Rank select s.StoryId).Skip(1).Last(), Pushed),
                Is.EqualTo("s1"));

            // Four joined rows: skipping them all leaves no last row.
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username orderby s.Rank select s.StoryId).Skip(4).LastOrDefault(), Pushed),
                Is.Null);

            Assert.That(() => (from s in Stories join w in Writers on s.WriterId equals w.Username orderby s.Rank select s.StoryId).Skip(9).Last(),
                Throws.InvalidOperationException);

            // Without a join.
            Assert.That(Stories.OrderBy(s => s.Rank).Select(s => s.StoryId).Skip(5).LastOrDefault(), Is.Null);
            Assert.That(Stories.OrderBy(s => s.Rank).Select(s => s.StoryId).Skip(4).Last(), Is.EqualTo("s1"));
            Assert.That(Stories.OrderBy(s => s.Rank).Select(s => s.StoryId).Take(2).Last(), Is.EqualTo("s4"));
        }

        [Test]
        public void Fallback_ResultOperatorsThatOnlyRunInMemory()
        {
            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s.Rank).Sum(),
                "Sum only runs on rows in memory"), Is.EqualTo(13));

            Assert.That(BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s.StoryId).Contains("s4"),
                "Contains only runs on rows in memory"), Is.False);

            var sections = BothPaths(() => (from s in Stories join w in Writers on s.WriterId equals w.Username select s.InSection).Distinct().ToList(),
                "Distinct only runs on rows in memory");

            Assert.That(sections, Is.EquivalentTo(new[] { "tech", "life", "none" }));
        }

        [Test]
        public void Fallback_InnerSequenceOrdered_WhenInnerSideSelected()
        {
            var ids = BothPaths(() => (from w in Writers
                                       join s in Stories.OrderBy(x => x.StoryId) on w.Username equals s.WriterId
                                       select s.StoryId).ToList(), "inner sequence is ordered");

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
        }

        [Test]
        public void Fallback_InnerSequenceHasAProjection()
        {
            var ids = BothPaths(() => (from s in Stories
                                       join w in Writers.Select(x => new { x.Username }) on s.WriterId equals w.Username
                                       select s.StoryId).ToList(), "projection");

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
        }

        [Test]
        public void Fallback_InnerSequenceHasClausesThatAreNotFilters()
        {
            var ids = BothPaths(() => (from s in Stories
                                       join w in Writers.CaptureStatistics(_ => { }) on s.WriterId equals w.Username
                                       select s.StoryId).ToList(), "cannot run as a join filter");

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
        }

        [Test]
        public void Fallback_QueryContainsASubQuery()
        {
            var wanted = new[] { "s1", "s2", "s4" };

            var ids = BothPaths(() => (from s in Stories
                                       join w in Writers on s.WriterId equals w.Username
                                       where wanted.Contains(s.StoryId)
                                       select s.StoryId).ToList(), "contains a subquery");

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2" }));
        }

        [Test]
        public void Fallback_OrderByScoreOfTheInnerSide()
        {
            trace.Clear();

            try
            {
                (from w in Writers join s in Stories on w.Username equals s.WriterId orderby s.Score() select s.StoryId).ToList();
            }
            catch (Exception)
            {
                // The in-memory path cannot order by the inner side; only the decision is under test.
            }

            Assert.That(trace, Has.Some.Contains("orders the inner side by score"));
        }

        [Test]
        public void Any_DocumentWithoutAKeyDoesNotJoinAnEmptyKey()
        {
            Add(new Section { SectionId = "unlabelled", Label = null });
            Add(new Story { StoryId = "s9", WriterId = "nobody", InSection = "", Rank = 9 });

            var any = BothPaths(() => (from s in Stories.Where(x => x.StoryId == "s9") join c in Sections on s.InSection equals c.Label select s).Any(), Pushed);

            Assert.That(any, Is.False);
        }

        [Test]
        public void InMemoryJoin_ThrowsItsOwnException()
        {
            provider.Settings.EnableJoinPushdown = false;

            // Ordering by the inner side is not supported in memory; the error must not be wrapped.
            Assert.That(() => (from w in Writers join s in Stories on w.Username equals s.WriterId orderby s.Rank select s.StoryId).ToList(),
                Throws.Exception.Not.InstanceOf<System.Reflection.TargetInvocationException>());
        }

        [Test]
        public void Disabled_AlwaysJoinsInMemory()
        {
            provider.Settings.EnableJoinPushdown = false;

            var ids = (from s in Stories join w in Writers on s.WriterId equals w.Username select s.StoryId).ToList();

            Assert.That(ids, Is.EquivalentTo(new[] { "s1", "s2", "s3", "s5" }));
            Assert.That(trace, Has.All.Contains("disabled"));
        }
    }
}
