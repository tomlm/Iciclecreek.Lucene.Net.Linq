using System;
using System.ComponentModel;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;

namespace Lucene.Net.Linq.Search
{
    /// <summary>
    /// <see cref="FieldComparerSource"/> for properties whose <see cref="Type"/>
    /// implements only the non-generic <see cref="IComparable"/> interface, and
    /// is converted to/from string by an associated <see cref="TypeConverter"/>.
    /// This is the fallback sort path used when <c>DocValues=false</c> on the
    /// field attribute. Setting <c>DocValues=true</c> bypasses this comparator
    /// entirely in favor of a typed <see cref="SortField"/> reading from the
    /// SortedDocValues column — but only when byte-wise ordering of the
    /// converter's string output matches the type's intended ordering.
    /// </summary>
    internal class NonGenericConvertableFieldComparatorSource : FieldComparerSource
    {
        private readonly Type type;
        private readonly TypeConverter converter;

        public NonGenericConvertableFieldComparatorSource(Type type, TypeConverter converter)
        {
            this.type = type;
            this.converter = converter;
        }

        public override FieldComparer NewComparer(string fieldname, int numHits, int sortPos, bool reversed)
        {
            var comparerType = typeof(NonGenericConvertableFieldComparer<>).MakeGenericType(type);
            return (FieldComparer)Activator.CreateInstance(comparerType, numHits, fieldname, converter);
        }

        public class NonGenericConvertableFieldComparer<TComparable> : FieldComparer<TComparable>
            where TComparable : class, IComparable
        {
            private readonly TComparable[] values;
            private readonly string field;
            private readonly TypeConverter converter;
            private BinaryDocValues currentReaderValues;
            private TComparable bottom;
            private TComparable topValue;

            public NonGenericConvertableFieldComparer(int numHits, string field, TypeConverter converter)
            {
                this.values = new TComparable[numHits];
                this.field = field;
                this.converter = converter;
            }

            public override int Compare(int slot1, int slot2)
                => CompareValues(values[slot1], values[slot2]);

            public override void SetBottom(int slot) => bottom = values[slot];
            public override void SetTopValue(TComparable value) => topValue = value;

            public override int CompareBottom(int doc) => CompareValues(bottom, ReadValue(doc));
            public override int CompareTop(int doc) => CompareValues(topValue, ReadValue(doc));

            public override void Copy(int slot, int doc) => values[slot] = ReadValue(doc);

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                currentReaderValues = FieldCache.DEFAULT.GetTerms(context.AtomicReader, field, false);
                return this;
            }

            public override TComparable this[int slot] => values[slot];

            private TComparable ReadValue(int doc)
            {
                var bytes = new BytesRef();
                currentReaderValues.Get(doc, bytes);
                if (bytes.Length == 0) return default;
                var str = bytes.Utf8ToString();
                return (TComparable)converter.ConvertFrom(str);
            }

            public override int CompareValues(TComparable first, TComparable second)
            {
                if (first == null && second == null) return 0;
                if (first == null) return -1;
                if (second == null) return 1;
                return first.CompareTo(second);
            }
        }
    }
}
