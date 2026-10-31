using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Lucene.Net.Linq.Transformation;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests.Transformation
{
    [TestFixture]
    public class SpanContainsExpressionTransformerTests
    {
        public class Doc
        {
            public string Id { get; set; }
            public string[] Tags { get; set; }
        }

        private readonly SpanContainsExpressionTransformer transformer = new SpanContainsExpressionTransformer();

        private static MethodCallExpression Body(Expression<Func<Doc, bool>> lambda) => (MethodCallExpression)lambda.Body;

        [Test]
        public void RewritesArrayFieldContains()
        {
            var call = Body(d => d.Tags.Contains("a"));

            var result = (MethodCallExpression)transformer.Transform(call);

            Assert.That(result.Method.DeclaringType, Is.EqualTo(typeof(Enumerable)));
            Assert.That(result.Method.Name, Is.EqualTo("Contains"));
            Assert.That(result.Arguments[0].Type, Is.EqualTo(typeof(string[])));
        }

        [Test]
        public void RewritesConstantArrayContainsField()
        {
            var ids = new[] { "1", "2" };
            var call = Body(d => ids.Contains(d.Id));

            var result = (MethodCallExpression)transformer.Transform(call);

            Assert.That(result.Method.DeclaringType, Is.EqualTo(typeof(Enumerable)));
            Assert.That(result.Arguments[1].ToString(), Is.EqualTo("d.Id"));
        }

        [Test]
        public void LeavesEnumerableContainsAlone()
        {
            var call = Body(d => Enumerable.Contains(d.Tags, "a"));

            Assert.That(transformer.Transform(call), Is.SameAs(call));
        }

        [Test]
        public void LeavesListContainsAlone()
        {
            var ids = new List<string> { "1" };
            var call = Body(d => ids.Contains(d.Id));

            Assert.That(transformer.Transform(call), Is.SameAs(call));
        }
    }
}
