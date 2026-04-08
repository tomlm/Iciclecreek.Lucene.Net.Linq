using System;
using System.Linq.Expressions;
using Lucene.Net.Linq.Clauses.Expressions;
using Lucene.Net.Linq.Transformation.Visitors;
using Lucene.Net.Search;
using NUnit.Framework;
using Remotion.Linq;

namespace Lucene.Net.Linq.Tests.Transformation.Visitors
{
    [TestFixture]
    public class BoostMethodCallVisitorTests
    {
        private BoostMethodCallVisitor visitor;

        [SetUp]
        public void SetUp()
        {
            visitor = new BoostMethodCallVisitor(0);
        }

        [Test]
        public void Stage0_Transform()
        {
            var methodInfo = Lucene.Net.Linq.Util.Reflection.MethodOf(() => LuceneMethods.Boost<string>(null, 0f));
            var fieldExpression = new LuceneQueryFieldExpression(typeof (string), "Name");
            const float boostAmount = 5.5f;

            // LuceneField(Name).Boost(5.5)
            var call = Expression.Call(methodInfo, fieldExpression, Expression.Constant(boostAmount));
            
            var result = visitor.Visit(call);

            Assert.That(result, Is.SameAs(fieldExpression));
            Assert.That(((LuceneQueryFieldExpression)result).FieldBoost, Is.EqualTo(boostAmount));
        }

        [Test]
        public void Stage1_Transform()
        {
            visitor = new BoostMethodCallVisitor(1);
            var methodInfo = Lucene.Net.Linq.Util.Reflection.MethodOf(() => false.Boost(0f));
            var fieldExpression = new LuceneQueryFieldExpression(typeof(string), "Name");
            var query = new LuceneQueryPredicateExpression(fieldExpression, Expression.Constant("foo"), Occur.SHOULD);

            const float boostAmount = 0.5f;

            // (LuceneQuery[Default](+Name:"foo")).Boost(0.5f)
            var call = Expression.Call(methodInfo, query, Expression.Constant(boostAmount));

            var result = visitor.Visit(call);

            Assert.That(result, Is.SameAs(query));
            Assert.That(((LuceneQueryPredicateExpression)result).Boost, Is.EqualTo(boostAmount));
        }

        [Test]
        public void Stage0_IgnoresNonLuceneQueryFieldExpression()
        {
            var methodInfo = Lucene.Net.Linq.Util.Reflection.MethodOf(() => LuceneMethods.Boost<string>(null, 0f));

            // "hello".Boost(5.5)
            var expr = Expression.Call(methodInfo, Expression.Constant("hello"), Expression.Constant(5.5f));

            var result = visitor.Visit(expr);

            Assert.That(result, Is.SameAs(expr));
        }

        [Test]
        public void Stage1_ThrowsWhenNotOnQueryField()
        {
            visitor = new BoostMethodCallVisitor(1);
            var methodInfo = Lucene.Net.Linq.Util.Reflection.MethodOf(() => LuceneMethods.Boost<string>(null, 0f));

            // "hello".Boost(5.5)
            var expr = Expression.Call(methodInfo, Expression.Constant("hello"), Expression.Constant(5.5f));

            TestDelegate call = () => visitor.Visit(expr);

            Assert.That(call, Throws.InstanceOf<NotSupportedException>());
        }

        [Test]
        public void IgnoresUnrelatedMethodCalls()
        {
            var methodInfo = Lucene.Net.Linq.Util.Reflection.MethodOf(() => string.IsNullOrEmpty("a"));

            var expr = Expression.Call(methodInfo, Expression.Constant("hello"));

            var result = visitor.Visit(expr);

            Assert.That(result, Is.SameAs(expr));
        }
    }
}