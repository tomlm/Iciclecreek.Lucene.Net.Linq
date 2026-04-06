using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Linq.Search;
using Lucene.Net.Linq.Util;
using Lucene.Net.Search;

namespace Lucene.Net.Linq.Mapping
{
    internal class NumericReflectionFieldMapper<T> : ReflectionFieldMapper<T>
    {
        private static readonly IEnumerable<Type> supportedValueTypes = new List<Type>{typeof(long), typeof(int), typeof(double), typeof(float)};

        private readonly TypeConverter typeToValueTypeConverter;
        private readonly int precisionStep;

        public NumericReflectionFieldMapper(PropertyInfo propertyInfo, StoreMode store, TypeConverter typeToValueTypeConverter, TypeConverter valueTypeToStringConverter, string field, int precisionStep, float boost)
            : base(propertyInfo, store, IndexMode.Analyzed, TermVectorMode.No, valueTypeToStringConverter, field, false, new KeywordAnalyzer(), boost)
        {
            this.typeToValueTypeConverter = typeToValueTypeConverter;
            this.precisionStep = precisionStep;
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
            var value = (object)null;

            // Numeric typed fields expose strongly-typed accessors; fall back
            // to the string representation if the field was indexed via the
            // legacy code path.
            if (field.NumericType != NumericFieldType.NONE)
            {
                value = field.GetNumericValue();
            }
            else
            {
                value = field.GetStringValue();
            }

            if (typeToValueTypeConverter != null)
            {
                value = typeToValueTypeConverter.ConvertFrom(value);
            }
            else if (value is string s)
            {
                // restore the boxed primitive
                var propType = propertyInfo.PropertyType.GetUnderlyingType();
                value = Convert.ChangeType(s, propType);
            }

            return value;
        }

        public override void CopyToDocument(T source, Document target)
        {
            var value = propertyGetter(source);

            target.RemoveFields(fieldName);

            if (value == null) return;

            value = ConvertToSupportedValueType(value);

            var fieldStore = store == StoreMode.Yes ? Field.Store.YES : Field.Store.NO;
            Field numericField;

            switch (value)
            {
                case int i:
                    numericField = new Int32Field(fieldName, i, fieldStore);
                    break;
                case long l:
                    numericField = new Int64Field(fieldName, l, fieldStore);
                    break;
                case float f:
                    numericField = new SingleField(fieldName, f, fieldStore);
                    break;
                case double d:
                    numericField = new DoubleField(fieldName, d, fieldStore);
                    break;
                default:
                    throw new ArgumentException("Unable to store ValueType " + value.GetType() + " as a numeric field.", nameof(source));
            }

            // In Lucene 4.8 boost on numeric fields is no longer supported
            // (norms are required for boost, and numeric fields don't index
            // norms). Boost is silently dropped if not the default.
            target.Add(numericField);
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
