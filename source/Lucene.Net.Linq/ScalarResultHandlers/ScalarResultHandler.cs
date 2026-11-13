using System;
using System.Collections.Generic;
using Lucene.Net.Search;
using Remotion.Linq.Clauses.ResultOperators;

namespace Lucene.Net.Linq.ScalarResultHandlers
{
    internal abstract class ScalarResultHandler
    {
        public abstract IEnumerable<Type> SupportedTypes { get; }

        public T Execute<T>(LuceneQueryModel luceneQueryModel, TopFieldDocs hits)
        {
            return (T) Convert.ChangeType(Execute(luceneQueryModel, hits), typeof (T));
        }

        protected abstract object Execute(LuceneQueryModel luceneQueryModel, TopFieldDocs hits);
    }

    internal class CountResultHandler : ScalarResultHandler
    {
        public override IEnumerable<Type> SupportedTypes
        {
            get { return new[] {typeof (CountResultOperator), typeof (LongCountResultOperator)}; }
        }

        protected override object Execute(LuceneQueryModel luceneQueryModel, TopFieldDocs hits)
        {
            // Rows left after Skip, capped by Take. A Skip that follows a Take
            // is also subtracted from MaxResults, which goes negative when it
            // skips more rows than were taken (Take(2).Skip(3)); that is no rows.
            return Math.Max(Math.Min(hits.TotalHits - luceneQueryModel.SkipResults, luceneQueryModel.MaxResults), 0);
        }
    }

    internal class AnyResultHandler : ScalarResultHandler
    {
        public override IEnumerable<Type> SupportedTypes
        {
            get { return new[] { typeof(AnyResultOperator) }; }
        }

        protected override object Execute(LuceneQueryModel luceneQueryModel, TopFieldDocs hits)
        {
            return hits.TotalHits - luceneQueryModel.SkipResults > 0;
        }
    }
}