using System.Linq.Expressions;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Lucene.Net.Linq.Clauses
{
    internal class FacetsClause : ExtensionClause<ConstantExpression>
    {
        public FacetsClause(ConstantExpression expression)
            : base(expression)
        {
        }

        public FacetQuery FacetQuery
        {
            get { return (FacetQuery)expression.Value; }
        }

        protected override void Accept(ILuceneQueryModelVisitor visitor, QueryModel queryModel, int index)
        {
            visitor.VisitFacetsClause(this, queryModel, index);
        }

        public override IBodyClause Clone(CloneContext cloneContext)
        {
            return new FacetsClause(expression);
        }
    }
}
