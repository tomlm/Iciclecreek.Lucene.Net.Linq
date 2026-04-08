using System;
using System.Linq.Expressions;

namespace Lucene.Net.Linq.Clauses.Expressions
{
    internal class AllowSpecialCharactersExpression : Expression
    {
        private readonly Expression pattern;

        internal AllowSpecialCharactersExpression(Expression pattern)
        {
            this.pattern = pattern;
        }

        public override ExpressionType NodeType => ExpressionType.Extension;
        public override Type Type => pattern.Type;
        public override bool CanReduce => false;

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
