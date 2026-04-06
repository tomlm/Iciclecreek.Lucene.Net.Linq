using System.IO;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;

namespace Lucene.Net.Linq.Analysis
{
    /// <summary>
    /// Wraps a <see cref="KeywordTokenizer"/> with a <see cref="LowerCaseFilter"/>
    /// so that queries with different case-spelling will match indexed values.
    /// </summary>
    public sealed class CaseInsensitiveKeywordAnalyzer : Analyzer
    {
        private static readonly LuceneVersion MatchVersion = LuceneVersion.LUCENE_48;

        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            var tokenizer = new KeywordTokenizer(reader);
            TokenStream filter = new LowerCaseFilter(MatchVersion, tokenizer);
            return new TokenStreamComponents(tokenizer, filter);
        }
    }
}
