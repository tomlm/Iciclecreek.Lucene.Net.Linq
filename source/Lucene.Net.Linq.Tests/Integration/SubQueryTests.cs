using System.Linq;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    [TestFixture]
    public class SubQueryTests : IntegrationTestBase
    {
        [SetUp]
        public void SetUp()
        {
            AddDocument(new SampleDocument { Id = "a", Scalar = 5});
            AddDocument(new SampleDocument { Id = "b", Scalar = 1});
            AddDocument(new SampleDocument { Id = "c", Scalar = 3});
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
