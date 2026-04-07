// Compatibility shims that recreate re-linq 1.x types/method names against
// re-linq 2.x. The original Lucene.Net.Linq tree-visitor and custom-expression
// classes were written for re-linq 1.13, where ExpressionTreeVisitor /
// ExtensionExpression / RegistryBase existed in these namespaces. In re-linq
// 2.x they are gone — visitors now derive from System.Linq.Expressions.ExpressionVisitor
// (via Remotion.Linq.Parsing.RelinqExpressionVisitor) and use the BCL Visit*
// method names. Rather than touch ~50 files individually we provide a thin
// adapter layer in the original namespaces that delegates between the old and
// new APIs. This file is the only place where the old/new mapping lives; the
// rest of the codebase continues to use the legacy API.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

// ---------------------------------------------------------------------------
// Remotion.Linq.Clauses.Expressions.ExtensionExpression  (re-linq 1.x)
// ---------------------------------------------------------------------------
namespace Remotion.Linq.Clauses.Expressions
{
    internal abstract class ExtensionExpression : Expression
    {
        private readonly Type _type;

        protected ExtensionExpression(Type type, ExpressionType nodeType)
        {
            _type = type;
            // The integer NodeType passed by callers (LuceneExpressionType.*) is
            // ignored: re-linq 2.x and System.Linq.Expressions require custom
            // expressions to report ExpressionType.Extension. The Lucene.Net.Linq
            // codebase dispatches by C# type, not by NodeType, so this is safe.
        }

        public sealed override Type Type => _type;
        public sealed override ExpressionType NodeType => ExpressionType.Extension;
        public override bool CanReduce => false;
    }
}

// ---------------------------------------------------------------------------
// Remotion.Linq.Parsing.ExpressionTreeVisitor  (re-linq 1.x)
// ---------------------------------------------------------------------------
namespace Remotion.Linq.Parsing
{
    internal abstract class ExpressionTreeVisitor : RelinqExpressionVisitor
    {
        // Legacy entry point.
        public Expression VisitExpression(Expression expression) => Visit(expression);

        // -------- Custom-expression dispatch --------
        protected sealed override Expression VisitExtension(Expression node)
        {
            if (node is ExtensionExpression ext) return VisitExtensionExpression(ext);
            return base.VisitExtension(node);
        }

        protected virtual Expression VisitExtensionExpression(ExtensionExpression expression)
            => base.VisitExtension(expression);

        // -------- Renamed BCL ExpressionVisitor methods --------
        protected sealed override Expression VisitBinary(BinaryExpression node) => VisitBinaryExpression(node);
        protected virtual Expression VisitBinaryExpression(BinaryExpression node) => base.VisitBinary(node);

        protected sealed override Expression VisitMethodCall(MethodCallExpression node) => VisitMethodCallExpression(node);
        protected virtual Expression VisitMethodCallExpression(MethodCallExpression node) => base.VisitMethodCall(node);

        protected sealed override Expression VisitMember(MemberExpression node) => VisitMemberExpression(node);
        protected virtual Expression VisitMemberExpression(MemberExpression node) => base.VisitMember(node);

        protected sealed override Expression VisitConstant(ConstantExpression node) => VisitConstantExpression(node);
        protected virtual Expression VisitConstantExpression(ConstantExpression node) => base.VisitConstant(node);

        protected sealed override Expression VisitUnary(UnaryExpression node) => VisitUnaryExpression(node);
        protected virtual Expression VisitUnaryExpression(UnaryExpression node)
        {
            // Stage 6 port: re-linq 1.x's ExpressionTreeVisitor recreated
            // unary nodes via Expression.MakeUnary without going through the
            // BCL ExpressionVisitor's Update + ValidateUnary path. The BCL
            // validation throws when a transforming visitor (such as
            // NoOpConvertExpressionRemovingVisitor) replaces an operand with
            // one of a different type. We bypass that by reconstructing
            // manually here, the way re-linq 1.x did.
            var newOperand = Visit(node.Operand);
            if (ReferenceEquals(newOperand, node.Operand)) return node;
            try
            {
                return Expression.MakeUnary(node.NodeType, newOperand, node.Type, node.Method);
            }
            catch (System.InvalidOperationException)
            {
                // MakeUnary itself rejected the rewrite; fall back to the
                // original to keep the visitor pipeline running.
                return node;
            }
        }

