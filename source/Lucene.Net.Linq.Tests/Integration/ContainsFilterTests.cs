using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Linq.Analysis;
using Lucene.Net.Linq.Mapping;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    [TestFixture]
    public class ContainsFilterTests : IntegrationTestBase
    {
        protected override Analyzer GetAnalyzer(Net.Util.LuceneVersion version)
        {
            return new PerFieldAnalyzer(new KeywordAnalyzer());
        }

        [DocumentKey(FieldName = "FixedKey", Value = "Item")]
        public class Item
        {
            [Field(Key = true)]
            public string Id { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string Category { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string Name { get; set; }
        }

        private void SeedItems()
        {
            AddDocument(new Item { Id = "1", Category = "fruit", Name = "Apple" });
            AddDocument(new Item { Id = "2", Category = "fruit", Name = "Banana" });
            AddDocument(new Item { Id = "3", Category = "veg", Name = "Carrot" });
            AddDocument(new Item { Id = "4", Category = "dairy", Name = "Milk" });
            AddDocument(new Item { Id = "5", Category = "dairy", Name = "Cheese" });
        }

        [Test]
        public void Contains_ArrayOfValues_FiltersCorrectly()
        {
            SeedItems();

            var categories = new[] { "fruit", "dairy" };
            var results = provider.AsQueryable<Item>()
                .Where(i => categories.Contains(i.Category))
                .ToList();

            Assert.That(results.Count, Is.EqualTo(4));
            Assert.That(results.All(r => r.Category == "fruit" || r.Category == "dairy"), Is.True);
        }

        [Test]
        public void Contains_ListOfValues_FiltersCorrectly()
        {
            SeedItems();

            var ids = new List<string> { "1", "3", "5" };
            var results = provider.AsQueryable<Item>()
                .Where(i => ids.Contains(i.Id))
                .ToList();

            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results.Select(r => r.Name).OrderBy(n => n).ToList(),
                Is.EqualTo(new[] { "Apple", "Carrot", "Cheese" }));
        }

        [Test]
        public void Contains_SingleValue_ReturnsSingleMatch()
        {
            SeedItems();

            var categories = new[] { "veg" };
            var results = provider.AsQueryable<Item>()
                .Where(i => categories.Contains(i.Category))
                .ToList();

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Name, Is.EqualTo("Carrot"));
        }

        [Test]
        public void Contains_EmptyCollection_ReturnsEmpty()
        {
            SeedItems();

            var empty = new string[0];
            var results = provider.AsQueryable<Item>()
                .Where(i => empty.Contains(i.Category))
                .ToList();

            Assert.That(results.Count, Is.EqualTo(0));
        }

        [Test]
        public void Contains_NoMatchingValues_ReturnsEmpty()
        {
            SeedItems();

            var categories = new[] { "meat", "grain" };
            var results = provider.AsQueryable<Item>()
                .Where(i => categories.Contains(i.Category))
                .ToList();

            Assert.That(results.Count, Is.EqualTo(0));
        }

        [Test]
        public void Contains_CombinedWithOtherPredicates()
        {
            SeedItems();

            var categories = new[] { "fruit", "dairy" };
            var results = provider.AsQueryable<Item>()
                .Where(i => categories.Contains(i.Category) && i.Name == "Apple")
                .ToList();

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Id, Is.EqualTo("1"));
        }

        [Test]
        public void Contains_WithQuerySyntax()
        {
            SeedItems();

            var categories = new[] { "fruit", "dairy" };
            var results = (
                from item in provider.AsQueryable<Item>()
                where categories.Contains(item.Category)
                select item
            ).ToList();

            Assert.That(results.Count, Is.EqualTo(4));
        }

        [Test]
        public void Contains_AllValues_ReturnsAll()
        {
            SeedItems();

            var allCategories = new[] { "fruit", "veg", "dairy" };
            var results = provider.AsQueryable<Item>()
                .Where(i => allCategories.Contains(i.Category))
                .ToList();

            Assert.That(results.Count, Is.EqualTo(5));
        }

        [Test]
        public void Contains_CapturedVariable_Works()
        {
            SeedItems();

            // Captured variable (not a direct constant)
            var filter = GetFilterCategories();
            var results = provider.AsQueryable<Item>()
                .Where(i => filter.Contains(i.Category))
                .ToList();

            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results.All(r => r.Category == "fruit"), Is.True);
        }

        private static string[] GetFilterCategories() => new[] { "fruit" };
    }
}
