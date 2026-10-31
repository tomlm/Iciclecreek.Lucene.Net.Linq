using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Lucene.Net.Linq.Fluent;
using Lucene.Net.Util;

namespace Lucene.Net.Linq.Tests.Integration
{
    [TestFixture]
    public class EnumerableContainsTests : IntegrationTestBase
    {
        private ClassMap<Tour> tourMap;

        public class Tour
        {
            public IEnumerable<int> AccommodationAges { get; set; }
            public decimal TotalPriceMin { get; set; }
            public decimal TotalPriceMax { get; set; }
        }

        public class Note
        {
            public string Id { get; set; }
            public string[] Links { get; set; }
            public List<string> Tags { get; set; }
        }

        private ClassMap<Note> NoteMap()
        {
            var map = new ClassMap<Note>(LuceneVersion.LUCENE_48);
            map.Key(p => p.Id);
            map.Property(p => p.Links).Stored().NotAnalyzed();
            map.Property(p => p.Tags).Stored().NotAnalyzed();
            return map;
        }

        private void AddNotes(ClassMap<Note> map)
        {
            using (var session = provider.OpenSession(map.ToDocumentMapper()))
            {
                session.Add(new Note { Id = "1", Links = new[] { "A", "B" }, Tags = new List<string> { "family" } });
                session.Add(new Note { Id = "2", Links = new[] { "B" }, Tags = new List<string> { "work", "family" } });
                session.Add(new Note { Id = "3", Links = new string[0], Tags = new List<string>() });
            }
        }

        // With C# 14 on .NET 10, array.Contains(x) in an expression binds to
        // MemoryExtensions.Contains(ReadOnlySpan<T>, T) through an implicit span conversion.
        [Test]
        public void ContainsString_OnStringArrayField()
        {
            var map = NoteMap();
            AddNotes(map);

            using (var session = provider.OpenSession(map.ToDocumentMapper()))
            {
                var result = session.Query().Where(n => n.Links.Contains("A")).ToList();
                Assert.That(result.Select(n => n.Id), Is.EqualTo(new[] { "1" }));
            }
        }

        [Test]
        public void ContainsString_OnStringArrayField_ViaEnumerable()
        {
            var map = NoteMap();
            AddNotes(map);

            using (var session = provider.OpenSession(map.ToDocumentMapper()))
            {
                var result = session.Query().Where(n => Enumerable.Contains(n.Links, "B")).OrderBy(n => n.Id).ToList();
                Assert.That(result.Select(n => n.Id), Is.EqualTo(new[] { "1", "2" }));
            }
        }

        [Test]
        public void ContainsString_OnStringListField()
        {
            var map = NoteMap();
            AddNotes(map);

            using (var session = provider.OpenSession(map.ToDocumentMapper()))
            {
                var result = session.Query().Where(n => n.Tags.Contains("work")).ToList();
                Assert.That(result.Select(n => n.Id), Is.EqualTo(new[] { "2" }));
            }
        }

        public static bool Unsupported(string value) => true;

        [Test]
        public void UntranslatablePredicate_Throws()
        {
            var map = NoteMap();
            AddNotes(map);

            using (var session = provider.OpenSession(map.ToDocumentMapper()))
            {
                // Silently ignoring the call would return every document.
                Assert.Throws<System.NotSupportedException>(() => session.Query().Where(n => Unsupported(n.Id)).ToList());
            }
        }

        [Test]
        public void In_ArrayOfValuesContainsField_ViaSpan()
        {
            var map = NoteMap();
            AddNotes(map);
            var ids = new[] { "1", "3" };

            using (var session = provider.OpenSession(map.ToDocumentMapper()))
            {
                var result = session.Query().Where(n => ids.Contains(n.Id)).OrderBy(n => n.Id).ToList();
                Assert.That(result.Select(n => n.Id), Is.EqualTo(new[] { "1", "3" }));
            }
        }

        [SetUp]
        public void SetUp()
        {
            tourMap = new ClassMap<Tour>(LuceneVersion.LUCENE_48);
            tourMap.Property(p => p.AccommodationAges).Stored().NotAnalyzed();
            tourMap.Property(p => p.TotalPriceMin).Stored().NotAnalyzed();
            tourMap.Property(p => p.TotalPriceMax).Stored().NotAnalyzed();
        }

        [Test]
        public void ContainsInt()
        {
            using (var session = provider.OpenSession(tourMap.ToDocumentMapper()))
            {
                session.Add(new Tour { AccommodationAges = new[] {10, 12}, TotalPriceMax = 100 });
                session.Add(new Tour { AccommodationAges = new[] {5, 12}, TotalPriceMax = 90 });
            }

            using (var session = provider.OpenSession(tourMap.ToDocumentMapper()))
            {
                var q = session.Query().Where(t => t.AccommodationAges.Contains(10));
                var result = q.ToList();
                Assert.That(result.Count, Is.EqualTo(1), "Expected single result");
                Assert.That(result.Single().TotalPriceMax, Is.EqualTo(100));
            }
        }
    }
}
