using System.IO;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;

namespace Lucene.Net.Linq.Tests.Integration
{
    /// <summary>
    /// 4.8 port: StandardAnalyzer is sealed in Lucene.Net 4.8, so we can no
    /// longer subclass it. Instead we compose a StandardTokenizer with a
    /// LowerCaseFilter and PorterStemFilter via CreateComponents.
    /// </summary>
    public class PorterStemAnalyzer : Analyzer
    {
        private readonly LuceneVersion matchVersion;

        public PorterStemAnalyzer(LuceneVersion version)
        {
            this.matchVersion = version;
        }

        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            var tokenizer = new StandardTokenizer(matchVersion, reader);
            TokenStream stream = new LowerCaseFilter(matchVersion, tokenizer);
            stream = new PorterStemFilter(stream);
            return new TokenStreamComponents(tokenizer, stream);
        }
    }
}
