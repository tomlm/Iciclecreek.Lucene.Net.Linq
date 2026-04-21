#if NET8_0_OR_GREATER
using ElBruno.LocalEmbeddings;
using ElBruno.LocalEmbeddings.Options;
#endif
using Microsoft.Extensions.AI;

namespace Lucene.Net.Linq.Tests
{
    internal static class SearchFixture
    {
        internal static readonly IEmbeddingGenerator<string, Embedding<float>> Generator =
#if NET8_0_OR_GREATER
        new LocalEmbeddingGenerator(new LocalEmbeddingsOptions
        {
            ModelName = "SmartComponents/bge-micro-v2",
            PreferQuantized = true
        });
#else
        null;
#endif
    }
}
