using System;
using System.Collections;
using System.Collections.Generic;
using Lucene.Net.Documents;

namespace Lucene.Net.Linq.Mapping
{
    internal class CollectionReflectionFieldMapper<T> : ReflectionFieldMapper<T>
    {
        private readonly Type elementType;

        public CollectionReflectionFieldMapper(ReflectionFieldMapper<T> inner, Type elementType)
            : base(inner.PropertyInfo, inner.Store, inner.IndexMode, inner.TermVector, inner.Converter, inner.FieldName, Lucene.Net.QueryParsers.Classic.Operator.OR, inner.CaseSensitive, inner.Analyzer, inner.Boost, nativeSort: false, docValues: false, facet: inner.Facet)
        {
            this.elementType = elementType;
        }

        /// <summary>
        /// Collections support value counts only; Lucene 4.8 has no
        /// multi-valued numeric doc-values for range counts.
        /// </summary>
        public override string RangeFacetField => null;

        public override NumericFieldType RangeFacetValueType => NumericFieldType.NONE;

        public override object ConvertToRangeFacetValue(object value)
        {
            throw new NotSupportedException("Range facets are not supported on collection property " + PropertyName + ".");
        }

        /// <summary>
        /// A collection property has several terms per document, so it has no
        /// single-term key form.
        /// </summary>
        internal override string ConvertToTermText(object value) => null;

        public override IEnumerable<string> GetFacetValues(T source)
        {
            if (!facet) yield break;

            var value = (IEnumerable)propertyGetter(source);

            if (value == null) yield break;

            foreach (var item in value)
            {
                var label = ConvertToFacetLabel(item);

                if (!string.IsNullOrEmpty(label))
                    yield return label;
            }
        }

        public override void CopyFromDocument(Document source, IQueryExecutionContext context, T target)
        {
            var values = new ArrayList();

            foreach (var field in source.GetFields(fieldName))
            {
                values.Add(ConvertFieldValue(field));
            }

            var array = values.ToArray(elementType);
            var propertyType = propertyInfo.PropertyType;

            // Arrays and interfaces an array implements (IEnumerable<T>, IList<T>…) take the array as is;
            // concrete collections such as List<T> are built from it.
            propertySetter(target, propertyType.IsAssignableFrom(array.GetType())
                ? array
                : Activator.CreateInstance(propertyType, array));
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
