using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    [TestFixture]
    public class SubQueryTests : IntegrationTestBase
    {
        // The same documents, for comparing a query with LINQ to Objects.
        private List<SampleDocument> local;

        [SetUp]
        public void SetUp()
        {
            local = new List<SampleDocument>();

            Add(new SampleDocument { Id = "a", Scalar = 5});
            Add(new SampleDocument { Id = "b", Scalar = 1});
            Add(new SampleDocument { Id = "c", Scalar = 3});
        }

        private void Add(SampleDocument document)
        {
            AddDocument(document);
            local.Add(document);
        }

        [Test]
        public void SubquerySkip()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            var results = (from outer in
                               (from d in docs
                                orderby d.Id
                                select d).Skip(2)
                           select new { outer.Id })
                           .ToList();

            Assert.That(results.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "c" }));
        }

        [Test]
        public void MainSkipSubquerySkip()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            var results = (from outer in
                               (from d in docs
                                orderby d.Id descending
                                select d).Skip(1)
                           select new { outer.Id })
                           .Skip(1)
                           .ToList();

            Assert.That(results.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void MainOrderBy()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            var results = (from outer in
                               (from d in docs select d)
                           orderby outer.Id descending
                           select new { outer.Id })
                           .ToList();

            Assert.That(results.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "c", "b", "a" }));
        }

        [Test]
        public void OrderByAfterPagedSubQuery_ReordersThePage()
        {
            // By Scalar: b, c, a. Skip(1) leaves c, a. Ordered by Id: a, c. Skip(1) leaves c.
            var docs = provider.AsQueryable<SampleDocument>();

            var results = (from outer in
                               (from d in docs
                                orderby d.Scalar
                                select d).Skip(1)
                           orderby outer.Id
                           select new { outer.Id })
                           .Skip(1)
                           .ToList();

            Assert.That(results.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "c" }));
        }

        [Test]
        public void WhereAfterPagedSubQuery_FiltersThePage()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            // By Scalar: b(1), c(3), a(5). The first two are b and c; only c has Scalar > 1.
            var results = docs.OrderBy(d => d.Scalar).Take(2).Where(d => d.Scalar > 1).ToList();

            Assert.That(results.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "c" }));
            Assert.That(docs.OrderBy(d => d.Scalar).Take(2).Count(d => d.Scalar > 1), Is.EqualTo(1));
            Assert.That(docs.OrderBy(d => d.Scalar).Skip(1).Any(d => d.Scalar == 1), Is.False);
        }

        [Test]
        public void SelectAfterPagedSubQuery_StillRunsInLucene()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            var results = docs.OrderBy(d => d.Scalar).Take(2).Select(d => d.Id).ToList();

            Assert.That(results, Is.EqualTo(new[] { "b", "c" }));
        }

        [Test]
        public void LaterOrderByIsThePrimarySort()
        {
            AddDocument(new SampleDocument { Id = "a", Scalar = 1 });

            var docs = provider.AsQueryable<SampleDocument>();

            // Ids are a, a, b, c; the earlier ordering by Scalar only breaks the tie between the two a's.
            var results = docs.OrderByDescending(d => d.Scalar).OrderBy(d => d.Id).ToList();

            Assert.That(results.Select(d => d.Id + d.Scalar).ToArray(), Is.EqualTo(new[] { "a5", "a1", "b1", "c3" }));

            // Within one clause the first key is the primary one.
            var thenBy = docs.OrderBy(d => d.Id).ThenBy(d => d.Scalar).ToList();

            Assert.That(thenBy.Select(d => d.Id + d.Scalar).ToArray(), Is.EqualTo(new[] { "a1", "a5", "b1", "c3" }));
        }

        [Test]
        public void ResultOperatorsLuceneDoesNotImplement_RunOnTheRows()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            Assert.That(docs.Select(d => d.Scalar).Sum(), Is.EqualTo(9));
            Assert.That(docs.Select(d => d.Scalar).Average(), Is.EqualTo(3.0));
            Assert.That(docs.Select(d => d.Id).Contains("c"), Is.True);
            Assert.That(docs.Select(d => d.Id).Contains("z"), Is.False);
            Assert.That(docs.All(d => d.Scalar > 0), Is.True);
            Assert.That(docs.All(d => d.Scalar > 1), Is.False);

            AddDocument(new SampleDocument { Id = "d", Scalar = 3 });

            Assert.That(docs.Select(d => d.Scalar).Distinct().ToList(), Is.EquivalentTo(new[] { 5, 1, 3 }));
            Assert.That(docs.Select(d => d.Scalar).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void MinAndMaxAfterPaging_AnswerForThePage()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            // By Scalar descending: 5, 3, 1. The first two are 5 and 3.
            Assert.That(docs.OrderByDescending(d => d.Scalar).Select(d => d.Scalar).Take(2).Min(), Is.EqualTo(3));
            Assert.That(docs.OrderBy(d => d.Scalar).Select(d => d.Scalar).Take(2).Max(), Is.EqualTo(3));
            Assert.That(docs.Select(d => d.Scalar).Min(), Is.EqualTo(1));
            Assert.That(docs.Select(d => d.Scalar).Max(), Is.EqualTo(5));
        }

        [Test]
        public void ClauseAfterDistinct_KeepsTheDistinct()
        {
            Add(new SampleDocument { Id = "d", Scalar = 3 });
            Add(new SampleDocument { Id = "e", Scalar = 5 });

            var docs = provider.AsQueryable<SampleDocument>();

            // Each of these puts Distinct in a sub-query of the clause that follows it.
            Assert.That(docs.Select(d => d.Scalar).Distinct().OrderBy(x => x).ToList(), Is.EqualTo(local.Select(d => d.Scalar).Distinct().OrderBy(x => x).ToList()));
            Assert.That(docs.Select(d => d.Scalar).Distinct().OrderBy(x => x).ToList(), Is.EqualTo(new[] { 1, 3, 5 }));
            Assert.That(docs.Select(d => d.Scalar).Distinct().Where(x => x != 5).ToList(), Is.EquivalentTo(new[] { 1, 3 }));
            Assert.That(docs.Select(d => d.Scalar).Distinct().Select(x => x + 1).ToList(), Is.EquivalentTo(new[] { 2, 4, 6 }));
            Assert.That(docs.Select(d => d.Scalar).Distinct().Count(x => x != 5), Is.EqualTo(2));
            Assert.That(docs.Select(d => d.Scalar).Distinct().OrderByDescending(x => x).Skip(1).Select(x => x * 10).ToList(), Is.EqualTo(new[] { 30, 10 }));
        }

        [Test]
        public void MinAndMaxWithASelectorAfterPaging_AnswerForThePage()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            // Min(selector) is Select(selector).Min(), which puts the paging in a sub-query. Scalars: 5, 1, 3.
            Assert.That(docs.OrderByDescending(d => d.Scalar).Take(2).Min(d => d.Scalar), Is.EqualTo(local.OrderByDescending(d => d.Scalar).Take(2).Min(d => d.Scalar)));
            Assert.That(docs.OrderByDescending(d => d.Scalar).Take(2).Min(d => d.Scalar), Is.EqualTo(3));
            Assert.That(docs.OrderBy(d => d.Scalar).Take(2).Select(d => d.Scalar).Max(), Is.EqualTo(3));
            Assert.That(docs.OrderBy(d => d.Scalar).Skip(1).Max(d => d.Scalar), Is.EqualTo(5));
            Assert.That(docs.OrderByDescending(d => d.Scalar).Skip(2).Select(d => d.Scalar).Min(), Is.EqualTo(1));
            Assert.That(docs.OrderBy(d => d.Scalar).Take(2).Select(d => d.Scalar).Skip(1).Min(), Is.EqualTo(3));

            // Without paging they still run in Lucene.
            Assert.That(docs.Min(d => d.Scalar), Is.EqualTo(1));
            Assert.That(docs.Where(d => d.Scalar < 5).Max(d => d.Scalar), Is.EqualTo(3));
        }

        [Test]
        public void SelectOrPagingAfterAClauseOnAPage()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            // By Scalar: b(1), c(3), a(5).
            Assert.That(docs.OrderBy(d => d.Scalar).Take(3).Where(d => d.Scalar > 1).Take(1).Select(d => d.Id).ToList(),
                Is.EqualTo(local.OrderBy(d => d.Scalar).Take(3).Where(d => d.Scalar > 1).Take(1).Select(d => d.Id).ToList()));

            Assert.That(docs.OrderBy(d => d.Scalar).Take(3).Where(d => d.Scalar > 1).Take(1).Select(d => d.Id).ToList(), Is.EqualTo(new[] { "c" }));
            Assert.That(docs.OrderBy(d => d.Scalar).Take(2).OrderByDescending(d => d.Scalar).Skip(1).Select(d => d.Id).ToList(), Is.EqualTo(new[] { "b" }));
            Assert.That(docs.OrderBy(d => d.Scalar).Take(2).OrderByDescending(d => d.Id).Take(1).Select(d => d.Id).First(), Is.EqualTo("c"));
        }

        [Test]
        public void ContainsOverALocalCollection_AfterPaging()
        {
            var docs = provider.AsQueryable<SampleDocument>();
            var ids = new[] { "a", "c", "z" };

            // By Scalar: b, c, a; the first two are b and c.
            Assert.That(docs.OrderBy(d => d.Scalar).Take(2).Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToList(),
                Is.EqualTo(local.OrderBy(d => d.Scalar).Take(2).Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToList()));

            Assert.That(docs.OrderBy(d => d.Scalar).Take(2).Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToList(), Is.EqualTo(new[] { "c" }));
            Assert.That(docs.OrderBy(d => d.Scalar).Take(2).Count(d => ids.Contains(d.Id)), Is.EqualTo(1));
            Assert.That(docs.OrderBy(d => d.Scalar).Skip(1).Where(d => !ids.Contains(d.Id)).ToList(), Is.Empty);
            Assert.That(docs.OrderBy(d => d.Scalar).Skip(1).Where(d => ids.Any(i => i == d.Id)).Select(d => d.Id).ToList(), Is.EqualTo(new[] { "c", "a" }));

            // Ahead of the paging it runs in Lucene, as before.
            Assert.That(docs.Where(d => ids.Contains(d.Id)).OrderBy(d => d.Scalar).Take(1).Select(d => d.Id).ToList(), Is.EqualTo(new[] { "c" }));
        }

        [Test]
        public void First_AfterPagingThatLeavesNoRows()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            Assert.That(docs.OrderBy(d => d.Scalar).Take(2).Skip(3).FirstOrDefault(), Is.Null);
            Assert.That(docs.OrderBy(d => d.Scalar).Take(0).FirstOrDefault(), Is.Null);
            Assert.That(() => docs.OrderBy(d => d.Scalar).Take(2).Skip(2).First(), Throws.InvalidOperationException);
            Assert.That(docs.OrderBy(d => d.Scalar).Take(2).Skip(1).First().Id, Is.EqualTo("c"));
            Assert.That(docs.OrderBy(d => d.Scalar).First().Id, Is.EqualTo("b"));
        }

        [Test]
        public void AllAndAggregate_AfterASelect()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            Assert.That(docs.Select(d => d.Scalar).All(x => x > 0), Is.True);
            Assert.That(docs.Select(d => d.Scalar).All(x => x > 1), Is.False);
            Assert.That(docs.Select(d => new { d.Scalar, d.Id }).All(x => x.Scalar > 0 && x.Id != "z"), Is.True);
            Assert.That(docs.OrderBy(d => d.Scalar).Take(2).Select(d => d.Scalar).All(x => x < 5), Is.True);
            Assert.That(docs.Select(d => d.Scalar % 2).Distinct().All(x => x == 1), Is.True);

            Assert.That(docs.Select(d => d.Scalar).Aggregate((x, y) => x + y), Is.EqualTo(local.Select(d => d.Scalar).Aggregate((x, y) => x + y)));
            Assert.That(docs.Select(d => d.Scalar).Aggregate(10, (x, y) => x + y), Is.EqualTo(19));
            Assert.That(docs.Select(d => d.Scalar).Aggregate(10, (x, y) => x + y, x => "=" + x), Is.EqualTo("=19"));
            Assert.That(docs.OrderBy(d => d.Scalar).Select(d => d.Id).Aggregate("", (x, y) => x + y), Is.EqualTo("bca"));
            Assert.That(docs.OrderBy(d => d.Scalar).Select(d => d.Id).Aggregate((x, y) => x + y), Is.EqualTo("bca"));
            Assert.That(docs.OrderBy(d => d.Scalar).Select(d => d.Id).Skip(1).Aggregate((x, y) => x + y), Is.EqualTo("ca"));
            Assert.That(() => docs.Where(d => d.Scalar > 100).Select(d => d.Scalar).Aggregate((x, y) => x + y), Throws.InvalidOperationException);

            // On whole documents, as before.
            Assert.That(docs.Aggregate(0, (x, d) => x + d.Scalar), Is.EqualTo(9));
            Assert.That(docs.OrderBy(d => d.Scalar).Skip(1).All(d => d.Scalar > 1), Is.True);
        }

        [Test]
        public void DefaultIfEmpty_IsApplied()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            Assert.That(docs.Where(d => d.Scalar > 100).Select(d => d.Scalar).DefaultIfEmpty(7).Sum(), Is.EqualTo(7));
            Assert.That(docs.Where(d => d.Scalar > 100).Select(d => d.Scalar).DefaultIfEmpty().ToList(), Is.EqualTo(new[] { 0 }));
            Assert.That(docs.Where(d => d.Scalar > 100).Select(d => d.Scalar).DefaultIfEmpty(7).Count(), Is.EqualTo(1));
            Assert.That(docs.Where(d => d.Scalar > 100).DefaultIfEmpty().First(), Is.Null);

            // With rows it changes nothing.
            Assert.That(docs.Select(d => d.Scalar).DefaultIfEmpty(7).Sum(), Is.EqualTo(9));
            Assert.That(docs.Select(d => d.Scalar).Cast<object>().Count(), Is.EqualTo(3));
        }

        [Test]
        public void AllowsConflictingOrderOnNoSkipTake()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            var results = (from outer in
                               (from d in docs
                                 orderby d.Scalar
                                 select d)
                           orderby outer.Id
                           select new { outer.Id })
                           .ToList();

            // The outer orderby is the later one, so it decides.
            Assert.That(results.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void AllowsSameOrderOnSubSkipTake()
        {
            var docs = provider.AsQueryable<SampleDocument>();

            var results = (from outer in
                               (from d in docs
                                orderby d.Scalar
                                orderby d.Id
                                select d).Skip(1)
                           orderby outer.Scalar
                           orderby outer.Id
                           select new { outer.Id })
                           .ToList();

            // The sub-query is ordered by Id (its later orderby): a, b, c. Skip(1) leaves b, c.
            Assert.That(results.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "b", "c" }));
        }

    }
}