        protected sealed override Expression VisitConditional(ConditionalExpression node) => VisitConditionalExpression(node);
        protected virtual Expression VisitConditionalExpression(ConditionalExpression node) => base.VisitConditional(node);

        protected sealed override Expression VisitNew(NewExpression node) => VisitNewExpression(node);
        protected virtual Expression VisitNewExpression(NewExpression node) => base.VisitNew(node);

        protected sealed override Expression VisitParameter(ParameterExpression node) => VisitParameterExpression(node);
        protected virtual Expression VisitParameterExpression(ParameterExpression node) => base.VisitParameter(node);

        protected sealed override Expression VisitInvocation(InvocationExpression node) => VisitInvocationExpression(node);
        protected virtual Expression VisitInvocationExpression(InvocationExpression node) => base.VisitInvocation(node);

        protected sealed override Expression VisitTypeBinary(TypeBinaryExpression node) => VisitTypeBinaryExpression(node);
        protected virtual Expression VisitTypeBinaryExpression(TypeBinaryExpression node) => base.VisitTypeBinary(node);

        protected sealed override Expression VisitNewArray(NewArrayExpression node) => VisitNewArrayExpression(node);
        protected virtual Expression VisitNewArrayExpression(NewArrayExpression node) => base.VisitNewArray(node);

        protected sealed override Expression VisitMemberInit(MemberInitExpression node) => VisitMemberInitExpression(node);
        protected virtual Expression VisitMemberInitExpression(MemberInitExpression node) => base.VisitMemberInit(node);

        protected sealed override Expression VisitListInit(ListInitExpression node) => VisitListInitExpression(node);
        protected virtual Expression VisitListInitExpression(ListInitExpression node) => base.VisitListInit(node);

        // -------- re-linq-specific node types --------
        protected sealed override Expression VisitQuerySourceReference(QuerySourceReferenceExpression expression)
            => VisitQuerySourceReferenceExpression(expression);
        protected virtual Expression VisitQuerySourceReferenceExpression(QuerySourceReferenceExpression expression)
            => base.VisitQuerySourceReference(expression);

        protected sealed override Expression VisitSubQuery(SubQueryExpression expression)
            => VisitSubQueryExpression(expression);
        protected virtual Expression VisitSubQueryExpression(SubQueryExpression expression)
            => base.VisitSubQuery(expression);
    }
}

// ---------------------------------------------------------------------------
// Remotion.Linq.Utilities.ReflectionUtility  (re-linq 1.x helper, removed in 2.x)
// ---------------------------------------------------------------------------
namespace Remotion.Linq.Utilities
{
    internal static class ReflectionUtility
    {
        public static MethodInfo GetMethod<T>(Expression<Func<T>> wrappedCall)
        {
            return ((MethodCallExpression)wrappedCall.Body).Method;
        }

        public static MethodInfo GetMethod(Expression<Action> wrappedCall)
        {
            return ((MethodCallExpression)wrappedCall.Body).Method;
        }
    }
}

// ---------------------------------------------------------------------------
// Remotion.Linq.Utilities.RegistryBase<,,>  (re-linq 1.x)
// ---------------------------------------------------------------------------
namespace Remotion.Linq.Utilities
{
    internal abstract class RegistryBase<TRegistry, TKey, TItem>
        where TRegistry : RegistryBase<TRegistry, TKey, TItem>, new()
    {
        private readonly Dictionary<TKey, TItem> _items = new Dictionary<TKey, TItem>();

        public static TRegistry CreateDefault()
        {
            var registry = new TRegistry();
            var itemTypes = new List<Type>();
            foreach (var t in typeof(TRegistry).Assembly.GetTypes())
            {
                if (!t.IsClass || t.IsAbstract) continue;
                if (!typeof(TItem).IsAssignableFrom(t)) continue;
                itemTypes.Add(t);
            }
            registry.RegisterForTypes(itemTypes);
            return registry;
        }

        protected abstract void RegisterForTypes(IEnumerable<Type> itemTypes);

        public void Register(IEnumerable<TKey> keys, TItem item)
        {
            foreach (var key in keys) _items[key] = item;
        }

        public TItem GetItemExact(TKey key)
            => _items.TryGetValue(key, out var v) ? v : default;

        public abstract TItem GetItem(TKey key);
    }
}
