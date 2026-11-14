using System;
using System.Linq;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Store;
using Lucene.Net.Util;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    /// <summary>
    /// Joins follow LINQ semantics (paging and scalars apply to the joined rows)
    /// and compare keys the way the key fields are indexed.
    /// </summary>
    [TestFixture]
    public class JoinSemanticsTests
    {
        public class Member
        {
            [Field(Key = true)]
            public string Name { get; set; }                     // default mapping: one lowercased term

            [Field(IndexMode.NotAnalyzed)]
            public string Exact { get; set; }

            [Field("uname", IndexMode.NotAnalyzed)]
            public string Renamed { get; set; }

            [NumericField]
            public int Number { get; set; }

            [Field]
            public DateTime Joined { get; set; }
        }

        public class Entry
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string Id { get; set; }

            [Field]
            public string Author { get; set; }                   // default mapping

            [Field(IndexMode.NotAnalyzed)]
            public string AuthorExact { get; set; }

            [Field(Analyzer = typeof(Lucene.Net.Analysis.Standard.StandardAnalyzer))]
            public string AuthorText { get; set; }               // tokenized and lowercased

            [NumericField]
            public int AuthorNumber { get; set; }

            [Field]
            public DateTime Day { get; set; }
        }

        private RAMDirectory directory;
        private LuceneDataProvider provider;

        private IQueryable<Member> Members => provider.AsQueryable<Member>();
        private IQueryable<Entry> Entries => provider.AsQueryable<Entry>();

        private static DateTime Day(int day) => new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp()
        {
            directory = new RAMDirectory();
            provider = new LuceneDataProvider(directory, LuceneVersion.LUCENE_48);

            using (var session = provider.OpenSession<Member>())
            {
                session.Add(new Member { Name = "alice", Exact = "alice", Renamed = "alice", Number = 1, Joined = Day(1) });
                session.Add(new Member { Name = "Bob", Exact = "Bob", Renamed = "Bob", Number = 2, Joined = Day(2) });
                session.Add(new Member { Name = "carol", Exact = "carol", Renamed = "carol", Number = 3, Joined = Day(3) });
                session.Commit();
            }

            using (var session = provider.OpenSession<Entry>())
            {
                session.Add(new Entry { Id = "e1", Author = "alice", AuthorExact = "alice", AuthorNumber = 1, Day = Day(1) });
                session.Add(new Entry { Id = "e2", Author = "ALICE", AuthorExact = "alice", AuthorNumber = 1, Day = Day(1) });
                session.Add(new Entry { Id = "e3", Author = "Bob", AuthorExact = "Bob", AuthorNumber = 2, Day = Day(2) });
                session.Add(new Entry { Id = "e4", Author = "nobody", AuthorExact = "nobody", AuthorNumber = 9, Day = Day(9) });
                session.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            provider.Dispose();
            directory.Dispose();
        }

        [Test]
        public void DefaultMappedKeys_JoinCaseInsensitively_LikeAQueryOnTheField()
        {
            var ids = (from e in Entries join m in Members on e.Author equals m.Name select e.Id).ToList();

            Assert.That(ids, Is.EquivalentTo(new[] { "e1", "e2", "e3" }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LowercasedKeyJoinedToExactKey_ComparesValues_CaseSensitively(bool pushdown)
        {
            provider.Settings.EnableJoinPushdown = pushdown;

            // "Bob" is indexed as "bob" in Author and as "Bob" in Exact: the values are equal, so they join.
            // "ALICE" and "alice" are different values, and the exact field makes the join case-sensitive.
            var entryFirst = (from e in Entries join m in Members on e.Author equals m.Exact select e.Id).ToList();
            var memberFirst = (from m in Members join e in Entries on m.Exact equals e.Author select e.Id).ToList();

            Assert.That(entryFirst, Is.EquivalentTo(new[] { "e1", "e3" }));
            Assert.That(memberFirst, Is.EquivalentTo(new[] { "e1", "e3" }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AnalyzedKeyJoinedToExactKey_ComparesValues_CaseSensitively(bool pushdown)
        {
            provider.Settings.EnableJoinPushdown = pushdown;

            using (var session = provider.OpenSession<Member>())
            {
                session.Add(new Member { Name = "m4", Exact = "01HZX7Q4KJ" });
                session.Add(new Member { Name = "m5", Exact = "two words" });
                session.Commit();
            }

            using (var session = provider.OpenSession<Entry>())
            {
                session.Add(new Entry { Id = "t1", AuthorText = "Bob" });
                session.Add(new Entry { Id = "t2", AuthorText = "bob" });
                session.Add(new Entry { Id = "t3", AuthorText = "01HZX7Q4KJ" });
                session.Add(new Entry { Id = "t4", AuthorText = "two words" });
                session.Add(new Entry { Id = "t5", AuthorText = "alice" });
                session.Commit();
            }

            var entryFirst = (from e in Entries join m in Members on e.AuthorText equals m.Exact select e.Id).ToList();
            var memberFirst = (from m in Members join e in Entries on m.Exact equals e.AuthorText select e.Id).ToList();

            Assert.That(entryFirst, Is.EquivalentTo(new[] { "t1", "t3", "t4", "t5" }));
            Assert.That(memberFirst, Is.EquivalentTo(new[] { "t1", "t3", "t4", "t5" }));
        }

        [Test]
        public void ExactKeys_JoinCaseSensitively()
        {
            using (var session = provider.OpenSession<Entry>())
            {
                session.Add(new Entry { Id = "e5", Author = "x", AuthorExact = "BOB", AuthorNumber = 0, Day = Day(5) });
                session.Commit();
            }

            var ids = (from e in Entries join m in Members on e.AuthorExact equals m.Exact select e.Id).ToList();

            Assert.That(ids, Is.EquivalentTo(new[] { "e1", "e2", "e3" }));
        }

        [Test]
        public void InnerKeyWithRenamedField()
        {
            var ids = (from e in Entries join m in Members on e.AuthorExact equals m.Renamed select e.Id).ToList();

            Assert.That(ids, Is.EquivalentTo(new[] { "e1", "e2", "e3" }));
        }

        [Test]
        public void NumericKeys()
        {
            var ids = (from e in Entries join m in Members on e.AuthorNumber equals m.Number select e.Id).ToList();

            Assert.That(ids, Is.EquivalentTo(new[] { "e1", "e2", "e3" }));
        }

        [Test]
        public void ConvertedKeys()
        {
            var ids = (from e in Entries join m in Members on e.Day equals m.Joined select e.Id).ToList();

            Assert.That(ids, Is.EquivalentTo(new[] { "e1", "e2", "e3" }));
        }

        [Test]
        public void Take_AppliesToJoinedRows()
        {
            // e4 sorts first but has no member, so it must not use up the Take.
            var ids = (from e in Entries.OrderByDescending(x => x.Id)
                       join m in Members on e.AuthorExact equals m.Exact
                       select e.Id).Take(1).ToList();

            Assert.That(ids, Is.EqualTo(new[] { "e3" }));
        }

        [Test]
        public void Take_OneToMany_LimitsRows()
        {
            var ids = (from m in Members.OrderBy(x => x.Exact)
                       join e in Entries on m.Exact equals e.AuthorExact
                       select e.Id).Take(2).ToList();

            Assert.That(ids.Count, Is.EqualTo(2));
        }

        [Test]
        public void Skip_AppliesToJoinedRows()
        {
            var ids = (from e in Entries.OrderByDescending(x => x.Id)
                       join m in Members on e.AuthorExact equals m.Exact
                       select e.Id).Skip(1).ToList();

            Assert.That(ids, Is.EqualTo(new[] { "e2", "e1" }));
        }

        [Test]
        public void Count_CountsJoinedRows()
        {
            var query = from e in Entries join m in Members on e.AuthorExact equals m.Exact select e.Id;

            Assert.That(query.Count(), Is.EqualTo(3));
            Assert.That(query.LongCount(), Is.EqualTo(3L));
        }

        [Test]
        public void Any_IsFalseWhenNothingJoins()
        {
            var unmatched = from e in Entries.Where(x => x.Id == "e4") join m in Members on e.AuthorExact equals m.Exact select e.Id;
            var matched = from e in Entries.Where(x => x.Id == "e3") join m in Members on e.AuthorExact equals m.Exact select e.Id;

            Assert.That(unmatched.Any(), Is.False);
            Assert.That(matched.Any(), Is.True);
        }

        [Test]
        public void First_ReturnsFirstJoinedRow()
        {
            var query = from e in Entries.OrderByDescending(x => x.Id)
                        join m in Members on e.AuthorExact equals m.Exact
                        select e.Id;

            Assert.That(query.First(), Is.EqualTo("e3"));
        }

        [Test]
        public void FirstOrDefault_AndSingle()
        {
            var none = from e in Entries.Where(x => x.Id == "e4") join m in Members on e.AuthorExact equals m.Exact select e.Id;
            var one = from e in Entries.Where(x => x.Id == "e3") join m in Members on e.AuthorExact equals m.Exact select e.Id;

            Assert.That(none.FirstOrDefault(), Is.Null);
            Assert.Throws<InvalidOperationException>(() => none.First());
            Assert.That(one.Single(), Is.EqualTo("e3"));
        }

        [Test]
        public void OneToMany_RepeatsTheOuterRow()
        {
            var names = (from m in Members join e in Entries on m.Exact equals e.AuthorExact select m.Exact).ToList();

            Assert.That(names, Is.EquivalentTo(new[] { "alice", "alice", "Bob" }));
        }
    }
}
