using System;
using System.Reflection;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Linq.Search;
using Lucene.Net.Search;

namespace Lucene.Net.Linq.Mapping
{
    /// <summary>
    /// Maps a property onto the document boost. <para/>
    /// TODO Lucene 4.8 port: document-level boost was removed in
    /// Lucene 4.8 — only field-level boost is supported. The mapper is
    /// preserved as a no-op for the write path so existing models that
    /// declare a [DocumentBoost] property still load; the value is silently
    /// ignored at index time. The read path returns 1.0f.
    /// </summary>
    internal class ReflectionDocumentBoostMapper<T> : IFieldMapper<T>, IDocumentFieldConverter
    {
        private readonly PropertyInfo propertyInfo;

        public ReflectionDocumentBoostMapper(PropertyInfo propertyInfo)
        {
            this.propertyInfo = propertyInfo;
        }

        public object GetFieldValue(Document document) => 1.0f;

        public void CopyToDocument(T source, Document target)
        {
            // Document boost is gone in Lucene 4.8 — silently drop.
        }

        public void CopyFromDocument(Document source, IQueryExecutionContext context, T target)
        {
            var value = GetFieldValue(source);
            propertyInfo.SetValue(target, value, null);
        }

        public SortField CreateSortField(bool reverse) => throw new NotSupportedException();
        public string ConvertToQueryExpression(object value) => throw new NotSupportedException();
        public string EscapeSpecialCharacters(string value) => throw new NotSupportedException();
        public Query CreateQuery(string pattern) => throw new NotSupportedException();
        public Query CreateRangeQuery(object lowerBound, object upperBound, RangeType lowerRange, RangeType upperRange) => throw new NotSupportedException();

        public object GetPropertyValue(T source) => propertyInfo.GetValue(source, null);

        public string PropertyName => propertyInfo.Name;
        public string FieldName => null;
        public Analyzer Analyzer => null;
        public IndexMode IndexMode => IndexMode.NotIndexed;
    }
}
