using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Linq.Search;
using Lucene.Net.Linq.Util;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;

namespace Lucene.Net.Linq.Mapping
{
    internal class NumericReflectionFieldMapper<T> : ReflectionFieldMapper<T>
    {
        private static readonly IEnumerable<Type> supportedValueTypes = new List<Type>{typeof(long), typeof(int), typeof(double), typeof(float)};

        private readonly TypeConverter typeToValueTypeConverter;
        private readonly int precisionStep;
        private readonly string rangeFacetField;

        public NumericReflectionFieldMapper(PropertyInfo propertyInfo, StoreMode store, TypeConverter typeToValueTypeConverter, TypeConverter valueTypeToStringConverter, string field, int precisionStep, float boost)
            : this(propertyInfo, store, typeToValueTypeConverter, valueTypeToStringConverter, field, precisionStep, boost, docValues: false)
        {
        }

        public NumericReflectionFieldMapper(PropertyInfo propertyInfo, StoreMode store, TypeConverter typeToValueTypeConverter, TypeConverter valueTypeToStringConverter, string field, int precisionStep, float boost, bool docValues)
            : this(propertyInfo, store, typeToValueTypeConverter, valueTypeToStringConverter, field, precisionStep, boost, docValues, facet: false)
        {
        }

        public NumericReflectionFieldMapper(PropertyInfo propertyInfo, StoreMode store, TypeConverter typeToValueTypeConverter, TypeConverter valueTypeToStringConverter, string field, int precisionStep, float boost, bool docValues, bool facet)
            : base(propertyInfo, store, IndexMode.Analyzed, TermVectorMode.No, valueTypeToStringConverter, field, Operator.OR, false, new KeywordAnalyzer(), boost, nativeSort: false, docValues: docValues, facet: facet)
        {
            this.typeToValueTypeConverter = typeToValueTypeConverter;
            this.precisionStep = precisionStep;
            this.rangeFacetField = facet ? fieldName + RangeFacetFieldSuffix : null;
        }

        /// <summary>
        /// Numeric fields keep range-facet values in the hidden
        /// <c>{Field}$range</c> doc-values column, as other properties do, and
        /// not in the column <c>DocValues = true</c> writes under the field's
        /// own name. Range counts read through FieldCache, which un-inverts the
        /// indexed terms of a segment that lacks the doc-values column. For the
        /// field's own name that would count documents written before
        /// <c>Facet</c> was set, and throw where the terms are Int32. The hidden
        /// field has no terms, so such segments are skipped.
        /// </summary>
        public override string RangeFacetField => rangeFacetField;

        public override NumericFieldType RangeFacetValueType
        {
            get
            {
                if (!facet) return NumericFieldType.NONE;

                var type = typeToValueTypeConverter != null
                    ? GetUnderlyingValueType()
                    : propertyInfo.PropertyType.GetUnderlyingType();

                if (type != null && type.IsEnum) type = Enum.GetUnderlyingType(type);

                // The range column widens int to long and float to double.
                if (type == typeof(double) || type == typeof(float)) return NumericFieldType.DOUBLE;
                return NumericFieldType.INT64;
            }
        }

        public override object ConvertToRangeFacetValue(object value)
        {
            if (value == null) return null;

            // FacetRange.Daily/Monthly/Yearly produce DateTime bounds, and neither
            // date type converts to the other through Convert.ChangeType. Accept
            // both on either kind of date property, as the base mapper does.
            var propertyType = propertyInfo.PropertyType.GetUnderlyingType();

            if (value is DateTime dateTime && propertyType == typeof(DateTimeOffset))
            {
                value = new DateTimeOffset(dateTime);
            }
            else if (value is DateTimeOffset dateTimeOffset && propertyType == typeof(DateTime))
            {
                value = dateTimeOffset.UtcDateTime;
            }

            value = ConvertToSupportedValueType(value);

            if (value != null && value.GetType().IsEnum)
            {
                value = Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()));
            }

