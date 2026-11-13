using System.Linq.Expressions;
using Lucene.Net.Search;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Lucene.Net.Linq.Clauses
{
    /// <summary>
    /// A join that is executed inside Lucene: only documents whose
    /// <see cref="JoinFilter.ToField"/> holds a term found in
    /// <see cref="JoinFilter.FromField"/> of some document matching
    /// <see cref="JoinFilter.FromQuery"/> are kept. Stands in for a
    /// <see cref="JoinClause"/> whose other side the result does not use, or
    /// sits in front of one to narrow the outer side before an in-memory join.
    /// </summary>
    internal class JoinFilterClause : ExtensionClause<ConstantExpression>
    {
        public JoinFilterClause(JoinFilter joinFilter)
            : this(Expression.Constant(joinFilter))
        {
        }

        private JoinFilterClause(ConstantExpression expression)
            : base(expression)
        {
        }

        public JoinFilter JoinFilter
        {
            get { return (JoinFilter)expression.Value; }
        }

        protected override void Accept(ILuceneQueryModelVisitor visitor, QueryModel queryModel, int index)
        {
            visitor.VisitJoinFilterClause(this, queryModel, index);
        }

        public override IBodyClause Clone(CloneContext cloneContext)
        {
            return new JoinFilterClause(expression);
        }
    }

    /// <summary>
    /// The "from" side of a query-time join. The executor turns it into a
    /// filter once it holds a searcher, because building the join runs
    /// <see cref="FromQuery"/> against that searcher.
    /// </summary>
    internal sealed class JoinFilter
    {
        public JoinFilter(Query fromQuery, string fromField, string toField)
        {
            FromQuery = fromQuery;
            FromField = fromField;
            ToField = toField;
        }

        /// <summary>Matches the documents of the side that is not loaded.</summary>
        public Query FromQuery { get; }

        /// <summary>Key field of the side that is not loaded.</summary>
        public string FromField { get; }

        /// <summary>Key field of the side the query returns.</summary>
        public string ToField { get; }

        public override string ToString() => "join(" + FromField + " -> " + ToField + " where " + FromQuery + ")";
    }
}
