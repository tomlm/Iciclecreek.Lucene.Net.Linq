using System;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Linq.Search;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;

namespace Lucene.Net.Linq.Mapping
{
    internal class DocumentKeyFieldMapper<T> : IFieldMapper<T>, IDocumentFieldConverter
    {
        private readonly string fieldName;
        private readonly string value;

        public DocumentKeyFieldMapper(string fieldName, string value)
        {
            this.fieldName = fieldName;
            this.value = value;
        }

        public object GetPropertyValue(T source) => value;

        public void CopyToDocument(T source, Document target)
        {
            target.Add(new StringField(fieldName, value, Field.Store.YES));
        }

        public object GetFieldValue(Document document) => value;

        public void CopyFromDocument(Document source, IQueryExecutionContext context, T target) { }

        public string ConvertToQueryExpression(object value) => this.value;

        public string EscapeSpecialCharacters(string value)
        {
            return QueryParserBase.Escape(value ?? string.Empty);
        }

        public Query CreateRangeQuery(object lowerBound, object upperBound, RangeType lowerRange, RangeType upperRange)
            => throw new NotSupportedException();

        public Query CreateQuery(string ignored) => new TermQuery(new Term(FieldName, value));

        public SortField CreateSortField(bool reverse) => throw new NotSupportedException();

        public string PropertyName => "**DocumentKey**" + fieldName;
        public string FieldName => fieldName;
        public Analyzer Analyzer => new KeywordAnalyzer();
        public IndexMode IndexMode => IndexMode.NotAnalyzed;
    }
}
