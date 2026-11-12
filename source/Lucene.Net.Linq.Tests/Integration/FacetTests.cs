using System;
using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Documents;
using Lucene.Net.Facet;
using Lucene.Net.Index;
using Lucene.Net.Linq.Fluent;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Store;
using Lucene.Net.Util;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    public class Passage
    {
        [Field(IndexMode.NotAnalyzed, Key = true)]
        public string Id { get; set; }

        [Field(IndexMode.NotAnalyzed, Facet = true)]
        public string Pile { get; set; }

        [Field(IndexMode.NotAnalyzed, Facet = true)]
        public string[] Topics { get; set; }

        [Field(IndexMode.NotAnalyzed, Facet = true)]
        public List<string> People { get; set; }

        // Plain (string-indexed) DateTime: ranges use the hidden ticks column.
        [Field(Facet = true)]
        public DateTime Date { get; set; }

        [NumericField(Facet = true)]
        public DateTime Published { get; set; }

        [NumericField(Facet = true)]
        public int Pages { get; set; }

        [NumericField(Facet = true)]
        public double Score { get; set; }

        [NumericField(Facet = true)]
        public float Weight { get; set; }

        [Field(IndexMode.NotAnalyzed)]
        public string NotFaceted { get; set; }
    }

    public class FacetEvent
    {
        [Field(IndexMode.NotAnalyzed, Key = true)]
        public string Id { get; set; }

        [NumericField(Facet = true)]
        public DateTimeOffset When { get; set; }

        [NumericField(Facet = true)]
        public long? Size { get; set; }

        // Both options: the own-name column for sorting and the hidden range column.
        [NumericField(Facet = true, DocValues = true)]
        public int Rank { get; set; }
    }

    [TestFixture]
    public class FacetTests
    {
        private static readonly LuceneVersion version = LuceneVersion.LUCENE_48;

        private Directory directory;
        private IndexWriter writer;
        private LuceneDataProvider provider;

        private static DateTime Utc(int year, int month, int day) => new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp()
        {
            directory = new RAMDirectory();
            writer = new IndexWriter(directory, new IndexWriterConfig(version, new Lucene.Net.Analysis.Core.KeywordAnalyzer()));
            provider = new LuceneDataProvider(directory, version, writer);
        }

        [TearDown]
        public void TearDown()
        {
            provider.Dispose();
            writer.Dispose();
            directory.Dispose();
        }

        private void AddSamples()
        {
            Add(new Passage { Id = "1", Pile = "news", Topics = new[] { "berlin", "history" }, People = new List<string> { "Ann", "Bob" },
                Date = Utc(2026, 1, 15), Published = Utc(2026, 1, 15), Pages = 10, Score = 0.5, Weight = 0.5f });
            Add(new Passage { Id = "2", Pile = "blog", Topics = new[] { "berlin", "music" }, People = new List<string> { "Ann" },
                Date = Utc(2026, 2, 10), Published = Utc(2026, 2, 10), Pages = 20, Score = 1.5, Weight = 1.5f });
            Add(new Passage { Id = "3", Pile = "news", Topics = new[] { "paris" }, People = new List<string>(),
                Date = Utc(2026, 2, 20), Published = Utc(2026, 2, 20), Pages = 30, Score = 2.5, Weight = 2.5f });
            Add(new Passage { Id = "4", Pile = "news", Topics = null, People = null,
                Date = Utc(2026, 3, 1), Published = Utc(2026, 3, 1), Pages = 40, Score = 2.5, Weight = 2.5f });
        }

        private void Add<T>(params T[] items) where T : new()
        {
            using (var session = provider.OpenSession<T>())
            {
                foreach (var item in items) session.Add(item);
                session.Commit();
            }
        }

        private static string[] Pairs(IEnumerable<FacetValue> values) => values.Select(v => v.Label + "=" + v.Count).ToArray();

        private IQueryable<Passage> Passages => provider.AsQueryable<Passage>();

        [Test]
        public void SingleValued_CountsPerValue_OrderedByCount()
        {
            AddSamples();

            var results = Passages.Facets(out var facets, f => f.Count(p => p.Pile)).ToList();

            Assert.That(results.Count, Is.EqualTo(4));
            Assert.That(Pairs(facets.For(p => p.Pile)), Is.EqualTo(new[] { "news=3", "blog=1" }));
        }

        [Test]
        public void MultiValued_ArrayAndList()
        {
            AddSamples();

            Passages.Facets(out var facets, f => f.Count(p => p.Topics).Count(p => p.People)).ToList();

            Assert.That(Pairs(facets.For(p => p.Topics)), Is.EqualTo(new[] { "berlin=2", "history=1", "music=1", "paris=1" }));
            Assert.That(Pairs(facets.For(p => p.People)), Is.EqualTo(new[] { "Ann=2", "Bob=1" }));
        }

        [Test]
        public void CountsFollowTheQuery_NotSkipTake()
        {
            AddSamples();

            var page = Passages
                .Where(p => p.Pile == "news")
                .Facets(out var facets, f => f.Count(p => p.Topics))
                .Skip(1)
                .Take(1)
                .ToList();

            Assert.That(page.Count, Is.EqualTo(1));
            Assert.That(Pairs(facets.For(p => p.Topics)), Is.EqualTo(new[] { "berlin=1", "history=1", "paris=1" }));
        }

        [Test]
        public void Top_LimitsValues()
        {
            AddSamples();

            Passages.Facets(out var facets, f => f.Count(p => p.Topics, top: 1)).ToList();

            Assert.That(Pairs(facets.For(p => p.Topics)), Is.EqualTo(new[] { "berlin=2" }));
        }

        [Test]
        public void Ranges_StringDateTime_UsesHiddenTicksColumn()
        {
            AddSamples();

            Passages.Facets(out var facets, f => f.Ranges(p => p.Date, FacetRange.Monthly(Utc(2026, 1, 1), Utc(2026, 3, 31)))).ToList();

            Assert.That(Pairs(facets.For(p => p.Date)), Is.EqualTo(new[] { "2026-01=1", "2026-02=2", "2026-03=1" }));
        }

        [Test]
        public void Ranges_NumericDateTime()
        {
            AddSamples();

            Passages.Where(p => p.Pile == "news")
                .Facets(out var facets, f => f.Ranges(p => p.Published, FacetRange.Monthly(Utc(2026, 1, 1), Utc(2026, 3, 31))))
                .ToList();

            Assert.That(Pairs(facets.For(p => p.Published)), Is.EqualTo(new[] { "2026-01=1", "2026-02=1", "2026-03=1" }));
        }

        [Test]
        public void Ranges_IntDoubleAndFloat_WithOpenBounds()
        {
            AddSamples();

            Passages.Facets(out var facets, f => f
                .Ranges(p => p.Pages, new[]
                {
                    new FacetRange("< 25", null, 25),
                    new FacetRange("25+", 25, null),
                    new FacetRange("10..20", 10, 20, maxInclusive: true),
                })
                .Ranges(p => p.Score, new[]
                {
                    new FacetRange("low", 0.0, 1.0),
                    new FacetRange("high", 1.0, null),
                })
                .Ranges(p => p.Weight, new[]
                {
                    new FacetRange("low", 0f, 1f),
                    new FacetRange("high", 1f, null),
                })).ToList();

            Assert.That(Pairs(facets.For(p => p.Pages)), Is.EqualTo(new[] { "< 25=2", "25+=2", "10..20=2" }));
            Assert.That(Pairs(facets.For(p => p.Score)), Is.EqualTo(new[] { "low=1", "high=3" }));
            Assert.That(Pairs(facets.For(p => p.Weight)), Is.EqualTo(new[] { "low=1", "high=3" }));
        }

        [Test]
        public void EmptyResult_ReturnsEmptyCountsAndZeroRanges()
        {
            AddSamples();

            var results = Passages
                .Where(p => p.Pile == "nothing-matches")
                .Facets(out var facets, f => f
                    .Count(p => p.Topics)
                    .Ranges(p => p.Pages, new[] { new FacetRange("all", null, null) }))
                .ToList();

            Assert.That(results, Is.Empty);
            Assert.That(facets.For(p => p.Topics), Is.Empty);
            Assert.That(Pairs(facets.For(p => p.Pages)), Is.EqualTo(new[] { "all=0" }));
        }

        [Test]
        public void EmptyIndex_ReturnsEmptyCounts()
        {
            // Lay down an empty commit; opening a never-committed directory while
            // this writer holds the lock is an existing limitation unrelated to facets.
            writer.Commit();

            var results = Passages.Facets(out var facets, f => f.Count(p => p.Topics)).ToList();

            Assert.That(results, Is.Empty);
            Assert.That(facets.For(p => p.Topics), Is.Empty);
        }

        [Test]
        public void TakeZero_StillCounts()
        {
            AddSamples();

            var results = Passages.Facets(out var facets, f => f.Count(p => p.Pile)).Take(0).ToList();

            Assert.That(results, Is.Empty);
            Assert.That(Pairs(facets.For(p => p.Pile)), Is.EqualTo(new[] { "news=3", "blog=1" }));
        }

        [Test]
        public void ScalarCount_FillsFacets()
        {
            AddSamples();

            var count = Passages.Where(p => p.Pile == "news").Facets(out var facets, f => f.Count(p => p.Topics)).Count();

            Assert.That(count, Is.EqualTo(3));
            Assert.That(Pairs(facets.For(p => p.Topics)), Is.EqualTo(new[] { "berlin=1", "history=1", "paris=1" }));
        }

        [Test]
        public void AfterSearcherReload_CountsReflectNewDocuments()
        {
            AddSamples();

            Passages.Facets(out var before, f => f.Count(p => p.Pile)).ToList();
            Assert.That(Pairs(before.For(p => p.Pile)), Is.EqualTo(new[] { "news=3", "blog=1" }));

            // Commit reloads the searcher; "forum" is a value the old reader state never saw.
            Add(new Passage { Id = "5", Pile = "forum", Date = Utc(2026, 1, 1), Published = Utc(2026, 1, 1) });
            Add(new Passage { Id = "6", Pile = "forum", Date = Utc(2026, 1, 1), Published = Utc(2026, 1, 1) });

            Passages.Facets(out var after, f => f.Count(p => p.Pile)).ToList();
            Assert.That(Pairs(after.For(p => p.Pile)), Is.EqualTo(new[] { "news=3", "forum=2", "blog=1" }));
        }

        [Test]
        public void ReexecutingQuery_ReplacesCounts()
        {
            AddSamples();

            var query = Passages.Facets(out var facets, f => f.Count(p => p.Pile));

            query.Where(p => p.Pile == "blog").ToList();
            Assert.That(Pairs(facets.For(p => p.Pile)), Is.EqualTo(new[] { "blog=1" }));

            query.ToList();
            Assert.That(Pairs(facets.For(p => p.Pile)), Is.EqualTo(new[] { "news=3", "blog=1" }));
        }

        [Test]
        public void DocumentsWithoutFacetFields_AreSearchableButNotCounted()
        {
            // Written by a mapping without facets, as an index created before facets were added would be.
            var legacy = new ClassMap<Passage>(version);
            legacy.Key(p => p.Id).NotAnalyzed();
            legacy.Property(p => p.Pile).NotAnalyzed();
            using (var session = provider.OpenSession(legacy.ToDocumentMapper()))
            {
                session.Add(new Passage { Id = "old", Pile = "news" });
                session.Commit();
            }

            var results = Passages.Facets(out var facets, f => f.Count(p => p.Pile)).ToList();
            Assert.That(results.Select(r => r.Id), Is.EqualTo(new[] { "old" }));
            Assert.That(facets.For(p => p.Pile), Is.Empty, "no document in the index has facet fields yet");

            Add(new Passage { Id = "new", Pile = "news" });

            results = Passages.Facets(out facets, f => f.Count(p => p.Pile)).ToList();
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(Pairs(facets.For(p => p.Pile)), Is.EqualTo(new[] { "news=1" }));
        }

        [Test]
        public void DocumentsWithoutFacetFields_NumericRanges_CountOnlyReindexedDocuments()
        {
            // A mapping without facets indexes the same numeric fields as terms only:
            // Pages as Int32, Published as Int64 ticks, Score as double, Weight as float.
            // Range counts read the hidden {Field}$range column, which those segments
            // don't have, so they must neither un-invert the terms (which throws for
            // Int32 terms read as Int64) nor count the old document.
            var legacy = new ClassMap<Passage>(version);
            legacy.Key(p => p.Id).NotAnalyzed();
            legacy.Property(p => p.Pile).NotAnalyzed();
            legacy.Property(p => p.Pages).AsNumericField();
            legacy.Property(p => p.Published).AsNumericField();
            legacy.Property(p => p.Score).AsNumericField();
            legacy.Property(p => p.Weight).AsNumericField();
            using (var session = provider.OpenSession(legacy.ToDocumentMapper()))
            {
                session.Add(new Passage { Id = "old", Pile = "news", Published = Utc(2026, 1, 15), Pages = 10, Score = 0.5, Weight = 0.5f });
                session.Commit();
            }

            var all = new[] { new FacetRange("all", null, null) };
            Action<FacetRequest<Passage>> ranges = f => f
                .Ranges(p => p.Pages, all)
                .Ranges(p => p.Published, all)
                .Ranges(p => p.Score, all)
                .Ranges(p => p.Weight, all);

            var results = Passages.Facets(out var facets, ranges).ToList();
            Assert.That(results.Select(r => r.Id), Is.EqualTo(new[] { "old" }));
            AssertRangeCounts(facets, "all=0", "no document in the index has range columns yet");

            Add(new Passage { Id = "new", Pile = "news", Published = Utc(2026, 2, 10), Pages = 20, Score = 1.5, Weight = 1.5f });

            results = Passages.Facets(out facets, ranges).ToList();
            Assert.That(results.Count, Is.EqualTo(2));
            AssertRangeCounts(facets, "all=1", "only the re-indexed document is counted");

            // The collector visits every segment, also the ones without a hit.
            results = Passages.Where(p => p.Id == "new").Facets(out facets, ranges).ToList();
            Assert.That(results.Count, Is.EqualTo(1));
            AssertRangeCounts(facets, "all=1", "query narrowed to the re-indexed document");

            // The old document is still found and sorted by its numeric fields.
            Assert.That(Passages.Where(p => p.Pages >= 10).OrderBy(p => p.Pages).Select(p => p.Id).ToList(), Is.EqualTo(new[] { "old", "new" }));
        }

        private static void AssertRangeCounts(FacetResults<Passage> facets, string expected, string message)
        {
            Assert.That(Pairs(facets.For(p => p.Pages)), Is.EqualTo(new[] { expected }), "int: " + message);
            Assert.That(Pairs(facets.For(p => p.Published)), Is.EqualTo(new[] { expected }), "DateTime (long): " + message);
            Assert.That(Pairs(facets.For(p => p.Score)), Is.EqualTo(new[] { expected }), "double: " + message);
            Assert.That(Pairs(facets.For(p => p.Weight)), Is.EqualTo(new[] { expected }), "float: " + message);
        }

        [Test]
        public void Ranges_NumericField_NullValuesAreNotCounted()
        {
            Add(new FacetEvent { Id = "1", When = Utc(2026, 1, 15), Size = 5, Rank = 1 },
                new FacetEvent { Id = "2", When = Utc(2026, 1, 16), Size = null, Rank = 2 });

            var results = provider.AsQueryable<FacetEvent>()
                .Facets(out var facets, f => f
                    .Ranges(p => p.Size, new[] { new FacetRange("all", null, null), new FacetRange("< 10", null, 10L) })
                    .Ranges(p => p.Rank, new[] { new FacetRange("all", null, null), new FacetRange("2+", 2, null) }))
                .OrderByDescending(p => p.Rank)
                .ToList();

            Assert.That(results.Select(r => r.Id), Is.EqualTo(new[] { "2", "1" }));
            Assert.That(Pairs(facets.For(p => p.Size)), Is.EqualTo(new[] { "all=1", "< 10=1" }));
            Assert.That(Pairs(facets.For(p => p.Rank)), Is.EqualTo(new[] { "all=2", "2+=1" }));
        }

        [Test]
        public void Ranges_NumericDateTimeOffset_AcceptsDateTimeBounds()
        {
            Add(new FacetEvent { Id = "1", When = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) },
                new FacetEvent { Id = "2", When = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero) },
                new FacetEvent { Id = "3", When = new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero) });

            // Daily, Monthly and Yearly only produce DateTime bounds.
            provider.AsQueryable<FacetEvent>()
                .Facets(out var facets, f => f.Ranges(p => p.When, FacetRange.Monthly(Utc(2026, 1, 1), Utc(2026, 2, 1))))
                .ToList();

            Assert.That(Pairs(facets.For(p => p.When)), Is.EqualTo(new[] { "2026-01=2", "2026-02=1" }));

            provider.AsQueryable<FacetEvent>()
                .Facets(out facets, f => f.Ranges(p => p.When, new[]
                {
                    new FacetRange("jan", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)),
                }))
                .ToList();

            Assert.That(Pairs(facets.For(p => p.When)), Is.EqualTo(new[] { "jan=2" }));
        }

        [Test]
        public void Ranges_DateTime_AcceptDateTimeOffsetBounds()
        {
            AddSamples();

            // An offset bound is the same instant as its UTC DateTime: 2026-02-01T00:00Z.
            var jan = new[]
            {
                new FacetRange("jan", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 1, 2, 0, 0, TimeSpan.FromHours(2))),
            };

            Passages.Facets(out var facets, f => f.Ranges(p => p.Published, jan).Ranges(p => p.Date, jan)).ToList();

            Assert.That(Pairs(facets.For(p => p.Published)), Is.EqualTo(new[] { "jan=1" }), "[NumericField] DateTime");
            Assert.That(Pairs(facets.For(p => p.Date)), Is.EqualTo(new[] { "jan=1" }), "[Field] DateTime");
        }

        [Test]
        public void ToDocument_NumericField_WritesHiddenRangeColumn_AndReplacesOnReuse()
        {
            var mapper = new ReflectionDocumentMapper<FacetEvent>(version, null);
            var doc = new Document();
            const string suffix = ReflectionFieldMapper<FacetEvent>.RangeFacetFieldSuffix;

            mapper.ToDocument(new FacetEvent { Id = "1", When = Utc(2026, 1, 15), Size = 5, Rank = 1 }, doc);

            // Facet alone adds no doc-values column under the field's own name.
            Assert.That(doc.GetFields("Size").Length, Is.EqualTo(1), "indexed field only");
            Assert.That(doc.GetFields("When").Length, Is.EqualTo(1), "indexed field only");
            Assert.That(doc.GetFields("Rank").Length, Is.EqualTo(2), "indexed field and the DocValues = true column");
            Assert.That(doc.GetField("Size" + suffix).GetInt64Value(), Is.EqualTo(5L));
            Assert.That(doc.GetField("When" + suffix).GetInt64Value(), Is.EqualTo(Utc(2026, 1, 15).Ticks));
            Assert.That(doc.GetField("Rank" + suffix).GetInt64Value(), Is.EqualTo(1L));

            mapper.ToDocument(new FacetEvent { Id = "1", When = Utc(2026, 1, 16), Size = null, Rank = 2 }, doc);

            Assert.That(doc.GetFields("Size"), Is.Empty);
            Assert.That(doc.GetFields("Size" + suffix), Is.Empty, "a null value removes the earlier range column");
            Assert.That(doc.GetFields("When" + suffix).Length, Is.EqualTo(1), "range columns are replaced, not duplicated");
            Assert.That(doc.GetFields("Rank").Length, Is.EqualTo(2));
            Assert.That(doc.GetFields("Rank" + suffix).Length, Is.EqualTo(1));
            Assert.That(doc.GetField("Rank" + suffix).GetInt64Value(), Is.EqualTo(2L));
        }

        [Test]
        public void ResultsBeforeExecution_Throw()
        {
            Passages.Facets(out var facets, f => f.Count(p => p.Pile));

            Assert.That(facets.IsAvailable, Is.False);
            Assert.Throws<InvalidOperationException>(() => facets.For(p => p.Pile));
        }

        [Test]
        public void PropertyNotMappedAsFacet_Throws()
        {
            AddSamples();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Passages.Facets(out _, f => f.Count(p => p.NotFaceted)).ToList());
            Assert.That(ex.Message, Does.Contain("NotFaceted"));
        }

        [Test]
        public void RangesOnCollection_Throw()
        {
            AddSamples();

            Assert.Throws<NotSupportedException>(() =>
                Passages.Facets(out _, f => f.Ranges(p => p.Topics, new[] { new FacetRange("x", "a", "b") })).ToList());
        }

        [Test]
        public void RequestValidation()
        {
            Assert.Throws<ArgumentException>(() => Passages.Facets(out _, f => { }));
            Assert.Throws<ArgumentException>(() => Passages.Facets(out _, f => f.Count(p => p.Pile).Count(p => p.Pile)));
            Assert.Throws<ArgumentException>(() => Passages.Facets(out _, f => f.Count(p => p.Pile.Length)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Passages.Facets(out _, f => f.Count(p => p.Pile, top: 0)));
        }

        [Test]
        public void FluentMapping_Facet()
        {
            var map = new ClassMap<Passage>(version);
            map.Key(p => p.Id).NotAnalyzed();
            map.Property(p => p.Topics).NotAnalyzed().Facet();
            map.Property(p => p.Pages).AsNumericField().Facet();
            var mapper = map.ToDocumentMapper();

            using (var session = provider.OpenSession(mapper))
            {
                session.Add(new Passage { Id = "1", Topics = new[] { "a", "b" }, Pages = 5 });
                session.Add(new Passage { Id = "2", Topics = new[] { "a" }, Pages = 50 });
                session.Commit();
            }

            provider.AsQueryable(mapper)
                .Facets(out var facets, f => f
                    .Count(p => p.Topics)
                    .Ranges(p => p.Pages, new[] { new FacetRange("small", null, 10), new FacetRange("big", 10, null) }))
                .ToList();

            Assert.That(Pairs(facets.For(p => p.Topics)), Is.EqualTo(new[] { "a=2", "b=1" }));
            Assert.That(Pairs(facets.For(p => p.Pages)), Is.EqualTo(new[] { "small=1", "big=1" }));
        }

        public class CustomMapped
        {
            public string Id { get; set; }
            public string Category { get; set; }
            public string[] Tags { get; set; }
        }

        /// <summary>
        /// A custom multi-valued facet field mapper, as a subclass might write.
        /// </summary>
        private class TagsMapper : IFacetFieldMapper<CustomMapped>
        {
            public string FieldName => "tag";
            public string PropertyName => nameof(CustomMapped.Tags);
            public bool Facet => true;
            public string RangeFacetField => null;
            public NumericFieldType RangeFacetValueType => NumericFieldType.NONE;
            public Lucene.Net.Analysis.Analyzer Analyzer => new Lucene.Net.Analysis.Core.KeywordAnalyzer();
            public IndexMode IndexMode => IndexMode.NotAnalyzed;

            public IEnumerable<string> GetFacetValues(CustomMapped source) => source.Tags ?? Enumerable.Empty<string>();

            public void CopyToDocument(CustomMapped source, Document target)
            {
                target.RemoveFields(FieldName);
                foreach (var tag in source.Tags ?? new string[0])
                    target.Add(new StringField(FieldName, tag, Field.Store.YES));
            }

            public void CopyFromDocument(Document source, IQueryExecutionContext context, CustomMapped target)
                => target.Tags = source.GetValues(FieldName);

            public object GetPropertyValue(CustomMapped source) => source.Tags;
            public object ConvertToRangeFacetValue(object value) => throw new NotSupportedException();
            public string ConvertToQueryExpression(object value) => (string)value;
            public string EscapeSpecialCharacters(string str) => str;
            public Lucene.Net.Search.Query CreateQuery(string pattern) => new Lucene.Net.Search.TermQuery(new Term(FieldName, pattern));
            public Lucene.Net.Search.Query CreateRangeQuery(object lowerBound, object upperBound, Lucene.Net.Linq.Search.RangeType lowerRange, Lucene.Net.Linq.Search.RangeType upperRange) => throw new NotSupportedException();
            public Lucene.Net.Search.SortField CreateSortField(bool reverse) => throw new NotSupportedException();
        }

        /// <summary>
        /// A subclass that adds its own field mappers through AddField/AddKeyField.
        /// </summary>
        private class CustomMapper : DocumentMapperBase<CustomMapped>
        {
            public CustomMapper() : base(LuceneVersion.LUCENE_48)
            {
                var keyword = new Lucene.Net.Analysis.Core.KeywordAnalyzer();

                AddKeyField(new ReflectionFieldMapper<CustomMapped>(typeof(CustomMapped).GetProperty(nameof(CustomMapped.Id)),
                    StoreMode.Yes, IndexMode.NotAnalyzed, TermVectorMode.No,
                    null, "Id", Lucene.Net.QueryParsers.Classic.Operator.OR, true, keyword, 1f, false, false));

                AddField(new ReflectionFieldMapper<CustomMapped>(typeof(CustomMapped).GetProperty(nameof(CustomMapped.Category)),
                    StoreMode.Yes, IndexMode.NotAnalyzed, TermVectorMode.No,
                    null, "cat", Lucene.Net.QueryParsers.Classic.Operator.OR, true, keyword, 1f, false, false, facet: true));

                AddField(new TagsMapper());
            }
        }

        [Test]
        public void FieldMappersAddedThroughAddField_AreFaceted()
        {
            var mapper = new CustomMapper();

            using (var session = provider.OpenSession(mapper))
            {
                session.Add(new CustomMapped { Id = "1", Category = "c1", Tags = new[] { "x", "y" } });
                session.Add(new CustomMapped { Id = "2", Category = "c1", Tags = new[] { "x" } });
                session.Add(new CustomMapped { Id = "3", Category = "c2" });
                session.Commit();
            }

            provider.AsQueryable(mapper).Facets(out var facets, f => f.Count(p => p.Category).Count(p => p.Tags)).ToList();

            Assert.That(Pairs(facets.For(p => p.Category)), Is.EqualTo(new[] { "c1=2", "c2=1" }));
            Assert.That(Pairs(facets.For(p => p.Tags)), Is.EqualTo(new[] { "x=2", "y=1" }));
        }

        [Test]
        public void ToDocument_FillsCallerDocumentWithFacetFields_AndReplacesOnReuse()
        {
            var mapper = new ReflectionDocumentMapper<Passage>(version, null);
            var doc = new Document();

            mapper.ToDocument(new Passage { Id = "1", Pile = "news", Topics = new[] { "a", "b" }, Date = Utc(2026, 1, 1) }, doc);

            var facetFields = doc.GetFields(FacetsConfig.DEFAULT_INDEX_FIELD_NAME);
            Assert.That(facetFields.Length, Is.GreaterThan(0));
            Assert.That(doc.GetFields("Date" + ReflectionFieldMapper<Passage>.RangeFacetFieldSuffix).Length, Is.EqualTo(1));

            mapper.ToDocument(new Passage { Id = "1", Pile = "news", Topics = new[] { "a", "b" }, Date = Utc(2026, 1, 1) }, doc);

            Assert.That(doc.GetFields(FacetsConfig.DEFAULT_INDEX_FIELD_NAME).Length, Is.EqualTo(facetFields.Length), "facet fields are replaced, not duplicated");
            Assert.That(doc.GetFields("Date" + ReflectionFieldMapper<Passage>.RangeFacetFieldSuffix).Length, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateValuesInOneDocument_CountOnce()
        {
            Add(new Passage { Id = "1", Pile = "news", Topics = new[] { "a", "a", "b", "a" }, People = new List<string> { "Ann", "Ann" } });
            Add(new Passage { Id = "2", Pile = "news", Topics = new[] { "a" }, People = new List<string> { "Bob" } });

            Passages.Facets(out var facets, f => f.Count(p => p.Topics).Count(p => p.People)).ToList();

            Assert.That(Pairs(facets.For(p => p.Topics)), Is.EqualTo(new[] { "a=2", "b=1" }));
            Assert.That(Pairs(facets.For(p => p.People)), Is.EqualTo(new[] { "Ann=1", "Bob=1" }));
        }
    }

#if NET8_0_OR_GREATER
    public class FacetVectorDocument
    {
        [Field(IndexMode.NotAnalyzed, Key = true)]
        public string Id { get; set; }

        [Field, VectorField]
        public string Title { get; set; }

        [Field(IndexMode.NotAnalyzed, Facet = true)]
        public string Category { get; set; }
    }

    [TestFixture]
    public class FacetVectorTests
    {
        private static readonly Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>> generator =
            new ElBruno.LocalEmbeddings.LocalEmbeddingGenerator(new ElBruno.LocalEmbeddings.Options.LocalEmbeddingsOptions
            {
                ModelName = "SmartComponents/bge-micro-v2",
                PreferQuantized = true
            });

        private RAMDirectory directory;
        private IndexWriter writer;
        private LuceneDataProvider provider;

        [OneTimeSetUp]
        public void SetUp()
        {
            directory = new RAMDirectory();
            writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48,
                new Lucene.Net.Analysis.Standard.StandardAnalyzer(LuceneVersion.LUCENE_48)));
            provider = new LuceneDataProvider(directory, LuceneVersion.LUCENE_48, writer);
            provider.Settings.EmbeddingGenerator = generator;

            using (var session = provider.OpenSession<FacetVectorDocument>())
            {
                session.Add(new FacetVectorDocument { Id = "1", Title = "the quick brown fox jumps over the lazy dog", Category = "animals" });
                session.Add(new FacetVectorDocument { Id = "2", Title = "a small kitten sleeping on a warm blanket", Category = "animals" });
                session.Add(new FacetVectorDocument { Id = "3", Title = "quantum physics and string theory research", Category = "science" });
                session.Add(new FacetVectorDocument { Id = "4", Title = "the big friendly bear eats honey in the forest", Category = "animals" });
                session.Add(new FacetVectorDocument { Id = "5", Title = "machine learning and neural network training", Category = "science" });
                session.Commit();
            }
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            provider.Dispose();
            writer.Dispose();
            directory.Dispose();
        }

        [Test]
        public void PureVectorQuery_CountsTheKNearestNeighbours()
        {
            var results = provider.AsQueryable<FacetVectorDocument>()
                .Where(d => d.Title.Similar("furry animals"))
                .Facets(out var facets, f => f.Count(d => d.Category))
                .Take(2)
                .ToList();

            Assert.That(results.Count, Is.EqualTo(2));

            var expected = results.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => g.Count());
            var counts = facets.For(d => d.Category).ToDictionary(v => v.Label, v => v.Count);
            Assert.That(counts, Is.EquivalentTo(expected), "the match set of a pure vector query is the Take(K) nearest neighbours");
        }

        [Test]
        public void HybridVectorQuery_CountsEveryDocumentMatchingTheFilter()
        {
            var results = provider.AsQueryable<FacetVectorDocument>()
                .Where(d => d.Title.Similar("furry animals") && d.Category == "animals")
                .Facets(out var facets, f => f.Count(d => d.Category))
                .Take(1)
                .ToList();

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(facets.For(d => d.Category).Select(v => v.Label + "=" + v.Count), Is.EqualTo(new[] { "animals=3" }));
        }
    }
#endif
}
