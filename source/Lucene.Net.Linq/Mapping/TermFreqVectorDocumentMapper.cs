using System.Collections.Generic;
using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Version = Lucene.Net.Util.LuceneVersion;

namespace Lucene.Net.Linq.Mapping
{
    /// <summary>
    /// Extends <see cref="ReflectionDocumentMapper{T}"/> to collect
    /// term-vector data per document. Per-document term vectors are
    /// retrieved via <see cref="IndexReader.GetTermVectors(int)"/> and
    /// projected into one <see cref="DocumentTermVector"/> per indexed
    /// field that has term vectors enabled. Use the
    /// <see cref="this[T]"/> indexer after running a query to retrieve
    /// the vectors collected for an item.
    /// </summary>
    public class TermFreqVectorDocumentMapper<T> : ReflectionDocumentMapper<T>
    {
        private readonly IDictionary<T, DocumentTermVector[]> map = new Dictionary<T, DocumentTermVector[]>();

        public TermFreqVectorDocumentMapper(Version version) : base(version) { }

        public TermFreqVectorDocumentMapper(Version version, Analyzer externalAnalyzer)
            : base(version, externalAnalyzer) { }

        public override void ToObject(Documents.Document source, IQueryExecutionContext context, T target)
        {
            base.ToObject(source, context, target);

            var reader = context.Searcher.IndexReader;
            var docId = context.CurrentScoreDoc.Doc;
            var fields = reader.GetTermVectors(docId);
            var vectors = new List<DocumentTermVector>();
            if (fields != null)
            {
                foreach (var fieldName in fields)
                {
                    var terms = fields.GetTerms(fieldName);
                    if (terms != null)
                    {
                        vectors.Add(new DocumentTermVector(fieldName, terms));
                    }
                }
            }
            map[target] = vectors.ToArray();
        }

        public DocumentTermVector[] this[T index] => map[index];
    }

    /// <summary>
    /// Captured term-vector data for a single field of a single document.
    /// </summary>
    public class DocumentTermVector
    {
        private readonly string[] terms;
        private readonly int[] frequencies;

        internal DocumentTermVector(string field, Terms termsCollection)
        {
            Field = field;
            var termList = new List<string>();
            var freqList = new List<int>();
            var enumerator = termsCollection.GetEnumerator();
            while (enumerator.MoveNext())
            {
                termList.Add(enumerator.Term.Utf8ToString());
                freqList.Add((int)enumerator.TotalTermFreq);
            }
            terms = termList.ToArray();
            frequencies = freqList.ToArray();
        }

        /// <summary>The name of the field these vectors came from.</summary>
        public string Field { get; }

        /// <summary>Returns the indexed terms for this field, in the order
        /// they were stored in the term vector (typically lexicographic).</summary>
        public string[] GetTerms() => (string[])terms.Clone();

        /// <summary>Returns the per-document occurrence count for each term,
        /// aligned with <see cref="GetTerms"/>.</summary>
        public int[] GetTermFrequencies() => (int[])frequencies.Clone();
    }
}
