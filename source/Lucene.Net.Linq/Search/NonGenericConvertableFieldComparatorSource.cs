using System;
using System.ComponentModel;

namespace Lucene.Net.Linq.Search
{
    // See FieldComparator.cs for the TODO Lucene 4.8 port note.
    internal class NonGenericConvertableFieldComparatorSource
    {
        private readonly Type type;
        private readonly TypeConverter converter;

        public NonGenericConvertableFieldComparatorSource(Type type, TypeConverter converter)
        {
            this.type = type;
            this.converter = converter;
        }
    }
}
