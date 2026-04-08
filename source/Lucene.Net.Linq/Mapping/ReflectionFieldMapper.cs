using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Linq.Search;
using Lucene.Net.Linq.Util;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Version = Lucene.Net.Util.LuceneVersion;
using System.Linq.Expressions;
using System.Collections.Concurrent;

namespace Lucene.Net.Linq.Mapping
{
    public class ReflectionFieldMapper<T> : IFieldMapper<T>, IDocumentFieldConverter
    {
        protected static ConcurrentDictionary<string, object> internalCache = new ConcurrentDictionary<string, object> (StringComparer.Ordinal);
        protected readonly PropertyInfo propertyInfo;
        protected readonly Func<T, object> propertyGetter;
        protected readonly Action<T, object> propertySetter;
        protected readonly StoreMode store;
        protected readonly IndexMode index;
        protected readonly TermVectorMode termVector;
        protected readonly TypeConverter converter;
        protected readonly string fieldName;
        protected readonly Operator defaultParserOperator;
        protected readonly bool caseSensitive;
        protected readonly Analyzer analyzer;
        protected readonly float boost;
        protected readonly bool nativeSort;
        protected readonly bool docValues;
        private readonly FieldType fieldType;

        public ReflectionFieldMapper(PropertyInfo propertyInfo, StoreMode store, IndexMode index, TermVectorMode termVector,
                                     TypeConverter converter, string fieldName, bool caseSensitive, Analyzer analyzer)
            : this(propertyInfo, store, index, termVector, converter, fieldName, caseSensitive, analyzer, 1f)
        {

        }

        public ReflectionFieldMapper(PropertyInfo propertyInfo, StoreMode store, IndexMode index, TermVectorMode termVector, TypeConverter converter, string fieldName, bool caseSensitive, Analyzer analyzer, float boost)
            : this(propertyInfo, store, index, termVector, converter, fieldName, Operator.OR, caseSensitive, analyzer, boost)
        {

        }

        public ReflectionFieldMapper(PropertyInfo propertyInfo, StoreMode store, IndexMode index, TermVectorMode termVector, TypeConverter converter, string fieldName, Operator defaultParserOperator, bool caseSensitive, Analyzer analyzer, float boost, bool nativeSort = false)
            : this(propertyInfo, store, index, termVector, converter, fieldName, defaultParserOperator, caseSensitive, analyzer, boost, nativeSort, docValues: false)
        {
        }

        public ReflectionFieldMapper(PropertyInfo propertyInfo, StoreMode store, IndexMode index, TermVectorMode termVector, TypeConverter converter, string fieldName, Operator defaultParserOperator, bool caseSensitive, Analyzer analyzer, float boost, bool nativeSort, bool docValues)
        {
            this.propertyInfo = propertyInfo;
            this.propertyGetter = CreatePropertyGetter(propertyInfo);
            if (propertyInfo.CanWrite)
                this.propertySetter = CreatePropertySetter(propertyInfo);
            this.store = store;
            this.index = index;
            this.termVector = termVector;
            this.converter = converter;
            this.fieldName = fieldName;
            this.defaultParserOperator = defaultParserOperator;
            this.caseSensitive = caseSensitive;
            this.analyzer = analyzer;
            this.boost = boost;
            this.nativeSort = nativeSort;
            this.docValues = docValues;
            this.fieldType = FieldTypeBuilder.Build(store, index, termVector);
        }

        public virtual Analyzer Analyzer => analyzer;
        public virtual PropertyInfo PropertyInfo => propertyInfo;
        public virtual StoreMode Store => store;
        public virtual IndexMode IndexMode => index;
        public virtual TermVectorMode TermVector => termVector;
        public virtual TypeConverter Converter => converter;
        public virtual string FieldName => fieldName;
        public virtual bool CaseSensitive => caseSensitive;
        public virtual float Boost => boost;
        public virtual string PropertyName => propertyInfo.Name;
        public virtual Operator DefaultParseOperator => defaultParserOperator;
        public virtual bool NativeSort => nativeSort;
        public virtual bool DocValues => docValues;

