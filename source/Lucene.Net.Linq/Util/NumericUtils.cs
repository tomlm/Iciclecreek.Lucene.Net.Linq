using System;
using Lucene.Net.Documents;
using Lucene.Net.Linq.Search;
using Lucene.Net.Search;
using Lucene.Net.Util;

namespace Lucene.Net.Linq.Util
{
    internal static class NumericRangeUtils
    {
        internal static Query CreateNumericRangeQuery(string fieldName, ValueType lowerBound, ValueType upperBound, RangeType lowerRange, RangeType upperRange)
        {
            if (lowerBound == null && upperBound == null)
            {
                throw new ArgumentException("lowerBound and upperBound may not both be null.");
            }

            if (lowerBound == null)
            {
                lowerBound = (ValueType) upperBound.GetType().GetField("MinValue").GetValue(null);
            }
            else if (upperBound == null)
            {
                upperBound = (ValueType) lowerBound.GetType().GetField("MaxValue").GetValue(null);
            }

            if (lowerBound.GetType() != upperBound.GetType())
            {
                throw new ArgumentException("Cannot compare different value types " + lowerBound.GetType() + " and " + upperBound.GetType());
            }

            lowerBound = ToNumericFieldValue(lowerBound);
            upperBound = ToNumericFieldValue(upperBound);

            var minInclusive = lowerRange == RangeType.Inclusive;
            var maxInclusive = upperRange == RangeType.Inclusive;

            if (lowerBound is int)
            {
                return NumericRangeQuery.NewInt32Range(fieldName, (int)lowerBound, (int)upperBound, minInclusive, maxInclusive);
            }
            if (lowerBound is long)
            {
                return NumericRangeQuery.NewInt64Range(fieldName, (long)lowerBound, (long)upperBound, minInclusive, maxInclusive);
            }
            if (lowerBound is float)
            {
                return NumericRangeQuery.NewSingleRange(fieldName, (float)lowerBound, (float)upperBound, minInclusive, maxInclusive);
            }
            if (lowerBound is double)
            {
                return NumericRangeQuery.NewDoubleRange(fieldName, (double)lowerBound, (double)upperBound, minInclusive, maxInclusive);
            }

            throw new NotSupportedException("Unsupported numeric range type " + lowerBound.GetType());
        }

        /// <summary>
        /// Converts supported value types such as DateTime to an underlying ValueType that is supported by
        /// <c ref="NumericRangeQuery"/>.
        /// </summary>
        internal static ValueType ToNumericFieldValue(this ValueType value)
        {
            if (value is DateTime)
            {
                return ((DateTime)value).ToUniversalTime().Ticks;
            }
            if (value is DateTimeOffset)
            {
                return ((DateTimeOffset)value).Ticks;
            }

            return value;
        }

        internal static string ToPrefixCoded(this ValueType value)
        {
            if (value is int i)
            {
                var br = new BytesRef();
                NumericUtils.Int32ToPrefixCoded(i, 0, br);
                return br.Utf8ToString();
            }
            if (value is long l)
            {
                var br = new BytesRef();
                NumericUtils.Int64ToPrefixCoded(l, 0, br);
                return br.Utf8ToString();
            }
            if (value is double d)
            {
                var br = new BytesRef();
                NumericUtils.Int64ToPrefixCoded(NumericUtils.DoubleToSortableInt64(d), 0, br);
                return br.Utf8ToString();
            }
            if (value is float f)
            {
                var br = new BytesRef();
                NumericUtils.Int32ToPrefixCoded(NumericUtils.SingleToSortableInt32(f), 0, br);
                return br.Utf8ToString();
            }

            throw new NotSupportedException("ValueType " + value.GetType() + " not supported.");
        }
    }

    internal static class TypeExtensions
    {
        internal static SortFieldType ToSortFieldType(this Type valueType)
        {
            if (valueType == typeof(long))   return SortFieldType.INT64;
            if (valueType == typeof(int))    return SortFieldType.INT32;
            if (valueType == typeof(double)) return SortFieldType.DOUBLE;
            if (valueType == typeof(float))  return SortFieldType.SINGLE;
            return SortFieldType.STRING;
        }

        internal static Type GetUnderlyingType(this Type type)
        {
            return Nullable.GetUnderlyingType(type) ?? type;
        }
    }
}
