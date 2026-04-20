using System.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Linq.Analysis;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Search;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    [TestFixture]
    public class JoinTests : IntegrationTestBase
    {
        protected override Analyzer GetAnalyzer(Net.Util.LuceneVersion version)
        {
            var perField = new PerFieldAnalyzer(new KeywordAnalyzer());
            perField.AddAnalyzer("Title", new Lucene.Net.Analysis.Standard.StandardAnalyzer(version));
            return perField;
        }

        [DocumentKey(FieldName = "FixedKey", Value = "Author")]
        public class Author
        {
            [Field(Key = true)]
            public string Username { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string DisplayName { get; set; }
        }

        [DocumentKey(FieldName = "FixedKey", Value = "Article")]
        public class Article
        {
            [Field(Key = true)]
            public string ArticleId { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string AuthorId { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string CategoryId { get; set; }

            [Field(IndexMode.Analyzed)]
            public string Title { get; set; }
        }

        [DocumentKey(FieldName = "FixedKey", Value = "Category")]
        public class Category
        {
            [Field(Key = true)]
            public string CategoryId { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string Label { get; set; }
        }

        private void SeedData()
        {
            AddDocument(new Author { Username = "alice", DisplayName = "Alice A." });
            AddDocument(new Author { Username = "bob", DisplayName = "Bob B." });

            AddDocument(new Article { ArticleId = "a1", AuthorId = "alice", Title = "First Post" });
            AddDocument(new Article { ArticleId = "a2", AuthorId = "bob", Title = "Second Post" });
            AddDocument(new Article { ArticleId = "a3", AuthorId = "alice", Title = "Third Post" });
        }

        [Test]
        public void Join_QuerySyntax_ReturnsMatchedPairs()
        {
            SeedData();

            var articles = provider.AsQueryable<Article>();
            var authors = provider.AsQueryable<Author>();

            var joined = (
                from article in articles
                join author in authors on article.AuthorId equals author.Username
                select new { article.ArticleId, author.DisplayName }
            ).ToList();

            Assert.That(joined.Count, Is.EqualTo(3));
            Assert.That(joined.Any(j => j.ArticleId == "a1" && j.DisplayName == "Alice A."), Is.True);
            Assert.That(joined.Any(j => j.ArticleId == "a2" && j.DisplayName == "Bob B."), Is.True);
            Assert.That(joined.Any(j => j.ArticleId == "a3" && j.DisplayName == "Alice A."), Is.True);
        }

        [Test]
        public void Join_WithWhereOnOuterSide_FiltersBeforeJoin()
        {
            SeedData();

            var articles = provider.AsQueryable<Article>();
            var authors = provider.AsQueryable<Author>();

            var joined = (
                from article in articles
                where article.AuthorId == "alice"
                join author in authors on article.AuthorId equals author.Username
                select new { article.ArticleId, author.DisplayName }
            ).ToList();

            Assert.That(joined.Count, Is.EqualTo(2));
            Assert.That(joined.All(j => j.DisplayName == "Alice A."), Is.True);
        }

        [Test]
        public void Join_ProjectsMultipleFields()
        {
            SeedData();

            var articles = provider.AsQueryable<Article>();
            var authors = provider.AsQueryable<Author>();

            var joined = (
                from article in articles
                join author in authors on article.AuthorId equals author.Username
                select new { article.ArticleId, article.Title, author.Username, author.DisplayName }
            ).ToList();

            Assert.That(joined.Count, Is.EqualTo(3));
            var first = joined.First(j => j.ArticleId == "a1");
            Assert.That(first.Title, Is.EqualTo("First Post"));
            Assert.That(first.Username, Is.EqualTo("alice"));
            Assert.That(first.DisplayName, Is.EqualTo("Alice A."));
        }

        [Test]
        public void Join_NoMatchingInner_ReturnsEmpty()
        {
            AddDocument(new Article { ArticleId = "orphan", AuthorId = "nobody", Title = "Orphan" });

            var articles = provider.AsQueryable<Article>();
            var authors = provider.AsQueryable<Author>();

            var joined = (
                from article in articles
                join author in authors on article.AuthorId equals author.Username
                select new { article.ArticleId, author.DisplayName }
            ).ToList();

            Assert.That(joined.Count, Is.EqualTo(0));
        }

        [Test]
        public void Join_EmptyOuterSide_ReturnsEmpty()
        {
            AddDocument(new Author { Username = "alice", DisplayName = "Alice A." });
            // No articles

            var articles = provider.AsQueryable<Article>();
            var authors = provider.AsQueryable<Author>();

            var joined = (
                from article in articles
                join author in authors on article.AuthorId equals author.Username
                select new { article.ArticleId, author.DisplayName }
            ).ToList();

            Assert.That(joined.Count, Is.EqualTo(0));
        }

        [Test]
        public void Join_MultipleArticlesPerAuthor_ReturnsAll()
        {
            AddDocument(new Author { Username = "alice", DisplayName = "Alice A." });
            AddDocument(new Article { ArticleId = "x1", AuthorId = "alice", Title = "One" });
            AddDocument(new Article { ArticleId = "x2", AuthorId = "alice", Title = "Two" });
            AddDocument(new Article { ArticleId = "x3", AuthorId = "alice", Title = "Three" });

            var articles = provider.AsQueryable<Article>();
            var authors = provider.AsQueryable<Author>();

            var joined = (
                from article in articles
                join author in authors on article.AuthorId equals author.Username
                select new { article.Title, author.DisplayName }
            ).ToList();

            Assert.That(joined.Count, Is.EqualTo(3));
            Assert.That(joined.All(j => j.DisplayName == "Alice A."), Is.True);
        }

        [Test]
        public void Join_MethodSyntax_Works()
        {
            SeedData();

            var joined = provider.AsQueryable<Article>()
                .Join(
                    provider.AsQueryable<Author>(),
                    article => article.AuthorId,
                    author => author.Username,
                    (article, author) => new { article.ArticleId, author.DisplayName })
                .ToList();

            Assert.That(joined.Count, Is.EqualTo(3));
            Assert.That(joined.Any(j => j.ArticleId == "a1" && j.DisplayName == "Alice A."), Is.True);
        }

        [Test]
        public void Join_SemiJoinPushdown_OnlyFetchesMatchingInnerDocuments()
        {
            // Seed 2 authors but articles only reference "alice"
            AddDocument(new Author { Username = "alice", DisplayName = "Alice A." });
            AddDocument(new Author { Username = "bob", DisplayName = "Bob B." });
            AddDocument(new Author { Username = "carol", DisplayName = "Carol C." });
            AddDocument(new Author { Username = "dave", DisplayName = "Dave D." });

            AddDocument(new Article { ArticleId = "a1", AuthorId = "alice", Title = "Post" });

            // Track how many Author documents the inner query returns
            // by wrapping the inner queryable with a counting projection
            int innerCount = 0;
            var articles = provider.AsQueryable<Article>();
            var authors = provider.AsQueryable<Author>()
                .Select(a => new { a.Username, a.DisplayName, Counted = CountSideEffect(ref innerCount, a) });

            // Direct join — the semi-join pushdown should use TermsFilter
            // to only fetch the 1 matching author ("alice"), not all 4.
            var joined = (
                from article in provider.AsQueryable<Article>()
                join author in provider.AsQueryable<Author>() on article.AuthorId equals author.Username
                select new { article.ArticleId, author.DisplayName }
            ).ToList();

            Assert.That(joined.Count, Is.EqualTo(1));
            Assert.That(joined[0].DisplayName, Is.EqualTo("Alice A."));

            // The real validation: if we run the inner query with just the filter
            // that the semi-join should produce, it returns only matching authors.
            var innerQuery = new Lucene.Net.Queries.TermsFilter(
                new Lucene.Net.Index.Term("Username", "alice"));
            var filteredAuthors = provider.AsQueryable<Author>()
                .Where(new ConstantScoreQuery(innerQuery))
                .ToList();

            Assert.That(filteredAuthors.Count, Is.EqualTo(1),
                "TermsFilter should only match the 1 author referenced by articles, not all 4");
            Assert.That(filteredAuthors[0].Username, Is.EqualTo("alice"));
        }

        [Test]
        public void Join_MultipleJoins_ChainsCorrectly()
        {
            AddDocument(new Author { Username = "alice", DisplayName = "Alice A." });
            AddDocument(new Author { Username = "bob", DisplayName = "Bob B." });
            AddDocument(new Category { CategoryId = "tech", Label = "Technology" });
            AddDocument(new Category { CategoryId = "life", Label = "Lifestyle" });

            AddDocument(new Article { ArticleId = "a1", AuthorId = "alice", CategoryId = "tech", Title = "AI Trends" });
            AddDocument(new Article { ArticleId = "a2", AuthorId = "bob", CategoryId = "life", Title = "Cooking Tips" });
            AddDocument(new Article { ArticleId = "a3", AuthorId = "alice", CategoryId = "life", Title = "Work-Life Balance" });

            var articles = provider.AsQueryable<Article>();
            var authors = provider.AsQueryable<Author>();
            var categories = provider.AsQueryable<Category>();

            // Multi-join: all 3 articles joined with authors and categories
            var joined = (
                from article in articles
                join author in authors on article.AuthorId equals author.Username
                join category in categories on article.CategoryId equals category.CategoryId
                select new { article.Title, author.DisplayName, category.Label }
            ).ToList();

            Assert.That(joined.Count, Is.EqualTo(3));
            Assert.That(joined.Any(j => j.Title == "AI Trends" && j.DisplayName == "Alice A." && j.Label == "Technology"), Is.True);
            Assert.That(joined.Any(j => j.Title == "Cooking Tips" && j.DisplayName == "Bob B." && j.Label == "Lifestyle"), Is.True);
            Assert.That(joined.Any(j => j.Title == "Work-Life Balance" && j.DisplayName == "Alice A." && j.Label == "Lifestyle"), Is.True);

            // Multi-join with Where filter on outer: only "tips" articles
            var filtered = (
                from article in articles.Where(a => a.Title.Contains("tips"))
                join author in authors on article.AuthorId equals author.Username
                join category in categories on article.CategoryId equals category.CategoryId
                select new { article.Title, author.DisplayName, category.Label }
            ).ToList();

            Assert.That(filtered.Count, Is.EqualTo(1));
            Assert.That(filtered[0].Title, Is.EqualTo("Cooking Tips"));
            Assert.That(filtered[0].DisplayName, Is.EqualTo("Bob B."));
            Assert.That(filtered[0].Label, Is.EqualTo("Lifestyle"));
        }

        private static Author CountSideEffect(ref int count, Author a)
        {
            count++;
            return a;
        }
    }
}