        public virtual object GetPropertyValue(T source) => propertyGetter(source);

        public virtual void CopyFromDocument(Document source, IQueryExecutionContext context, T target)
        {
            if (!propertyInfo.CanWrite) return;

            var fieldValue = GetFieldValue(source);

            if (fieldValue != null)
                propertySetter(target, fieldValue);
        }

        public object GetFieldValue(Document document)
        {
            var field = document.GetField(fieldName);

            if (field == null)
                return null;

            if (!propertyInfo.CanWrite)
                return null;

            return ConvertFieldValue(field);
        }

        public virtual void CopyToDocument(T source, Document target)
        {
            var value = propertyGetter(source);

            target.RemoveFields(fieldName);

            AddField(target, value);
        }

        public virtual string ConvertToQueryExpression(object value)
        {
            if (converter != null)
            {
                return (string)converter.ConvertTo(value, typeof(string));
            }

            return (string)value;
        }

        public virtual string EscapeSpecialCharacters(string value)
        {
            return QueryParserBase.Escape(value ?? string.Empty);
        }

        public virtual Query CreateQuery(string pattern)
        {
            Query query;

            if (TryParseKeywordContainingWhitespace(pattern, out query))
            {
                return query;
            }

            var queryParser = new QueryParser(Version.LUCENE_48, FieldName, analyzer)
            {
                AllowLeadingWildcard = true,
                LowercaseExpandedTerms = !CaseSensitive,
                DefaultOperator = defaultParserOperator
            };

            return queryParser.Parse(pattern);
        }

        /// <summary>
        /// Attempt to determine if a given query pattern contains whitespace and
        /// the analyzer does not tokenize on whitespace. This is a work-around
        /// for cases when QueryParser would split a keyword that contains whitespace
        /// into multiple tokens.
        /// </summary>
        protected virtual bool TryParseKeywordContainingWhitespace(string pattern, out Query query)
        {
            query = null;

            if (pattern.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) < 0) return false;

            var terms = Analyzer.GetTerms(FieldName, pattern).ToList();

            if (terms.Count > 1) return false;

            var termValue = Unescape(terms.Single());
            var term = new Term(FieldName, termValue);

            if (IsWildcardPattern(termValue))
            {
                query = new WildcardQuery(term);
            }
            else
            {
                query = new TermQuery(term);
            }

            return true;
        }

        protected virtual bool IsWildcardPattern(string pattern)
        {
            var unescaped = pattern.Replace(@"\\", "");
            return unescaped.Replace(@"\*", "").Contains("*")
                || unescaped.Replace(@"\?", "").Contains("?");
        }

