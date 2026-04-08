using System;
using System.Linq.Expressions;

namespace Lucene.Net.Linq.Clauses.Expressions
{
    internal class LuceneOrderByRelevanceExpression : Expression
    {
        private static readonly LuceneOrderByRelevanceExpression instance = new LuceneOrderByRelevanceExpression();

        private LuceneOrderByRelevanceExpression()
        {
        }

        public override ExpressionType NodeType => ExpressionType.Extension;
        public override Type Type => typeof(object);
        public override bool CanReduce => false;

        public static Expression Instance
        {
            get { return instance; }
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            return this;
        }
    }
}
