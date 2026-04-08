using System;
using System.Linq.Expressions;
using Lucene.Net.Search;

namespace Lucene.Net.Linq.Clauses.Expressions
{
    internal class LuceneQueryExpression : Expression
    {
        private readonly Query query;

        internal LuceneQueryExpression(Query query)
        {
            this.query = query;
        }

        public override ExpressionType NodeType => ExpressionType.Extension;
        public override Type Type => typeof(Query);
        public override bool CanReduce => false;

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            // no children.
            return this;
        }

        public Query Query
        {
            get { return query; }
        }
    }
}
