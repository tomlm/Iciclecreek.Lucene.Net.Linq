using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Integration
{
    /// <summary>
    /// Times <c>Similar()</c> queries with and without other conditions, right
    /// after a write ("cold": the searcher is new, so any vector index has to
    /// be rebuilt) and on an unchanged index ("warm"). Run explicitly:
    /// <c>dotnet test -c Release -f net10.0 --filter "FullyQualifiedName~VectorFilterBenchmark" --logger "console;verbosity=detailed"</c>
    /// The 50,000 case rebuilds the vector index several times, at about two minutes each.
    /// The file has no dependency on this version, so it can be copied into an older
    /// checkout to compare.
    /// </summary>
    [TestFixture, Explicit, Category("Benchmark")]
    public class VectorFilterBenchmark
    {
        public class BenchClip
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string Id { get; set; }

            [Field(IndexMode.NotAnalyzed)]
            public string Topic { get; set; }

            [Field, VectorField]
            public string Text { get; set; }
        }

        /// <summary>A second type in the same index, with the same vector field.</summary>
        public class BenchMemo
        {
            [Field(IndexMode.NotAnalyzed, Key = true)]
            public string Id { get; set; }

            [Field, VectorField]
            public string Text { get; set; }
        }

        /// <summary>A repeatable pseudo-random 384-dimension vector per text.</summary>
        private class RandomEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
        {
            public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
                IEnumerable<string> values, EmbeddingGenerationOptions options = null, CancellationToken cancellationToken = default)
            {
                var results = new GeneratedEmbeddings<Embedding<float>>();
                foreach (var text in values)
                {
                    var seed = 17;
                    foreach (var c in text) seed = unchecked(seed * 31 + c);
                    var random = new Random(seed);
                    var vector = new float[384];
                    for (var i = 0; i < vector.Length; i++) vector[i] = (float)(random.NextDouble() * 2 - 1);
                    results.Add(new Embedding<float>(vector));
                }
                return Task.FromResult(results);
            }

            public object GetService(Type serviceType, object serviceKey = null) => null;

            public void Dispose()
            {
            }
        }

        private const int WarmRuns = 15;
        private const int ColdRuns = 3;

        [TestCase(5000)]
        [TestCase(50000)]
        public void Similar_WithAndWithoutFilters(int clipCount)
        {
            using (var directory = new RAMDirectory())
            using (var provider = new LuceneDataProvider(directory, LuceneVersion.LUCENE_48))
            {
                provider.Settings.EmbeddingGenerator = new RandomEmbeddingGenerator();

                // 1% of the clips are "rare", 10% "some". A quarter as many memos share the vector field.
                using (var session = provider.OpenSession<BenchClip>())
                {
                    session.Add(Enumerable.Range(0, clipCount).Select(i => new BenchClip
                    {
                        Id = "c" + i,
                        Topic = i % 100 == 0 ? "rare" : i % 10 == 1 ? "some" : "common",
                        Text = "clip text " + i,
                    }).ToArray());
                    session.Commit();
                }

                using (var session = provider.OpenSession<BenchMemo>())
                {
                    session.Add(Enumerable.Range(0, clipCount / 4).Select(i => new BenchMemo { Id = "m" + i, Text = "memo text " + i }).ToArray());
                    session.Commit();
                }

                Func<IQueryable<BenchClip>> clips = () => provider.AsQueryable<BenchClip>();

                Console.WriteLine("{0} clips ({1} rare, {2} some) and {3} memos; Take(10); cold = median of {4}, warm = median of {5}",
                    clipCount, clipCount / 100, clipCount / 10, clipCount / 4, ColdRuns, WarmRuns);
                Console.WriteLine("  {0,-58} {1,10} {2,10} {3,6}", "query", "cold ms", "warm ms", "rows");

                Report(provider, "1% filter in another Where", () => clips().Where(c => c.Topic == "rare").Where(c => c.Text.Similar("find me")).Take(10).ToList());
                Report(provider, "1% filter in the same predicate", () => clips().Where(c => c.Topic == "rare" && c.Text.Similar("find me")).Take(10).ToList());
                Report(provider, "10% filter in another Where", () => clips().Where(c => c.Topic == "some").Where(c => c.Text.Similar("find me")).Take(10).ToList());
                Report(provider, "10% filter in the same predicate", () => clips().Where(c => c.Topic == "some" && c.Text.Similar("find me")).Take(10).ToList());
                Report(provider, "Similar alone (default settings)", () => clips().Where(c => c.Text.Similar("find me")).Take(10).ToList());

                if (TrySetExactLimit(provider, int.MaxValue))
                {
                    Report(provider, "Similar alone, always compared one by one", () => clips().Where(c => c.Text.Similar("find me")).Take(10).ToList());
                    TrySetExactLimit(provider, 0);
                    Report(provider, "Similar alone, always through the vector index", () => clips().Where(c => c.Text.Similar("find me")).Take(10).ToList());
                }
            }
        }

        /// <summary>Sets <c>Settings.VectorExactSearchLimit</c> where the library has it.</summary>
        private static bool TrySetExactLimit(LuceneDataProvider provider, int limit)
        {
            var property = provider.Settings.GetType().GetProperty("VectorExactSearchLimit");
            if (property == null) return false;
            property.SetValue(provider.Settings, limit);
            return true;
        }

        private static int writes;

        private static void Report(LuceneDataProvider provider, string name, Func<List<BenchClip>> query)
        {
            var rows = query().Count;

            // Cold: a write gives the next query a new searcher.
            var cold = new double[ColdRuns];
            for (var i = 0; i < ColdRuns; i++)
            {
                using (var session = provider.OpenSession<BenchMemo>())
                {
                    session.Add(new BenchMemo { Id = "w" + writes++, Text = "written between queries " + writes });
                    session.Commit();
                }

                var watch = Stopwatch.StartNew();
                query();
                cold[i] = watch.Elapsed.TotalMilliseconds;
            }

            var warm = new double[WarmRuns];
            for (var i = 0; i < WarmRuns; i++)
            {
                var watch = Stopwatch.StartNew();
                query();
                warm[i] = watch.Elapsed.TotalMilliseconds;
            }

            Array.Sort(cold);
            Array.Sort(warm);
            Console.WriteLine("  {0,-58} {1,10:F1} {2,10:F1} {3,6}", name, cold[ColdRuns / 2], warm[WarmRuns / 2], rows);
        }
    }
}
