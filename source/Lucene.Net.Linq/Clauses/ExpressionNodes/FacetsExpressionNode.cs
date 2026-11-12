using System.Linq.Expressions;
using System.Reflection;
using Remotion.Linq;
using Remotion.Linq.Parsing.Structure.IntermediateModel;

namespace Lucene.Net.Linq.Clauses.ExpressionNodes
{
    internal class FacetsExpressionNode : MethodCallExpressionNodeBase
    {
        public static readonly MethodInfo[] SupportedMethods =
        {
            Lucene.Net.Linq.Util.Reflection.MethodOf(() => LuceneMethods.ApplyFacets<object>(null, null))
        };

        private readonly ConstantExpression facetQuery;

        public FacetsExpressionNode(MethodCallExpressionParseInfo parseInfo, ConstantExpression facetQuery)
            : base(parseInfo)
        {
            this.facetQuery = facetQuery;
        }

        public override Expression Resolve(ParameterExpression inputParameter, Expression expressionToBeResolved, ClauseGenerationContext clauseGenerationContext)
        {
            return Source.Resolve(inputParameter, expressionToBeResolved, clauseGenerationContext);
        }

        protected override void ApplyNodeSpecificSemantics(QueryModel queryModel, ClauseGenerationContext clauseGenerationContext)
        {
            queryModel.BodyClauses.Add(new FacetsClause(facetQuery));
        }
    }
}
