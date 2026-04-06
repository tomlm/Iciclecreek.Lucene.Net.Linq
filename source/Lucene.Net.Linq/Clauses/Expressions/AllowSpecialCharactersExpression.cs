using System.Linq.Expressions;
using Remotion.Linq.Clauses.Expressions;

namespace Lucene.Net.Linq.Clauses.Expressions
{
    internal class AllowSpecialCharactersExpression : ExtensionExpression
    {
        private readonly Expression pattern;

        internal AllowSpecialCharactersExpression(Expression pattern)
            : base(pattern.Type, (ExpressionType)LuceneExpressionType.AllowSpecialCharactersExpression)
        {
            this.pattern = pattern;
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            var newPattern = visitor.Visit(pattern);

            if (Equals(pattern, newPattern)) return this;

            return new AllowSpecialCharactersExpression(newPattern);
        }

        public Expression Pattern
        {
            get { return pattern; }
        }

        public override string ToString()
        {
            return pattern + ".AllowSpecialCharacters()";
        }
    }
}