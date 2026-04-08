using System;
using System.Collections.Generic;
using System.Reflection;
using Lucene.Net.Documents;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Search;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Mapping
{
    [TestFixture]
    public class FieldMappingInfoBuilderSortFieldTests
    {
        private PropertyInfo versionPropInfo;
        private PropertyInfo nativeVersionPropInfo;
        private Document document;

        [Field(Converter = typeof(VersionConverter))]
        public Version Version { get; set; }

        [Field(Converter = typeof(VersionConverter), NativeSort = true)]
        public Version NativeVersion { get; set; }

        [SetUp]
        public void SetUp()
        {
            versionPropInfo = GetType().GetProperty("Version");
            nativeVersionPropInfo = GetType().GetProperty("NativeVersion");
            document = new Document();
        }

        [Test]
        public void DefaultSortsByComplextType()
        {
            var result = CreateMapper(versionPropInfo).CreateSortField(reverse: false);

            // SortField.CUSTOM was removed in Lucene 4.8; a custom
            // FieldComparerSource now reports SortFieldType.CUSTOM via the
            // SortField (Type returns CUSTOM when ComparerSource is set).
            Assert.That(result.Type, Is.EqualTo(SortFieldType.CUSTOM));
            Assert.That(result.ComparerSource, Is.Not.Null);
        }

        [Test]
        public void SpecifyNativeSortUsesStringSort()
        {
            var result = CreateMapper(nativeVersionPropInfo).CreateSortField(reverse: false);

            Assert.That(result.Type, Is.EqualTo(SortFieldType.STRING));
        }
        
        private IFieldMapper<FieldMappingInfoBuilderSortFieldTests> CreateMapper(PropertyInfo propertyInfo)
        {
            return FieldMappingInfoBuilder.Build<FieldMappingInfoBuilderSortFieldTests>(propertyInfo);
        }
        
    }
}