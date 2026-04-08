// Stage 5 port: Lucene.Net.Search.Similar.MoreLikeThis was moved to the
// Lucene.Net.Queries.Mlt package in 4.8 with a substantially different API
// (no SetFieldNames builder, different ctor, etc.). The sample is preserved
// as a stub for documentation purposes; the original implementation is in
// git history if needed as a starting point for a fresh port.

using NUnit.Framework;

namespace Sample
{
    [TestFixture]
    [Explicit]
    [Ignore("MoreLikeThisSample needs to be re-ported against Lucene.Net.Queries.Mlt")]
    public class MoreLikeThisSample
    {
        [Test]
        public void Demo()
        {
        }
    }
}
