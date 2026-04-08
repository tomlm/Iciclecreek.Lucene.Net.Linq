using System;
using System.Collections.Generic;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Linq.Search;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Version = Lucene.Net.Util.LuceneVersion;

namespace Lucene.Net.Linq
{
    public class FieldMappingQueryParser<T> : QueryParser
    {
        private readonly Version matchVersion;
        private readonly IDocumentMapper<T> mapper;
        private readonly string initialDefaultField;
        private static readonly string DefaultField = typeof(FieldMappingQueryParser<T>).FullName + ".DEFAULT_FIELD";

        public FieldMappingQueryParser(Version matchVersion, IDocumentMapper<T> mapper)
            : base(matchVersion, DefaultField, mapper.Analyzer)
        {
            this.initialDefaultField = DefaultField;
            this.matchVersion = matchVersion;
            this.mapper = mapper;
            this.DefaultSearchProperty = DefaultField;
        }

        public FieldMappingQueryParser(Version matchVersion, string defaultSearchField, IDocumentMapper<T> mapper)
            : base(matchVersion, defaultSearchField, mapper.Analyzer)
        {
            this.initialDefaultField = defaultSearchField;
            this.DefaultSearchProperty = defaultSearchField;
            this.matchVersion = matchVersion;
            this.mapper = mapper;
        }

        /// <summary>
        /// Default property for queries that don't specify which field to search.
        /// For an example query like <c>Lucene OR NuGet</c>, if this property is
        /// set to <c>SearchText</c>, it will produce a query like
        /// <c>SearchText:Lucene OR SearchText:NuGet</c>.
        /// </summary>
        public string DefaultSearchProperty { get; set; }

        public Version MatchVersion => matchVersion;

        public IDocumentMapper<T> DocumentMapper => mapper;

        public override string Field => DefaultSearchProperty;

        protected override Query GetFieldQuery(string field, string queryText, bool quoted)
        {
            var mapping = GetMapping(field);

            try
            {
                var codedQueryText = mapping.ConvertToQueryExpression(queryText);
                return mapping.CreateQuery(codedQueryText);
            }
            catch (Exception ex)
            {
                throw new ParseException(ex.Message, ex);
            }
        }

        protected override Query GetRangeQuery(string field, string part1, string part2, bool startInclusive, bool endInclusive)
        {
            var lowerRangeType = startInclusive ? RangeType.Inclusive : RangeType.Exclusive;
            var upperRangeType = endInclusive ? RangeType.Inclusive : RangeType.Exclusive;
            var mapping = GetMapping(field);
            try
            {
                return mapping.CreateRangeQuery(part1, part2, lowerRangeType, upperRangeType);
            }
            catch (Exception ex)
            {
                throw new ParseException(ex.Message, ex);
            }
        }

        protected override Query GetFieldQuery(string field, string queryText, int slop)
        {
            return base.GetFieldQuery(OverrideField(field), queryText, slop);
        }

        protected override Query GetWildcardQuery(string field, string termStr)
        {
            return base.GetWildcardQuery(OverrideField(field), termStr);
        }

        protected override Query GetPrefixQuery(string field, string termStr)
        {
            return base.GetPrefixQuery(OverrideField(field), termStr);
        }

        protected override Query GetFuzzyQuery(string field, string termStr, float minSimilarity)
        {
            return base.GetFuzzyQuery(OverrideField(field), termStr, minSimilarity);
        }

        private string OverrideField(string field)
        {
            if (field == initialDefaultField)
            {
                field = DefaultSearchProperty;
            }
            return field;
        }

        protected virtual IFieldMappingInfo GetMapping(string field)
        {
            field = OverrideField(field);

            try
            {
                return mapper.GetMappingInfo(field);
            }
            catch (KeyNotFoundException)
            {
                throw new ParseException("Unrecognized field: '" + field + "'");
            }
        }
    }
}