            switch (value)
            {
                case double d: return d;
                case float f: return (double)f;
                default: return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// The converter of a numeric field produces the indexed number, which
        /// is no use as a label for dates, so the label is the value in
        /// invariant-culture form. Dates are converted to UTC first, as the
        /// indexed ticks of a <see cref="DateTime"/> are: one instant is one
        /// label whatever its <see cref="DateTime.Kind"/> or offset.
        /// </summary>
        protected internal override string ConvertToFacetLabel(object value)
        {
            switch (value)
            {
                case null: return null;
                case DateTime dt: return dt.ToUniversalTime().ToString(FieldMappingInfoBuilder.DefaultDateTimeFormat, CultureInfo.InvariantCulture);
                case DateTimeOffset dto: return dto.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        public int PrecisionStep
        {
            get { return precisionStep; }
        }

        public override SortField CreateSortField(bool reverse)
        {
            var targetType = propertyInfo.PropertyType;

            if (typeToValueTypeConverter != null)
            {
                targetType = GetUnderlyingValueType();
            }

            return new SortField(FieldName, targetType.ToSortFieldType(), reverse);
        }

        protected internal override object ConvertFieldValue(IIndexableField field)
        {
            object value;

            // Numeric typed fields expose strongly-typed accessors; we use
            // those rather than GetNumericValue() because in Lucene.Net 4.8
            // the latter returns a boxed J2N.Numerics.Int64/Int32/Single/Double
            // wrapper that does NOT cast directly to System.Int64/etc. The
            // typed accessors return BCL primitives.
            switch (field.NumericType)
            {
                case NumericFieldType.INT64:  value = field.GetInt64Value(); break;
                case NumericFieldType.INT32:  value = field.GetInt32Value(); break;
                case NumericFieldType.DOUBLE: value = field.GetDoubleValue(); break;
                case NumericFieldType.SINGLE: value = field.GetSingleValue(); break;
                case NumericFieldType.NONE:
                default:                      value = field.GetStringValue(); break;
            }

            if (typeToValueTypeConverter != null)
            {
                value = typeToValueTypeConverter.ConvertFrom(value);
            }
            else if (value is string s)
            {
                // Field was stored via the legacy string code path; coerce
                // back to the underlying property type.
                var propType = propertyInfo.PropertyType.GetUnderlyingType();
                value = Convert.ChangeType(s, propType);
            }

            return value;
        }

        public override void CopyToDocument(T source, Document target)
        {
            var value = propertyGetter(source);

            target.RemoveFields(fieldName);

            // Also when the value is null: a reused document may hold the
            // range column of an earlier value.
            var rangeField = rangeFacetField;
            if (rangeField != null) target.RemoveFields(rangeField);

            if (value == null) return;

            value = ConvertToSupportedValueType(value);

            // Coerce enums to their underlying integral primitive so the
            // switch below picks up the right typed-field constructor.
            if (value != null && value.GetType().IsEnum)
            {
                value = Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()));
            }

            var fieldStore = store == StoreMode.Yes ? Field.Store.YES : Field.Store.NO;
            Field numericField;

            // The range column holds the indexed number as a long or a double
            // (see RangeFacetValueType), whatever the width of the field itself.
            Field dvField = null;
            Field rangeDvField = null;
            switch (value)
            {
                case int i:
                    numericField = new Int32Field(fieldName, i, fieldStore);
                    if (docValues) dvField = new NumericDocValuesField(fieldName, i);
                    if (rangeField != null) rangeDvField = new NumericDocValuesField(rangeField, i);
                    break;
                case long l:
                    numericField = new Int64Field(fieldName, l, fieldStore);
                    if (docValues) dvField = new NumericDocValuesField(fieldName, l);
                    if (rangeField != null) rangeDvField = new NumericDocValuesField(rangeField, l);
                    break;
                case float f:
                    numericField = new SingleField(fieldName, f, fieldStore);
                    if (docValues) dvField = new SingleDocValuesField(fieldName, f);
                    if (rangeField != null) rangeDvField = new DoubleDocValuesField(rangeField, f);
                    break;
                case double d:
                    numericField = new DoubleField(fieldName, d, fieldStore);
                    if (docValues) dvField = new DoubleDocValuesField(fieldName, d);
                    if (rangeField != null) rangeDvField = new DoubleDocValuesField(rangeField, d);
                    break;
                default:
                    throw new ArgumentException("Unable to store ValueType " + value.GetType() + " as a numeric field.", nameof(source));
            }

            // In Lucene 4.8 boost on numeric fields is no longer supported
            // (norms are required for boost, and numeric fields don't index
            // norms). Boost is silently dropped if not the default.
            target.Add(numericField);
            if (dvField != null) target.Add(dvField);
            if (rangeDvField != null) target.Add(rangeDvField);
        }

        public override string ConvertToQueryExpression(object value)
        {
            value = ConvertToSupportedValueType(value);

            return ((ValueType) value).ToPrefixCoded();
        }

        public override string EscapeSpecialCharacters(string value)
        {
            // no need to escape since value will not be parsed.
            return value;
        }

        public override Query CreateQuery(string pattern)
        {
            if (pattern == "*")
            {
                return base.CreateQuery(pattern);
            }

            return new TermQuery(new Term(FieldName, pattern));
        }

        public override Query CreateRangeQuery(object lowerBound, object upperBound, RangeType lowerRange, RangeType upperRange)
        {
            if (lowerBound != null && !propertyInfo.PropertyType.IsInstanceOfType(lowerBound))
            {
                lowerBound = ConvertToSupportedValueType(lowerBound);
            }
            if (upperBound != null && !propertyInfo.PropertyType.IsInstanceOfType(upperBound))
            {
                upperBound = ConvertToSupportedValueType(upperBound);
            }
            return NumericRangeUtils.CreateNumericRangeQuery(fieldName, (ValueType)lowerBound, (ValueType)upperBound, lowerRange, upperRange);
        }

        private object ConvertToSupportedValueType(object value)
        {
            if (value is string && (string) value == "*") return null;

            var propertyType = propertyInfo.PropertyType.GetUnderlyingType();

            if (typeToValueTypeConverter == null)
            {
                return Convert.ChangeType(value, propertyType);
            }

            var type = GetUnderlyingValueType();

            if (!typeToValueTypeConverter.CanConvertFrom(null, value.GetType()))
            {
                value = Convert.ChangeType(value, propertyType);
            }

            return type != null ? typeToValueTypeConverter.ConvertTo(value, type) : value;
        }

        private Type GetUnderlyingValueType()
        {
            return supportedValueTypes.FirstOrDefault(t => typeToValueTypeConverter.CanConvertTo(t));
        }
    }
}
