using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Lucene.Net.Linq.Clauses.Expressions
{
    internal class LuceneCompositeOrderingExpression : Expression
    {
        private readonly IEnumerable<LuceneQueryFieldExpression> fields;

        public LuceneCompositeOrderingExpression(IEnumerable<LuceneQueryFieldExpression> fields)
        {
            this.fields = fields;
        }

        public override ExpressionType NodeType => ExpressionType.Extension;
        public override Type Type => typeof(object);
        public override bool CanReduce => false;

        public IEnumerable<LuceneQueryFieldExpression> Fields
        {
            get { return fields; }
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            return this;
        }
    }
}
