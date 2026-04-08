using Lucene.Net.Linq.Mapping;
using Version = Lucene.Net.Util.LuceneVersion;

namespace Lucene.Net.Linq.Fluent
{
    internal class FluentDocumentMapper<T> : DocumentMapperBase<T>
    {
        public FluentDocumentMapper(Version version) : base(version)
        {
        }
    }
}