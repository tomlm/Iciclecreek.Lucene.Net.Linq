using System;
using System.ComponentModel;

namespace Lucene.Net.Linq.Search
{
    // See FieldComparator.cs for the TODO Lucene 4.8 port note. Reflection
    // mappers don't currently instantiate this; the converter-sort path is
    // disabled in ReflectionFieldMapper.CreateSortField.
    internal class GenericConvertableFieldComparatorSource
    {
        private readonly Type type;
        private readonly TypeConverter converter;

        public GenericConvertableFieldComparatorSource(Type type, TypeConverter converter)
        {
            this.type = type;
            this.converter = converter;
        }
    }
}
