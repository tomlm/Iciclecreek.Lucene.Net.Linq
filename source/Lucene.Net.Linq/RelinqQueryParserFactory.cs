using Lucene.Net.Linq.Clauses.ExpressionNodes;
using Lucene.Net.Linq.Transformation;
using Remotion.Linq.Parsing.ExpressionVisitors.Transformation;
using Remotion.Linq.Parsing.Structure;
using Remotion.Linq.Parsing.Structure.ExpressionTreeProcessors;
using Remotion.Linq.Parsing.Structure.NodeTypeProviders;

namespace Lucene.Net.Linq
{
    internal static class RelinqQueryParserFactory
    {
        internal static QueryParser CreateQueryParser()
        {
            var expressionTreeParser = new ExpressionTreeParser(
                CreateNodeTypeProvider(),
                CreateExpressionTreeProcessor());

            return new QueryParser(expressionTreeParser);
        }

        private static CompoundNodeTypeProvider CreateNodeTypeProvider()
        {
            // Manually register Lucene.Net.Linq's custom expression nodes.
            // re-linq 2.x removed the assembly-scanning convenience overload
            // (MethodInfoBasedNodeTypeRegistry.CreateFromTypes), so we declare
            // the mapping here. Add to this list when introducing new nodes.
            var registry = new MethodInfoBasedNodeTypeRegistry();
            registry.Register(OpenGenericMethods(BoostExpressionNode.SupportedMethods), typeof(BoostExpressionNode));
            registry.Register(OpenGenericMethods(QueryStatisticsCallbackExpressionNode.SupportedMethods), typeof(QueryStatisticsCallbackExpressionNode));
            registry.Register(OpenGenericMethods(TrackRetrievedDocumentsExpressionNode.SupportedMethods), typeof(TrackRetrievedDocumentsExpressionNode));

            var nodeTypeProvider = ExpressionTreeParser.CreateDefaultNodeTypeProvider();
            nodeTypeProvider.InnerProviders.Add(registry);

            return nodeTypeProvider;
        }

        // re-linq 2.x's MethodInfoBasedNodeTypeRegistry requires open generic
        // method definitions; the SupportedMethods arrays on our expression
        // nodes use closed-generic placeholder calls (LuceneMethods.Boost<object>)
        // which we have to unwrap before registering.
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodInfo> OpenGenericMethods(System.Reflection.MethodInfo[] methods)
        {
            foreach (var m in methods)
            {
                yield return m.IsGenericMethod ? m.GetGenericMethodDefinition() : m;
            }
        }

        /// <summary>
        /// Creates an <c cref="IExpressionTreeProcessor"/> that will execute
        /// <c cref="AllowSpecialCharactersExpressionTransformer"/>
        /// before executing <c cref="PartialEvaluatingExpressionTreeProcessor"/>
        /// and other default processors. 
        /// </summary>
        internal static IExpressionTreeProcessor CreateExpressionTreeProcessor()
        {
            var firstRegistry = new ExpressionTransformerRegistry();
            firstRegistry.Register(new AllowSpecialCharactersExpressionTransformer());

            var processor = ExpressionTreeParser.CreateDefaultProcessor(ExpressionTransformerRegistry.CreateDefault());
            processor.InnerProcessors.Insert(0, new TransformingExpressionTreeProcessor(firstRegistry));
            return processor;
        }
    }
}