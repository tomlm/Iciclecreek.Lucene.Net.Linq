using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Linq.Analysis;
using Lucene.Net.Linq.Mapping;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    /// <summary>
    /// <c>!</c> applied to each kind of condition. Every query is also run with
    /// LINQ to Objects over the same items, and both must select the expected ids.
    /// </summary>
    [TestFixture]
    public class NegationTests : IntegrationTestBase
    {
        protected override Analyzer GetAnalyzer(Net.Util.LuceneVersion version)
        {
            return new PerFieldAnalyzer(new KeywordAnalyzer());
        }

        [DocumentKey(FieldName = "FixedKey", Value = "Item")]
        public class Item
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string Id { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string Name { get; set; }

            [NumericField]
            public int Number { get; set; }

            public bool Flag { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public List<string> Tags { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public List<string> People { get; set; }
        }

        private List<Item> local;

        [SetUp]
        public void SetUp()
        {
            local = new List<Item>
            {
                new Item { Id = "1", Name = "apple", Number = 1, Flag = true, Tags = new List<string> { "music", "art" }, People = new List<string> { "p-lee", "p-kim" } },
                new Item { Id = "2", Name = "avocado", Number = 3, Flag = false, Tags = new List<string> { "music" }, People = new List<string> { "p-kim" } },
                new Item { Id = "3", Name = "banana", Number = 5, Flag = true, Tags = new List<string> { "art" }, People = new List<string> { "p-lee" } },
                new Item { Id = "4", Name = "cherry", Number = 7, Flag = false, Tags = new List<string> { "music" }, People = new List<string>() },
                new Item { Id = "5", Name = "date", Number = 9, Flag = true, Tags = new List<string>(), People = new List<string>() },
            };

            local.ForEach(AddDocument);
        }

        private void AssertSelects(Expression<Func<Item, bool>> predicate, params string[] expectedIds)
        {
            Assert.That(local.Where(predicate.Compile()).Select(i => i.Id).ToArray(), Is.EquivalentTo(expectedIds), "LINQ to Objects");
            Assert.That(provider.AsQueryable<Item>().Where(predicate).ToList().Select(i => i.Id).ToArray(), Is.EquivalentTo(expectedIds), "Lucene");
        }

        // ---- Contains on a collection field (issue #15) ---------------------------

        [Test]
        public void NotCollectionContains_WithPositiveClause()
        {
            AssertSelects(i => i.Tags.Contains("music") && !i.People.Contains("p-lee"), "2", "4");
        }

        [Test]
        public void NotCollectionContains_Alone()
        {
            AssertSelects(i => !i.People.Contains("p-lee"), "2", "4", "5");
        }

        [Test]
        public void NotCollectionContains_Twice()
        {
            AssertSelects(i => !i.People.Contains("p-lee") && !i.Tags.Contains("music"), "5");
        }

        [Test]
        public void NotCollectionContains_OrPositiveClause()
        {
            AssertSelects(i => !i.People.Contains("p-lee") || i.Name == "banana", "2", "3", "4", "5");
        }

        [Test]
        public void NotLocalCollectionContainsField()
        {
            var names = new List<string> { "apple", "date" };

            AssertSelects(i => !names.Contains(i.Name), "2", "3", "4");
            AssertSelects(i => i.Flag && !names.Contains(i.Name), "3");
        }

        // ---- comparisons ----------------------------------------------------------

        [Test]
        public void NotEqual()
        {
            AssertSelects(i => !(i.Name == "apple"), "2", "3", "4", "5");
            AssertSelects(i => i.Flag && !(i.Name == "apple"), "3", "5");
        }

        [Test]
        public void NotNotEqual()
        {
            AssertSelects(i => !(i.Name != "apple"), "1");
        }

        [Test]
        public void NotGreaterThan()
        {
            AssertSelects(i => !(i.Number > 3), "1", "2");
        }

        [Test]
        public void NotGreaterThanOrEqual()
        {
            AssertSelects(i => !(i.Number >= 3), "1");
        }

        [Test]
        public void NotLessThan()
        {
            AssertSelects(i => !(i.Number < 7), "4", "5");
        }

        [Test]
        public void NotLessThanOrEqual()
        {
            AssertSelects(i => !(i.Number <= 7), "5");
        }

        [Test]
        public void NotComparison_FieldOnTheRight()
        {
            AssertSelects(i => !(3 < i.Number), "1", "2");
            AssertSelects(i => !("apple" == i.Name), "2", "3", "4", "5");
        }

        [Test]
        public void NotComparisons_MergedIntoARange()
        {
            AssertSelects(i => !(i.Number < 3) && !(i.Number > 7), "2", "3", "4");
            AssertSelects(i => !(i.Number < 3) && i.Number < 9, "2", "3", "4");
            AssertSelects(i => !(i.Number > 3) || i.Number > 7, "1", "2", "5");
            AssertSelects(i => !(i.Number > 3 && i.Number < 9), "1", "2", "5");
        }

        [Test]
        public void NotFlagComparedWithConstant()
        {
            AssertSelects(i => !(i.Flag == true), "2", "4");
            AssertSelects(i => !(i.Flag != true), "1", "3", "5");
        }

        // ---- groups ---------------------------------------------------------------

        [Test]
        public void NotAnd()
        {
            AssertSelects(i => !(i.Name == "apple" && i.Number == 1), "2", "3", "4", "5");
            AssertSelects(i => !(i.Tags.Contains("music") && i.People.Contains("p-kim")), "3", "4", "5");
            AssertSelects(i => !(i.Flag && i.Number > 3), "1", "2", "4");
        }

        [Test]
        public void NotOr()
        {
            AssertSelects(i => !(i.Name == "apple" || i.Number == 5), "2", "4", "5");
            AssertSelects(i => !(i.Tags.Contains("art") || i.People.Contains("p-kim")), "4", "5");
            AssertSelects(i => !(i.Flag || i.Number > 3), "2");
        }

        [Test]
        public void NotAnd_WithPositiveClause()
        {
            AssertSelects(i => i.Tags.Contains("music") && !(i.Flag && i.Number == 1), "2", "4");
            AssertSelects(i => i.Tags.Contains("art") || !(i.Flag && i.Number > 3), "1", "2", "3", "4");
        }

        [Test]
        public void NotOr_WithPositiveClause()
        {
            AssertSelects(i => i.Tags.Contains("music") && !(i.Name == "apple" || i.Name == "cherry"), "2");
            AssertSelects(i => i.Name == "apple" || !(i.Tags.Contains("music") || i.Tags.Contains("art")), "1", "5");
        }

        [Test]
        public void NotGroup_WithMethodCallAndFlag()
        {
            AssertSelects(i => !(i.Name.StartsWith("a") && i.Flag), "2", "3", "4", "5");
            AssertSelects(i => !(i.Name.StartsWith("a") || i.Flag), "4");
        }

        [Test]
        public void NotNestedGroups()
        {
            AssertSelects(i => !(i.Name == "apple" || (i.Flag && !(i.Number < 9))), "2", "3", "4");
            AssertSelects(i => !(!(i.Name == "apple") && !i.Flag), "1", "3", "5");
        }

        // ---- double negation ------------------------------------------------------

        [Test]
        public void DoubleNegation()
        {
            AssertSelects(i => !!(i.Name == "apple"), "1");
            AssertSelects(i => !!(i.Number > 3), "3", "4", "5");
            AssertSelects(i => !!i.People.Contains("p-lee"), "1", "3");
            AssertSelects(i => !!(i.Name == "apple" || i.Number == 5), "1", "3");
            AssertSelects(i => !(!(i.Name == "apple" && i.Flag)), "1");
            AssertSelects(i => !!!(i.Name == "apple"), "2", "3", "4", "5");
            AssertSelects(i => !!i.Flag, "1", "3", "5");
            AssertSelects(i => !!i.Name.StartsWith("a"), "1", "2");
        }

        // ---- behaviours that already held ----------------------------------------

        [Test]
        public void NotFlag()
        {
            AssertSelects(i => !i.Flag, "2", "4");
            AssertSelects(i => i.Tags.Contains("music") && !i.Flag, "2", "4");
        }

        [Test]
        public void NotMethodCall()
        {
            AssertSelects(i => !i.Name.StartsWith("a"), "3", "4", "5");
            AssertSelects(i => i.Flag && !i.Name.StartsWith("a"), "3", "5");
        }

        [Test]
        public void NotEqualOperator()
        {
            AssertSelects(i => i.Name != "apple", "2", "3", "4", "5");
            AssertSelects(i => i.Flag && i.Name != "apple", "3", "5");
        }

        [Test]
        public void NotEqualOperators_InParentheses()
        {
            AssertSelects(i => i.Flag && (i.Name != "apple" && i.Name != "date"), "3");
            AssertSelects(i => i.Name == "apple" || (i.Name != "banana" && i.Name != "date"), "1", "2", "4");
        }
    }
}
