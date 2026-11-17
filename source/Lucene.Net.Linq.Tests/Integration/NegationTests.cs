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

            // The three below have no ordered value in item 3: null, null and NaN.
            [NumericField]
            public int? Maybe { get; set; }

            [NumericField]
            public DateTime? When { get; set; }

            [NumericField]
            public double Real { get; set; }
        }

        private List<Item> local;

        [SetUp]
        public void SetUp()
        {
            local = new List<Item>
            {
                new Item { Id = "1", Name = "apple", Number = 1, Flag = true, Tags = new List<string> { "music", "art" }, People = new List<string> { "p-lee", "p-kim" }, Maybe = 1, When = new DateTime(2019, 1, 1), Real = 1.5 },
                new Item { Id = "2", Name = "avocado", Number = 3, Flag = false, Tags = new List<string> { "music" }, People = new List<string> { "p-kim" }, Maybe = 3, When = new DateTime(2021, 1, 1), Real = 3 },
                new Item { Id = "3", Name = "banana", Number = 5, Flag = true, Tags = new List<string> { "art" }, People = new List<string> { "p-lee" }, Maybe = null, When = null, Real = double.NaN },
                new Item { Id = "4", Name = "cherry", Number = 7, Flag = false, Tags = new List<string> { "music" }, People = new List<string>(), Maybe = 7, When = new DateTime(2023, 1, 1), Real = 7.5 },
                new Item { Id = "5", Name = "date", Number = 9, Flag = true, Tags = new List<string>(), People = new List<string>(), Maybe = 9, When = new DateTime(2025, 1, 1), Real = 9.5 },
            };

            local.ForEach(AddDocument);
        }

        private void AssertSelects(Expression<Func<Item, bool>> predicate, params string[] expectedIds)
        {
            Assert.That(local.Where(predicate.Compile()).Select(i => i.Id).ToArray(), Is.EquivalentTo(expectedIds), "LINQ to Objects");
            Assert.That(provider.AsQueryable<Item>().Where(predicate).ToList().Select(i => i.Id).ToArray(), Is.EquivalentTo(expectedIds), "Lucene");
        }

        /// <summary>
        /// For a predicate that calls <c>Query()</c>, which only Lucene can run:
        /// <paramref name="sameInObjects"/> is the same condition for LINQ to Objects.
        /// </summary>
        private void AssertSelects(Expression<Func<Item, bool>> predicate, Func<Item, bool> sameInObjects, params string[] expectedIds)
        {
            Assert.That(local.Where(sameInObjects).Select(i => i.Id).ToArray(), Is.EquivalentTo(expectedIds), "LINQ to Objects");
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
        public void NotComparisons_OnTheSameField()
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

        // ---- ranges over a value that is not ordered: missing, or NaN -------------
        // a > b and a <= b are both false for such a value, so !(a > b) selects it and (a <= b) does not.

        [Test]
        public void NotRange_NullableInt()
        {
            AssertSelects(i => !(i.Maybe > 3), "1", "2", "3");
            AssertSelects(i => !(i.Maybe >= 3), "1", "3");
            AssertSelects(i => !(i.Maybe < 7), "3", "4", "5");
            AssertSelects(i => !(i.Maybe <= 7), "3", "5");
            AssertSelects(i => !(3 < i.Maybe), "1", "2", "3");
        }

        [Test]
        public void NotRange_NullableInt_InGroups()
        {
            AssertSelects(i => i.Flag && !(i.Maybe > 3), "1", "3");
            AssertSelects(i => !(i.Maybe > 3 || i.Name == "apple"), "2", "3");
            AssertSelects(i => !(i.Maybe > 1 && i.Maybe < 9), "1", "3", "5");
            AssertSelects(i => !(i.Maybe < 3) && !(i.Maybe > 7), "2", "3", "4");
            AssertSelects(i => !(i.Maybe > 3) || i.Maybe > 7, "1", "2", "3", "5");
        }

        [Test]
        public void NotRange_NullableDateTime()
        {
            AssertSelects(i => !(i.When > new DateTime(2022, 1, 1)), "1", "2", "3");
            AssertSelects(i => !(i.When >= new DateTime(2021, 1, 1)), "1", "3");
            AssertSelects(i => !(i.When < new DateTime(2022, 1, 1)), "3", "4", "5");
            AssertSelects(i => !(i.When <= new DateTime(2023, 1, 1)), "3", "5");
        }

        [Test]
        public void NotRange_NaN()
        {
            AssertSelects(i => !(i.Real > 3), "1", "2", "3");
            AssertSelects(i => !(i.Real >= 3), "1", "3");
            AssertSelects(i => !(i.Real < 7.5), "3", "4", "5");
            AssertSelects(i => !(i.Real <= 7.5), "3", "5");
            AssertSelects(i => i.Flag && !(i.Real > 3), "1", "3");
        }

        [Test]
        public void Range_DoesNotSelectAMissingValueOrNaN()
        {
            AssertSelects(i => i.Maybe <= 3, "1", "2");
            AssertSelects(i => !!(i.Maybe <= 3), "1", "2");
            AssertSelects(i => i.When <= new DateTime(2021, 1, 1), "1", "2");
            AssertSelects(i => i.Real <= 3, "1", "2");
        }

        // ---- string.Compare --------------------------------------------------------

        [Test]
        public void NotStringCompare_NextToARangeOnTheSameField()
        {
            // The prohibited range is not a bound: Name <= "b" && Name < "c", not "b" < Name < "c".
            AssertSelects(i => !(string.Compare(i.Name, "b") > 0) && string.Compare(i.Name, "c") < 0, "1", "2");
            AssertSelects(i => string.Compare(i.Name, "b") > 0 && !(string.Compare(i.Name, "c") < 0), "4", "5");
        }

        [Test]
        public void NotGroupOfStringCompares()
        {
            // Name > "c" && Name < "avocado": nothing.
            AssertSelects(i => !(string.Compare(i.Name, "c") <= 0 || string.Compare(i.Name, "avocado") >= 0));
            AssertSelects(i => !(string.Compare(i.Name, "b") < 0 || string.Compare(i.Name, "d") > 0), "3", "4");
            AssertSelects(i => !(string.Compare(i.Name, "b") > 0 && string.Compare(i.Name, "d") < 0), "1", "2", "5");
        }

        // ---- Query() --------------------------------------------------------------

        [Test]
        public void NotQuery()
        {
            AssertSelects(i => !i.Name.Query("apple"), i => i.Name != "apple", "2", "3", "4", "5");
            AssertSelects(i => !i.Name.Query("apple cherry"), i => i.Name != "apple" && i.Name != "cherry", "2", "3", "5");
            AssertSelects(i => i.Flag && !i.Name.Query("apple cherry"), i => i.Flag && i.Name != "apple" && i.Name != "cherry", "3", "5");
        }

        [Test]
        public void NotQuery_OfOnlyExclusions()
        {
            // Query("-apple") is everything except apple, so its negation is apple.
            AssertSelects(i => !i.Name.Query("-apple"), i => i.Name == "apple", "1");
            AssertSelects(i => !i.Name.Query("-apple -cherry"), i => i.Name == "apple" || i.Name == "cherry", "1", "4");
            AssertSelects(i => i.Flag && !i.Name.Query("-apple -cherry"), i => i.Flag && (i.Name == "apple" || i.Name == "cherry"), "1");
            AssertSelects(i => i.Name == "date" || !i.Name.Query("-apple -cherry"), i => i.Name == "date" || i.Name == "apple" || i.Name == "cherry", "1", "4", "5");
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

        [Test]
        public void NotStringCompare()
        {
            AssertSelects(i => !(string.Compare(i.Name, "b") > 0), "1", "2");
            AssertSelects(i => i.Flag && !(string.Compare(i.Name, "b") > 0), "1");
        }
    }
}
