using System;
using System.Linq.Expressions;

namespace Lucene.Net.Linq.Clauses.Expressions
{
    internal class LuceneQueryFieldExpression : Expression
    {
        private readonly Type type;
        private readonly string fieldName;

        internal LuceneQueryFieldExpression(Type type, string fieldName)
        {
            this.type = type;
            this.fieldName = fieldName;
            FieldBoost = 1;
        }

        public override ExpressionType NodeType => ExpressionType.Extension;
        public override Type Type => type;
        public override bool CanReduce => false;

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            // no children.
            return this;
        }

        public string FieldName { get { return fieldName; } }

        // Renamed from "Boost" to avoid C# overload-resolution collision
        // with the LuceneMethods.Boost<T>(this T, float) extension method,
        // which is in scope across the Lucene.Net.Linq namespace tree.
        public float FieldBoost { get; set; }

        public bool Equals(LuceneQueryFieldExpression other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(other.fieldName, fieldName);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != typeof(LuceneQueryFieldExpression)) return false;
            return Equals((LuceneQueryFieldExpression)obj);
        }

        public override int GetHashCode()
        {
            return (fieldName != null ? fieldName.GetHashCode() : 0);
        }

        public static bool operator ==(LuceneQueryFieldExpression left, LuceneQueryFieldExpression right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(LuceneQueryFieldExpression left, LuceneQueryFieldExpression right)
        {
            return !Equals(left, right);
        }

        public override string ToString()
        {
            var s = "LuceneField(" + fieldName + ")";
            if (Math.Abs(FieldBoost - 1.0f) > 0.01f)
            {
                return s + "^" + FieldBoost;
            }
            return s;
        }
    }
}
