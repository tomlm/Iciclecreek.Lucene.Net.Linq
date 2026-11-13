using System;
using System.Diagnostics;
using System.Linq;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Store;
using Lucene.Net.Util;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    /// <summary>
    /// Compares a join run inside Lucene with the same join run in memory, on
    /// a broad outer query that only wants the first 20 rows. Run explicitly:
    /// <c>dotnet test -f net8.0 --filter "FullyQualifiedName~JoinPushdownBenchmark"</c>
    /// </summary>
    [TestFixture, Explicit, Category("Benchmark")]
    public class JoinPushdownBenchmark
    {
        public class BenchWriter
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string Username { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string Country { get; set; }
        }

        public class BenchStory
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string StoryId { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string WriterId { get; set; }

            [NumericField]
            public int Rank { get; set; }
        }

        private const int WriterCount = 5000;
        private const int StoryCount = 50000;
        private const int Iterations = 10;

        [Test]
        public void BroadOuterQuery_Take20()
        {
            using (var directory = new RAMDirectory())
            using (var provider = new LuceneDataProvider(directory, LuceneVersion.LUCENE_48))
            {
                using (var session = provider.OpenSession<BenchWriter>())
                {
                    session.Add(Enumerable.Range(0, WriterCount)
                        .Select(i => new BenchWriter { Username = "w" + i, Country = i % 2 == 0 ? "DE" : "US" }).ToArray());
                    session.Commit();
                }

                using (var session = provider.OpenSession<BenchStory>())
                {
                    // Every tenth story has a writer that does not exist.
                    session.Add(Enumerable.Range(0, StoryCount)
                        .Select(i => new BenchStory { StoryId = "s" + i, WriterId = "w" + (i % 10 == 0 ? -1 : i % WriterCount), Rank = i }).ToArray());
                    session.Commit();
                }

                Func<string[]> query = () =>
                    (from s in provider.AsQueryable<BenchStory>()
                     join w in provider.AsQueryable<BenchWriter>().Where(w => w.Country == "DE") on s.WriterId equals w.Username
                     orderby s.Rank
                     select s.StoryId).Take(20).ToArray();

                var pushed = Measure(provider, true, query, out var pushedRows);
                var inMemory = Measure(provider, false, query, out var inMemoryRows);

                Assert.That(pushedRows, Is.EqualTo(inMemoryRows));

                Console.WriteLine("{0} stories joined to {1} writers, Take(20), median of {2} runs", StoryCount, WriterCount, Iterations);
                Console.WriteLine("  pushed into Lucene: {0:F1} ms", pushed);
                Console.WriteLine("  joined in memory:   {0:F1} ms", inMemory);
                Console.WriteLine("  speed-up:           {0:F0}x", inMemory / pushed);
            }
        }

        private static double Measure(LuceneDataProvider provider, bool pushdown, Func<string[]> query, out string[] rows)
        {
            provider.Settings.EnableJoinPushdown = pushdown;

            rows = query(); // warm up: JIT, FieldCache

            var times = new double[Iterations];
            for (var i = 0; i < Iterations; i++)
            {
                var watch = Stopwatch.StartNew();
                query();
                times[i] = watch.Elapsed.TotalMilliseconds;
            }

            Array.Sort(times);
            return times[Iterations / 2];
        }
    }
}
