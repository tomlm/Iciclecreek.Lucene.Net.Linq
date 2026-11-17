using System.Linq.Expressions;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Transformation.Visitors;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Transformation.Visitors
{
    [TestFixture]
    public class FlagToBinaryConditionVisitorTests
    {
        private FlagToBinaryConditionVisitor visitor;

        [SetUp]
        public void SetUp()
        {
            visitor = new FlagToBinaryConditionVisitor();
        }

        [Test]
        public void ConvertFlag()
        {
            // "where doc.SomeFlag"
            var expression = new LuceneQueryFieldExpression(typeof (bool), "SomeFlag");

            var result = visitor.Visit(expression) as BinaryExpression;

            Assert.That(result, Is.Not.Null, "Expected BinaryExpression to be returned.");
            Assert.That(result.Left, Is.SameAs(expression));
            Assert.That(result.Right, Is.InstanceOf<ConstantExpression>());
            Assert.That(((ConstantExpression)result.Right).Value, Is.EqualTo(true));
        }

        [Test]
        public void ConvertFlagNestedInCompoundBinaryLeft()
        {
            // where doc.SomeFlag && doc.Name == "foo"
            var flag = new LuceneQueryFieldExpression(typeof(bool), "SomeFlag");
            var binary = Expression.MakeBinary(ExpressionType.Equal, new LuceneQueryFieldExpression(typeof(string), "Name"), Expression.Constant("foo"));
            var topBinary = Expression.MakeBinary(ExpressionType.AndAlso, flag, binary);

            var result = visitor.Visit(topBinary) as BinaryExpression;

            Assert.That(result, Is.Not.Null, "Expected BinaryExpression to be returned.");
            Assert.That(result.Right, Is.SameAs(binary));
            Assert.That(result.Left, Is.InstanceOf<BinaryExpression>());
            Assert.That(((BinaryExpression)result.Left).Left, Is.SameAs(flag));
            Assert.That(((BinaryExpression)result.Left).Right, Is.InstanceOf<ConstantExpression>());
        }

        [Test]
        public void ConvertFlagNestedInCompoundBinaryRight()
        {
            // where doc.Name == "foo" || doc.SomeFlag
            var flag = new LuceneQueryFieldExpression(typeof(bool), "SomeFlag");
            var binary = Expression.MakeBinary(ExpressionType.Equal, new LuceneQueryFieldExpression(typeof(string), "Name"), Expression.Constant("foo"));
            var topBinary = Expression.MakeBinary(ExpressionType.OrElse, binary, flag);

            var result = visitor.Visit(topBinary) as BinaryExpression;

            Assert.That(result, Is.Not.Null, "Expected BinaryExpression to be returned.");
            Assert.That(result.Left, Is.SameAs(binary));
            Assert.That(result.Right, Is.InstanceOf<BinaryExpression>());
            Assert.That(((BinaryExpression)result.Right).Left, Is.SameAs(flag));
            Assert.That(((BinaryExpression)result.Right).Right, Is.InstanceOf<ConstantExpression>());
        }

        [Test]
        public void IgnoresWhenAlreadyInBinaryExpression()
        {
            // where doc.SomeFlag == true
            var flag = new LuceneQueryFieldExpression(typeof(bool), "SomeFlag");
            var binary = Expression.MakeBinary(ExpressionType.Equal, flag, Expression.Constant(true));

            var result = (BinaryExpression)visitor.Visit(binary);

            Assert.That(result, Is.SameAs(binary));
        }

        [Test]
        public void Inverse()
        {
            // "where !doc.SomeFlag"
            var flag = new LuceneQueryFieldExpression(typeof(bool), "SomeFlag");
            var expression = Expression.MakeUnary(ExpressionType.Not, flag, typeof(bool));
            var result = visitor.Visit(expression) as BinaryExpression;

            Assert.That(result, Is.Not.Null, "Expected BinaryExpression to be returned.");
            Assert.That(result.Left, Is.SameAs(flag));
            Assert.That(result.Right, Is.InstanceOf<ConstantExpression>());
            Assert.That(((ConstantExpression)result.Right).Value, Is.EqualTo(false));
        }

        [Test]
        public void ConvertNestedMethodCall()
        {
            // where !doc.Name.StartsWith("foo")
            var field = new LuceneQueryFieldExpression(typeof(string), "Name");
            var startsWith = Expression.Call(field, "StartsWith", null, Expression.Constant("foo"));
            var expression = Expression.MakeUnary(ExpressionType.Not, startsWith, typeof(bool));
            var result = visitor.Visit(expression) as BinaryExpression;

            Assert.That(result, Is.Not.Null, "Expected BinaryExpression to be returned.");
            Assert.That(result.Left, Is.SameAs(startsWith));
            Assert.That(result.Right, Is.InstanceOf<ConstantExpression>());
            Assert.That(result.NodeType, Is.EqualTo(ExpressionType.Equal));
            Assert.That(((ConstantExpression)result.Right).Value, Is.EqualTo(false));
        }

        [Test]
        public void DoubleInverse()
        {
            // "where !(!doc.SomeFlag)"
            var flag = new LuceneQueryFieldExpression(typeof(bool), "SomeFlag");
            var expression = Expression.MakeUnary(ExpressionType.Not, flag, typeof(bool));
            expression = Expression.MakeUnary(ExpressionType.Not, expression, typeof(bool));
            var result = visitor.Visit(expression) as BinaryExpression;

            Assert.That(result, Is.Not.Null, "Expected BinaryExpression to be returned.");
            Assert.That(result.Left, Is.SameAs(flag));
            Assert.That(result.Right, Is.InstanceOf<ConstantExpression>());
            Assert.That(((ConstantExpression)result.Right).Value, Is.EqualTo(true));
        }

        [TestCase(ExpressionType.Equal, ExpressionType.NotEqual)]
        [TestCase(ExpressionType.NotEqual, ExpressionType.Equal)]
        [TestCase(ExpressionType.GreaterThan, ExpressionType.LessThanOrEqual)]
        [TestCase(ExpressionType.GreaterThanOrEqual, ExpressionType.LessThan)]
        [TestCase(ExpressionType.LessThan, ExpressionType.GreaterThanOrEqual)]
        [TestCase(ExpressionType.LessThanOrEqual, ExpressionType.GreaterThan)]
        public void InverseComparison(ExpressionType comparison, ExpressionType expected)
        {
            // where !(doc.Count > 3)
            var field = new LuceneQueryFieldExpression(typeof(int), "Count");
            var constant = Expression.Constant(3);
            var expression = Expression.Not(Expression.MakeBinary(comparison, field, constant));

            var result = visitor.Visit(expression) as BinaryExpression;

            Assert.That(result, Is.Not.Null, "Expected BinaryExpression to be returned.");
            Assert.That(result.NodeType, Is.EqualTo(expected));
            Assert.That(result.Left, Is.SameAs(field));
            Assert.That(result.Right, Is.SameAs(constant));
        }

        [Test]
        public void DoubleInverseComparison()
        {
            // where !(!(doc.Count > 3))
            var binary = Expression.MakeBinary(ExpressionType.GreaterThan, new LuceneQueryFieldExpression(typeof(int), "Count"), Expression.Constant(3));

            var result = visitor.Visit(Expression.Not(Expression.Not(binary)));

            Assert.That(result, Is.SameAs(binary));
        }

        [Test]
        public void DoubleInverseMethodCall()
        {
            // where !(!doc.Name.StartsWith("foo"))
            var startsWith = Expression.Call(new LuceneQueryFieldExpression(typeof(string), "Name"), "StartsWith", null, Expression.Constant("foo"));

            var result = visitor.Visit(Expression.Not(Expression.Not(startsWith)));

            Assert.That(result, Is.SameAs(startsWith));
        }

        [Test]
        public void InverseFlagComparedWithConstant()
        {
            // where !(doc.SomeFlag == true)
            var flag = new LuceneQueryFieldExpression(typeof(bool), "SomeFlag");
            var expression = Expression.Not(Expression.MakeBinary(ExpressionType.Equal, flag, Expression.Constant(true)));

            var result = visitor.Visit(expression) as BinaryExpression;

            Assert.That(result, Is.Not.Null, "Expected BinaryExpression to be returned.");
            Assert.That(result.NodeType, Is.EqualTo(ExpressionType.NotEqual));
            Assert.That(result.Left, Is.SameAs(flag));
            Assert.That(((ConstantExpression)result.Right).Value, Is.EqualTo(true));
        }

        [TestCase(ExpressionType.AndAlso, ExpressionType.OrElse)]
        [TestCase(ExpressionType.OrElse, ExpressionType.AndAlso)]
        public void InverseGroup(ExpressionType group, ExpressionType expected)
        {
            // where !(doc.SomeFlag && doc.Name == "foo" && doc.Name.StartsWith("foo"))
            var flag = new LuceneQueryFieldExpression(typeof(bool), "SomeFlag");
            var name = new LuceneQueryFieldExpression(typeof(string), "Name");
            var equal = Expression.MakeBinary(ExpressionType.Equal, name, Expression.Constant("foo"));
            var startsWith = Expression.Call(name, "StartsWith", null, Expression.Constant("foo"));
            var expression = Expression.Not(Expression.MakeBinary(group, Expression.MakeBinary(group, flag, equal), startsWith));

            var result = visitor.Visit(expression) as BinaryExpression;

            Assert.That(result, Is.Not.Null, "Expected BinaryExpression to be returned.");
            Assert.That(result.NodeType, Is.EqualTo(expected));

            var left = (BinaryExpression)result.Left;
            Assert.That(left.NodeType, Is.EqualTo(expected));

            // doc.SomeFlag == false
            var notFlag = (BinaryExpression)left.Left;
            Assert.That(notFlag.NodeType, Is.EqualTo(ExpressionType.Equal));
            Assert.That(notFlag.Left, Is.SameAs(flag));
            Assert.That(((ConstantExpression)notFlag.Right).Value, Is.EqualTo(false));

            // doc.Name != "foo"
            var notEqual = (BinaryExpression)left.Right;
            Assert.That(notEqual.NodeType, Is.EqualTo(ExpressionType.NotEqual));
            Assert.That(notEqual.Left, Is.SameAs(name));

            // doc.Name.StartsWith("foo") == false
            var notStartsWith = (BinaryExpression)result.Right;
            Assert.That(notStartsWith.NodeType, Is.EqualTo(ExpressionType.Equal));
            Assert.That(notStartsWith.Left, Is.SameAs(startsWith));
            Assert.That(((ConstantExpression)notStartsWith.Right).Value, Is.EqualTo(false));
        }

        [Test]
        public void InverseGroup_DoesNotLeak()
        {
            // where !(doc.SomeFlag && doc.OtherFlag) && doc.ThirdFlag
            var third = new LuceneQueryFieldExpression(typeof(bool), "ThirdFlag");
            var group = Expression.AndAlso(new LuceneQueryFieldExpression(typeof(bool), "SomeFlag"), new LuceneQueryFieldExpression(typeof(bool), "OtherFlag"));

            var result = (BinaryExpression)visitor.Visit(Expression.AndAlso(Expression.Not(group), third));

            Assert.That(result.NodeType, Is.EqualTo(ExpressionType.AndAlso));
            Assert.That(result.Left.NodeType, Is.EqualTo(ExpressionType.OrElse));
            Assert.That(((BinaryExpression)result.Right).Left, Is.SameAs(third));
            Assert.That(((ConstantExpression)((BinaryExpression)result.Right).Right).Value, Is.EqualTo(true));
        }

    }
}