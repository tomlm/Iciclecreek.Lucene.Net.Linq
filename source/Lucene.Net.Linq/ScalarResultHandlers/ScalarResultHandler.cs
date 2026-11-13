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
            // Rows left after Skip, capped by Take.
            return Math.Min(Math.Max(hits.TotalHits - luceneQueryModel.SkipResults, 0), luceneQueryModel.MaxResults);
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