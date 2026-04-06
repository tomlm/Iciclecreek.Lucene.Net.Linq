using System.Collections.Generic;
using System.IO;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;

namespace Lucene.Net.Linq.Util
{
    internal static class AnalyzerExtensions
    {
        internal static string Analyze(this Analyzer analyzer, string fieldName, string pattern)
        {
            using (var enumerator = analyzer.GetTerms(fieldName, pattern).GetEnumerator())
            {
                if (!enumerator.MoveNext())
                {
                    return string.Empty;
                }
                return enumerator.Current;
            }
        }

        internal static IEnumerable<string> GetTerms(this Analyzer analyzer, string fieldName, string pattern)
        {
            var stream = analyzer.GetTokenStream(fieldName, new StringReader(pattern));
            try
            {
                stream.Reset();
                while (stream.IncrementToken())
                {
                    var attr = stream.GetAttribute<ICharTermAttribute>();
                    if (attr == null) continue;
                    yield return attr.ToString();
                }
                stream.End();
            }
            finally
            {
                stream.Dispose();
            }
        }
    }
}
