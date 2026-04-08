using System;
using System.Linq.Expressions;
using System.Reflection;
using Lucene.Net.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Util;

namespace Lucene.Net.Linq.Transformation.Visitors
{
    /// <summary>
    /// Replaces MemberExpression instances like [QuerySourceReferenceExpression].PropertyName with <c ref="LuceneQueryFieldExpression"/>
    /// </summary>
    internal class QuerySourceReferencePropertyTransformingVisitor : LuceneExpressionVisitor
    {
        private MemberExpression parent;
        private LuceneQueryFieldExpression queryField;

        protected override Expression VisitMember(MemberExpression expression)
        {
            parent = expression;
            
            var result = base.VisitMember(expression);

            return queryField ?? result;
        }

        protected override Expression VisitQuerySourceReference(QuerySourceReferenceExpression expression)
        {
            var propertyInfo = parent.Member as PropertyInfo;

            if (propertyInfo == null)
            {
                throw new NotSupportedException("Only MemberExpression of type PropertyInfo may be used on QuerySourceReferenceExpression.");
            }

            var propertyType = propertyInfo.PropertyType;
            if (propertyType.IsEnum)
            {
                propertyType = Enum.GetUnderlyingType(propertyType);
            }

            queryField = new LuceneQueryFieldExpression(propertyType, propertyInfo.Name);
            return base.VisitQuerySourceReference(expression);
        }
    }
}