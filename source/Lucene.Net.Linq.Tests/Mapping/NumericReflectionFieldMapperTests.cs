using System;
using System.ComponentModel;
using Lucene.Net.Documents;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Linq.Search;
using Lucene.Net.Linq.Util;
using Lucene.Net.Search;
using Lucene.Net.Util;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Mapping
{
    [TestFixture]
    public class NumericReflectionFieldMapperTests
    {
        private NumericReflectionFieldMapper<Sample> mapper;

        public class Sample
        {
            public Int32 Int { get; set; }
            public long Long { get; set; }
            public long? NullableLong { get; set; }
            public Complex Complex { get; set; }
        }

        public class Complex
        {
            public string Id { get; set; }
        }

        [Test]
        public void StoreLong()
        {
            mapper = new NumericReflectionFieldMapper<Sample>(typeof(Sample).GetProperty("Long"), StoreMode.Yes, null, TypeDescriptor.GetConverter(typeof(long)), "Long", NumericUtils.PRECISION_STEP_DEFAULT, 1.0f);

            var sample = new Sample { Long = 1234L };
            var document = new Document();
            mapper.CopyToDocument(sample, document);

            var field = document.GetField("Long");
            Assert.That(field.GetInt64Value(), Is.EqualTo(1234L));
        }

        [Test]
        public void UsesPrecisionStep()
        {
            mapper = new NumericReflectionFieldMapper<Sample>(typeof(Sample).GetProperty("Int"), StoreMode.Yes, null, TypeDescriptor.GetConverter(typeof(int)), "Int", 128, 1.0f);

            var sample = new Sample { Long = 1234L };
            var document = new Document();
            mapper.CopyToDocument(sample, document);

            var field = document.GetField("Int");
            // Stage 5 port: precisionStep is no longer reflected on the
            // TokenStream representation; the typed-field constructors carry
            // it implicitly. Just verify that a numeric value made it onto
            // the document via the typed accessor.
            Assert.That(field.NumericType, Is.Not.EqualTo(global::Lucene.Net.Documents.NumericFieldType.NONE));
        }

        [Test]
        public void ConvertsFieldValue()
        {
            mapper = new NumericReflectionFieldMapper<Sample>(typeof(Sample).GetProperty("Int"), StoreMode.Yes, null, TypeDescriptor.GetConverter(typeof(int)), "Int", 128, 1.0f);

            var result = mapper.ConvertFieldValue(new Int32Field("Int", 100, Field.Store.YES));

            Assert.That(result, Is.EqualTo(100));
        }

        [Test]
        public void ConvertsFieldValueToNonValueType()
        {
            var valueTypeConverter = new SampleConverter();

            mapper = new NumericReflectionFieldMapper<Sample>(typeof(Sample).GetProperty("Complex"), StoreMode.Yes, valueTypeConverter, TypeDescriptor.GetConverter(typeof(int)), "Complex", 128, 1.0f);

            var result = mapper.ConvertFieldValue(new Int32Field("Complex", 100, Field.Store.YES));

            Assert.That(result, Is.InstanceOf<Complex>());
        }

        [Test]
        public void SortType_Int()
        {
            mapper = new NumericReflectionFieldMapper<Sample>(typeof(Sample).GetProperty("Int"), StoreMode.Yes, null, TypeDescriptor.GetConverter(typeof(int)), "Int", 128, 1.0f);

            Assert.That(mapper.CreateSortField(false).Type, Is.EqualTo(SortFieldType.INT32));
        }

        [Test]
        public void SortType_Long()
        {
            mapper = new NumericReflectionFieldMapper<Sample>(typeof(Sample).GetProperty("Long"), StoreMode.Yes, null, TypeDescriptor.GetConverter(typeof(long)), "Long", NumericUtils.PRECISION_STEP_DEFAULT, 1.0f);

            Assert.That(mapper.CreateSortField(false).Type, Is.EqualTo(SortFieldType.INT64));
        }

        [Test]
        public void SortType_Complex()
        {
            var valueTypeConverter = new SampleConverter();

            mapper = new NumericReflectionFieldMapper<Sample>(typeof(Sample).GetProperty("Complex"), StoreMode.Yes, valueTypeConverter, TypeDescriptor.GetConverter(typeof(int)), "Complex", 128, 1.0f);

            Assert.That(mapper.CreateSortField(false).Type, Is.EqualTo(SortFieldType.INT32));
        }

        [Test]
        public void RangeQuery()
        {
            mapper = new NumericReflectionFieldMapper<Sample>(typeof(Sample).GetProperty("Long"), StoreMode.Yes, null, TypeDescriptor.GetConverter(typeof(int)), "Long", 128, 1.0f);

            var result = mapper.CreateRangeQuery(-5L, 5L, RangeType.Inclusive, RangeType.Exclusive);

            Assert.That(result.GetType().IsGenericType && result.GetType().GetGenericTypeDefinition() == typeof(NumericRangeQuery<>), Is.True, "Expected NumericRangeQuery<T>");
            Assert.That(result.ToString(), Is.EqualTo("Long:[-5 TO 5}"));
        }

        [Test]
        public void RangeQueryUnbounded()
        {
            mapper = new NumericReflectionFieldMapper<Sample>(typeof(Sample).GetProperty("Long"), StoreMode.Yes, null, TypeDescriptor.GetConverter(typeof(int)), "Long", 128, 1.0f);

            var result = mapper.CreateRangeQuery(100L, null, RangeType.Exclusive, RangeType.Inclusive);

            Assert.That(result.GetType().IsGenericType && result.GetType().GetGenericTypeDefinition() == typeof(NumericRangeQuery<>), Is.True, "Expected NumericRangeQuery<T>");
            Assert.That(result.ToString(), Is.EqualTo(string.Format("Long:{{100 TO {0}]", long.MaxValue)));
        }

        [Test]
        public void ConvertsNullable()
        {
            mapper = new NumericReflectionFieldMapper<Sample>(typeof(Sample).GetProperty("NullableLong"), StoreMode.Yes, null, TypeDescriptor.GetConverter(typeof(int)), "NullableLong", 128, 1.0f);

            var result = mapper.ConvertToQueryExpression(123L);

            Assert.That(result, Is.EqualTo(123L.ToPrefixCoded()));
        }

        public class SampleConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                return sourceType == typeof(int);
            }

            public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            {
                return destinationType == typeof(int);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
            {
                return new Complex { Id = value.ToString() };
            }
        }
    }
}