        protected virtual string Unescape(string pattern)
        {
            return pattern.Replace(@"\", "");
        }

        private static Func<T, object> CreatePropertyGetter(System.Reflection.PropertyInfo propertyInfo)
        {
            string cacheKey = "getter." + propertyInfo.GetHashCode ();
            object cache;
            if (internalCache.TryGetValue (cacheKey, out cache))
                return (Func<T, object>)cache;

            var name = propertyInfo.Name;
            var source = Expression.Parameter(typeof(T));
            var method = Expression.Lambda<Func<T, object>>(Expression.Convert(Expression.Property(source, name), typeof (object)), source).Compile();

            internalCache.TryAdd (cacheKey, method);
            return method;
        }

        private static Action<T, object> CreatePropertySetter(System.Reflection.PropertyInfo propertyInfo)
        {
            string cacheKey = "setter." + propertyInfo.GetHashCode ();
            object cache;
            if (internalCache.TryGetValue (cacheKey, out cache))
                return (Action<T, object>)cache;

            var name = propertyInfo.Name;
            var propType = propertyInfo.PropertyType;

            var sourceType = Expression.Parameter(typeof(T));
            var argument = Expression.Parameter(typeof(object), name);
            var propExp = Expression.Property(sourceType, name);

            var castToObject = Expression.Convert(argument, propType);

            var method = Expression.Lambda<Action<T, object>> (Expression.Assign (propExp, castToObject), sourceType, argument).Compile ();

            internalCache.TryAdd (cacheKey, method);
            return method;
        }

        public virtual Query CreateRangeQuery(object lowerBound, object upperBound, RangeType lowerRange, RangeType upperRange)
        {
            var minInclusive = lowerRange == RangeType.Inclusive;
            var maxInclusive = upperRange == RangeType.Inclusive;

            var lowerBoundStr = lowerBound == null ? null : EvaluateExpressionToStringAndAnalyze(lowerBound);
            var upperBoundStr = upperBound == null ? null : EvaluateExpressionToStringAndAnalyze(upperBound);
            return TermRangeQuery.NewStringRange(FieldName, lowerBoundStr, upperBoundStr, minInclusive, maxInclusive);
        }

        public virtual SortField CreateSortField(bool reverse)
        {
            if (docValues)
                return new SortField(FieldName, SortFieldType.STRING, reverse);

            if (Converter == null || NativeSort)
                return new SortField(FieldName, SortFieldType.STRING, reverse);

            var propertyType = propertyInfo.PropertyType;

            // Lucene.Net 4.8's FieldComparer<T> constrains T to reference
            // types, so the converter-based custom comparator path can only
            // service properties whose declared type is a reference type.
            // Value types (int, bool, DateTime, nullables) fall back to a
            // string SortField; properties that need true numeric ordering
            // should be marked [NumericField], which routes to
            // NumericReflectionFieldMapper and a typed SortField instead.
            if (propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) != null)
                return new SortField(FieldName, SortFieldType.STRING, reverse);

            FieldComparerSource source;

            if (typeof(IComparable).IsAssignableFrom(propertyType))
            {
                source = new NonGenericConvertableFieldComparatorSource(propertyType, Converter);
            }
            else if (typeof(IComparable<>).MakeGenericType(propertyType).IsAssignableFrom(propertyType))
            {
                source = new GenericConvertableFieldComparatorSource(propertyType, Converter);
            }
            else
            {
                throw new NotSupportedException(string.Format(
                    "The type {0} does not implement IComparable or IComparable<T>. To use alphanumeric sorting, specify NativeSort=true on the mapping.",
                    propertyType));
            }

            return new SortField(FieldName, source, reverse);
        }

        private string EvaluateExpressionToStringAndAnalyze(object value)
        {
            return analyzer.Analyze(FieldName, ConvertToQueryExpression(value));
        }

        protected internal virtual object ConvertFieldValue(IIndexableField field)
        {
            var fieldValue = (object)field.GetStringValue();

            if (converter != null)
            {
                fieldValue = converter.ConvertFrom(fieldValue);
            }
            return fieldValue;
        }

        protected internal void AddField(Document target, object value)
        {
            if (value == null)
                return;

            var fieldValue = (string)null;

            if (converter != null)
            {
                fieldValue = (string)converter.ConvertTo(value, typeof(string));
            }
            else if (value is string)
            {
                fieldValue = (string)value;
            }

            if (fieldValue != null)
            {
                var field = new Field(fieldName, fieldValue, fieldType);
                field.Boost = Boost;
                target.Add(field);

                if (docValues)
                {
                    // Parallel SortedDocValues column for fast sort/group/facet.
                    // Lucene 4.8 allows a same-named DV field alongside the
                    // indexed/stored field; the typed SortField path reads
                    // from this column at sort time without uninverting via
                    // FieldCache.
                    target.Add(new SortedDocValuesField(fieldName, new BytesRef(fieldValue)));
                }
            }
        }
    }
}
