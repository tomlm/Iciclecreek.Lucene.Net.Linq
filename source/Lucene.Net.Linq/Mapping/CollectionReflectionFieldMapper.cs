using System;
using System.Collections;
using Lucene.Net.Documents;

namespace Lucene.Net.Linq.Mapping
{
    internal class CollectionReflectionFieldMapper<T> : ReflectionFieldMapper<T>
    {
        private readonly Type elementType;

        public CollectionReflectionFieldMapper(ReflectionFieldMapper<T> inner, Type elementType)
            : base(inner.PropertyInfo, inner.Store, inner.IndexMode, inner.TermVector, inner.Converter, inner.FieldName, inner.CaseSensitive, inner.Analyzer, inner.Boost)
        {
            this.elementType = elementType;
        }

        public override void CopyFromDocument(Document source, IQueryExecutionContext context, T target)
        {
            var values = new ArrayList();

            foreach (var field in source.GetFields(fieldName))
            {
                values.Add(ConvertFieldValue(field));
            }

            propertySetter(target, values.ToArray(elementType));
        }

        public override void CopyToDocument(T source, Document target)
        {
            target.RemoveFields(fieldName);

            var value = (IEnumerable)propertyGetter(source);

            if (value == null) return;

            foreach (var item in value)
            {
                AddField(target, item);
            }
        }
    }
}
