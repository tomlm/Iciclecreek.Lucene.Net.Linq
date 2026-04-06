using System;
using System.Collections.Generic;
using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Version = Lucene.Net.Util.Version;

namespace Lucene.Net.Linq.Mapping
{
    /// <summary>
    /// Extends <see cref="ReflectionDocumentMapper{T}"/> to collect
    /// term-vector data per document. <para/>
    /// TODO Lucene 4.8 port: the Lucene 3.x ITermFreqVector type was
    /// removed; per-document term vectors are now retrieved via
    /// <see cref="IndexReader.GetTermVector(int, string)"/> which returns
    /// <see cref="Terms"/>. The class is preserved as a stub so existing
    /// downstream references compile; the term-vector retrieval method
    /// throws until the API is reworked.
    /// </summary>
    public class TermFreqVectorDocumentMapper<T> : ReflectionDocumentMapper<T>
    {
        private readonly IDictionary<T, Terms[]> map = new Dictionary<T, Terms[]>();

        public TermFreqVectorDocumentMapper(Version version) : base(version) { }

        public TermFreqVectorDocumentMapper(Version version, Analyzer externalAnalyzer)
            : base(version, externalAnalyzer) { }

        public override void ToObject(Documents.Document source, IQueryExecutionContext context, T target)
        {
            base.ToObject(source, context, target);
            // Term vector retrieval needs porting; skip rather than throw so
            // base ToObject still functions for callers that ignore vectors.
        }

        public Terms[] this[T index] => map.TryGetValue(index, out var v)
            ? v
            : throw new NotSupportedException("Term-vector retrieval has not yet been ported to Lucene.Net 4.8.");
    }
}
