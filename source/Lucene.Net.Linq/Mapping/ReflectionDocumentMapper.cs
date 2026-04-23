using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Linq.Util;
using Microsoft.Extensions.AI;
using Version = Lucene.Net.Util.LuceneVersion;

namespace Lucene.Net.Linq.Mapping
{
    /// <summary>
    /// Maps public properties on <typeparamref name="T"/> to
    /// Lucene <see cref="Field"/>s using optional metadata
    /// attributes such as <see cref="FieldAttribute"/>,
    /// <see cref="NumericFieldAttribute"/>,
    /// <see cref="IgnoreFieldAttribute"/>,
    /// <see cref="DocumentKeyAttribute"/>
    /// and <see cref="QueryScoreAttribute"/>.
    /// </summary>
    public class ReflectionDocumentMapper<T> : DocumentMapperBase<T>
    {
        /// <summary>
        /// Constructs an instance that will create an <see cref="Analyzer"/>
        /// using metadata on public properties on the type <typeparamref name="T"/>.
        /// </summary>
        /// <param name="version">Version compatibility for analyzers and indexers.</param>
        public ReflectionDocumentMapper(Version version)
            : this(version, null)
        {
        }

        /// <summary>
        /// Constructs an instance with an externall supplied analyzer
        /// and the compatibility version of the index.
        /// </summary>
        /// <param name="version">Version compatibility for analyzers and indexers.</param>
        /// <param name="externalAnalyzer"></param>
        public ReflectionDocumentMapper(Version version, Analyzer externalAnalyzer)
            : this(version, externalAnalyzer, typeof(T))
        {
        }

        /// <summary>
        /// Constructs an instance with an externally supplied analyzer,
        /// version, and embedding generator for vector fields.
        /// </summary>
        public ReflectionDocumentMapper(Version version, Analyzer externalAnalyzer,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
            : this(version, externalAnalyzer, typeof(T), embeddingGenerator)
        {
        }

        private ReflectionDocumentMapper(Version version, Analyzer externalAnalyzer, Type type,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
            : base(version, externalAnalyzer)
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            BuildFieldMap(props, embeddingGenerator);

            BuildKeyFieldMap(type, props);

            BuildDefaultSearchProperty(props);
        }

        private ReflectionDocumentMapper(Version version, Analyzer externalAnalyzer, Type type)
            : base(version, externalAnalyzer)
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            BuildFieldMap(props);

            BuildKeyFieldMap(type, props);

            BuildDefaultSearchProperty(props);
        }

        private void BuildFieldMap(IEnumerable<PropertyInfo> props,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
        {
            foreach (var p in props)
            {
                if (p.GetCustomAttribute<IgnoreFieldAttribute>(true) != null)
                {
                    continue;
                }
                var mappingContext = FieldMappingInfoBuilder.Build<T>(p, version, externalAnalyzer, embeddingGenerator);

                fieldMap.Add(mappingContext.PropertyName, mappingContext);

                if (!string.IsNullOrWhiteSpace(mappingContext.FieldName) && mappingContext.Analyzer != null)
                {
                    analyzer.AddAnalyzer(mappingContext.FieldName, mappingContext.Analyzer);
                }
            }
        }

        private void BuildFieldMap(IEnumerable<PropertyInfo> props)
        {
            foreach (var p in props)
            {
                if (p.GetCustomAttribute<IgnoreFieldAttribute>(true) != null)
                {
                    continue;
                }
                var mappingContext = FieldMappingInfoBuilder.Build<T>(p, version, externalAnalyzer, null);

                fieldMap.Add(mappingContext.PropertyName, mappingContext);

                if (!string.IsNullOrWhiteSpace(mappingContext.FieldName) && mappingContext.Analyzer != null)
                {
                    analyzer.AddAnalyzer(mappingContext.FieldName, mappingContext.Analyzer);
                }
            }
        }

        private void BuildKeyFieldMap(Type type, IEnumerable<PropertyInfo> props)
        {
            var keyProps = from p in props
                           let a = p.GetCustomAttribute<BaseFieldAttribute>(true)
                           where a != null && a.Key
                           select p;

            keyFields.AddRange(keyProps.Select(kp => fieldMap[kp.Name]));

            foreach (var attr in type.GetCustomAttributes<DocumentKeyAttribute>(true))
            {
                AddKeyField(new DocumentKeyFieldMapper<T>(attr.FieldName, attr.Value));
            }
        }

        private void BuildDefaultSearchProperty(IEnumerable<PropertyInfo> props)
        {
            var defaultProp = props.FirstOrDefault(p =>
            {
                var a = p.GetCustomAttribute<BaseFieldAttribute>(true);
                return a != null && a.Default;
            });

            if (defaultProp != null)
            {
                DefaultSearchProperty = defaultProp.Name;
            }
        }
    }
}
