using System;
using Lucene.Net.Documents;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Store;
using NSubstitute;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests
{
    [TestFixture]
    public class LuceneQueryExecutorTests
    {
        private TestableLuceneQueryExecutor<Record> executor;
        private IDocumentMapper<Record> mapper;
        private Document document;
        private Record record;
        private QueryExecutionContext context;

        [SetUp]
        public void SetUp()
        {
            record = new Record();
            document = new Document();
            mapper = Substitute.For<IDocumentMapper<Record>>();
            executor = new TestableLuceneQueryExecutor<Record>(new Context(new RAMDirectory(), new object()), _ => record, mapper);
            context = new QueryExecutionContext();
        }

        [Test]
        public void ConvertDocument()
        {
            var capturedKey = (IDocumentKey) null;
            var record = new Record();
            ObjectLookup<Record> lookup = k => { capturedKey = k; return record; };

            var enhancedMapper = Substitute.For<IDocumentMapperWithConverter>();
            executor = new TestableLuceneQueryExecutor<Record>(new Context(new RAMDirectory(), new object()), lookup, enhancedMapper);

            var key = new DocumentKey();
            enhancedMapper.ToKey(document).Returns(key);

            var result = executor.ConvertDocument(document, context);

            Assert.That(capturedKey, Is.SameAs(key), "Captured Key");
            Assert.That(result, Is.SameAs(record), "Record");

            enhancedMapper.Received().ToObject(document, context, record);
        }

        [Test]
        public void GetDocumentKey_ConvertToObjectThenToKey()
        {
            var key = new DocumentKey();

            mapper.ToKey(record).Returns(key);

            var result = executor.GetDocumentKey(document, context);

            Assert.That(result, Is.SameAs(key));
            mapper.Received().ToObject(document, context, record);
        }

        [Test]
        public void GetDocumentKey_GetKeyDirectlyWhenSupported()
        {
            var enhancedMapper = Substitute.For<IDocumentMapperWithConverter>();
            executor = new TestableLuceneQueryExecutor<Record>(new Context(new RAMDirectory(), new object()), _ => record, enhancedMapper);

            var key = new DocumentKey();

            enhancedMapper.ToKey(document).Returns(key);

            var result = executor.GetDocumentKey(document, context);

            Assert.That(result, Is.SameAs(key));
        }

        class TestableLuceneQueryExecutor<T> : LuceneQueryExecutor<T>
        {
            public TestableLuceneQueryExecutor(Context context, ObjectLookup<T> newItem, IDocumentMapper<T> mapper) : base(context, newItem, mapper)
            {
            }

            public new IDocumentKey GetDocumentKey(Document doc, IQueryExecutionContext context)
            {
                return base.GetDocumentKey(doc, context);
            }

            public new T ConvertDocument(Document doc, IQueryExecutionContext context)
            {
                return base.ConvertDocument(doc, context);
            }
        }

        public interface IDocumentMapperWithConverter : IDocumentMapper<Record>, IDocumentKeyConverter
        {
        }
    }
}
