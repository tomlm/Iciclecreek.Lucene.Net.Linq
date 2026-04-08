using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Linq.Abstractions;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Search;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Lucene.Net.Linq.Tests
{
    [TestFixture]
    public class LuceneSessionTests
    {
        private LuceneSession<Record> session;
        private IDocumentMapper<Record> mapper;
        private IDocumentModificationDetector<Record> detector;
        private IIndexWriter writer;
        private Context context;

        [SetUp]
        public void SetUp()
        {
            mapper = Substitute.For<IDocumentMapper<Record>>();
            detector = Substitute.For<IDocumentModificationDetector<Record>>();
            writer = Substitute.For<IIndexWriter>();
            context = Substitute.For<Context>(null, new object());

            session = new LuceneSession<Record>(mapper, detector, writer, context, null);

            mapper.ToKey(Arg.Any<Record>())
                .Returns(ci =>
                {
                    var rec = (Record)ci[0];
                    return new DocumentKey(new Dictionary<IFieldMappingInfo, object>
                    {
                        { new FakeFieldMappingInfo { FieldName = "Id" }, rec.Id }
                    });
                });
        }

        [Test]
        public void AddWithSameKeyLastWriteWins()
        {
            var r1 = new Record { Id = "11", Name = "A" };
            var r2 = new Record { Id = "11", Name = "B" };

            session.Add(r1, r2);
            var pendingAdditions = session.ConvertPendingAdditions();

            Assert.That(pendingAdditions.Count(), Is.EqualTo(1));
            mapper.Received().ToDocument(r2, Arg.Any<Document>());
        }

        [Test]
        public void Commit_DeleteAll()
        {
            session.DeleteAll();

            session.Commit();

            Assert.That(session.DeleteAllFlag, Is.False, "Commit should reset flag.");

            writer.Received().DeleteAll();
            writer.Received().Commit();
        }

        [Test]
        public void Commit_Delete()
        {
            var q1 = new TermQuery(new Term("field1", "value1"));
            var q2 = new TermQuery(new Term("field1", "value2"));

            var r1 = new Record { Id = "12" };

            session.Delete(r1);
            session.Delete(q1, q2);

            session.Commit();

            Assert.That(session.Deletions, Is.Empty, "Commit should clear pending deletions.");

            writer.Received().DeleteDocuments(Arg.Is<Query[]>(qs => qs.Length == 3));
            writer.Received().Commit();
        }

        [Test]
        public void Commit_Add_DeletesKey()
        {
            var record = new Record { Id = "1" };

            session.Add(record);
            session.Commit();

            Assert.That(session.ConvertPendingAdditions, Is.Empty, "Commit should clear pending deletions.");

            mapper.Received().ToDocument(record, Arg.Any<Document>());
            writer.Received().DeleteDocuments(Arg.Any<Query[]>());
            writer.Received().AddDocument(Arg.Any<Document>());
            writer.Received().Commit();
        }

        [Test]
        public void Commit_Add_DeletesAllKeys()
        {
            var records = new[] {
                new Record { Id = "1" },
                new Record { Id = "2" }
            };

            session.Add(records);
            session.Commit();

            Assert.That(session.ConvertPendingAdditions, Is.Empty, "Commit should clear pending changes.");

            mapper.Received(2).ToDocument(Arg.Any<Record>(), Arg.Any<Document>());
            writer.Received().DeleteDocuments(Arg.Is<Query[]>(qs => qs.Length == 2));
            writer.Received(2).AddDocument(Arg.Any<Document>());
            writer.Received().Commit();
        }

        [Test]
        public void Commit_Add_KeyConstraint_None_DoesNotDelete()
        {
            var record = new Record { Id = "1" };
            session.Add(KeyConstraint.None, record);

            session.Commit();

            Assert.That(session.ConvertPendingAdditions, Is.Empty, "Commit should clear pending changes.");

            mapper.Received().ToDocument(record, Arg.Any<Document>());
            writer.Received().AddDocument(Arg.Any<Document>());
            writer.Received().Commit();
            writer.DidNotReceive().DeleteDocuments(Arg.Any<Query[]>());
        }

        [Test]
        public void Commit_Add_ConvertsDocumentAndKeyLate()
        {
            var record = new Record();

            session.Add(record);
            record.Id = "biully";
            record.Name = "a name";

            session.Commit();

            Assert.That(session.ConvertPendingAdditions, Is.Empty, "Commit should clear pending deletions.");

            mapper.Received().ToDocument(record, Arg.Any<Document>());
            writer.Received().DeleteDocuments(Arg.Any<Query[]>());
            writer.Received().AddDocument(Arg.Any<Document>());
            writer.Received().Commit();
        }

        [Test]
        public void Commit_ReloadsSearcher()
        {
            session.DeleteAll();

            session.Commit();

            context.Received().Reload();
        }

        [Test]
        public void Commit_NoPendingChanges()
        {
            session.Commit();
            writer.DidNotReceive().Commit();
        }

        [Test]
        public void DeleteAll()
        {
            session.DeleteAll();

            Assert.That(session.DeleteAllFlag, Is.True, "DeleteAllFlag");
        }

        [Test]
        public void DeleteAllClearsPendingAdditions()
        {
            var r1 = new Record();

            session.Add(r1);
            session.DeleteAll();

            Assert.That(session.ConvertPendingAdditions, Is.Empty, "Additions");
        }

        [Test]
        public void Delete()
        {
            var r1 = new Record { Id = "12" };

            session.Delete(r1);

            Assert.That(session.Deletions.Single().ToString(), Is.EqualTo("+Id:12"));
        }

        [Test]
        public void Delete_RemovesFromPendingAdditions()
        {
            var r1 = new Record { Id = "12" };
            session.Add(r1);
            session.Delete(r1);

            Assert.That(session.Additions, Is.Empty);
            Assert.That(session.Deletions.Single().ToString(), Is.EqualTo("+Id:12"));
        }

        [Test]
        public void Delete_MarkedForDeletion()
        {
            var r1 = new Record { Id = "12" };
            var key = mapper.ToKey(r1);

            session.Delete(r1);

            Assert.That(session.DocumentTracker.IsMarkedForDeletion(key), Is.True, "IsMarkedForDeletion");
        }

        [Test]
        public void Delete_MarkedForDeletion_ClearedOnRollback()
        {
            var r1 = new Record { Id = "12" };
            var key = mapper.ToKey(r1);

            session.Delete(r1);
            session.Rollback();

            Assert.That(session.DocumentTracker.IsMarkedForDeletion(key), Is.False, "IsMarkedForDeletion");
        }

        [Test]
        public void Delete_SetsPendingChangesFlag()
        {
            var r1 = new Record { Id = "12" };

            session.Delete(r1);

            Assert.That(session.PendingChanges, Is.True, "PendingChanges");
        }

        [Test]
        public void Delete_ThrowsOnEmptyKey()
        {
            // Override the default ToKey stub.
            mapper.ToKey(Arg.Any<Record>()).Returns(new DocumentKey());

            var r1 = new Record { Id = "12" };

            TestDelegate call = () => session.Delete(r1);

            Assert.That(call, Throws.InvalidOperationException);
        }

        [Test]
        public void Query_Attaches()
        {
            var records = new Record[0].AsQueryable();
            var provider = Substitute.For<IQueryProvider>();
            var queryable = Substitute.For<IQueryable<Record>>();
            queryable.Provider.Returns(provider);
            queryable.Expression.Returns(Expression.Constant(records));
            provider.CreateQuery<Record>(Arg.Any<Expression>()).Returns(records);
            session = new LuceneSession<Record>(mapper, detector, writer, context, queryable);

            session.Query();
        }

        [Test]
        public void PendingChanges_DirtyDocuments()
        {
            var record = new Record { Id = "0" };
            var document = new Document();
            var key = mapper.ToKey(record);

            detector.IsModified(record, document).Returns(true);
            session.DocumentTracker.TrackDocument(key, record, document);
            record.Id = "1";

            session.StageModifiedDocuments();

            Assert.That(session.PendingChanges, Is.True, "Should detect modified document.");
        }

        [Test]
        public void PendingChanges_NoDirtyDocuments()
        {
            var record = new Record { Id = "1" };
            var document = new Document();
            document.Add(new StringField("Id", "1", Field.Store.YES));

            var key = mapper.ToKey(record);

            detector.IsModified(record, document).Returns(false);
            session.DocumentTracker.TrackDocument(key, record, document);

            session.StageModifiedDocuments();

            Assert.That(session.PendingChanges, Is.False, "Should not stage unmodified document.");
        }

        [Test]
        public void Dispose_Commits()
        {
            session.DeleteAll();
            session.Dispose();

            writer.Received().DeleteAll();
            writer.Received().Commit();
        }

        [Test]
        public void Commit_RollbackException_ThrowsAggregateException()
        {
            var ex1 = new Exception("ex1");
            var ex2 = new Exception("ex2");
            writer.When(w => w.DeleteAll()).Do(_ => throw ex1);
            writer.When(w => w.Rollback()).Do(_ => throw ex2);

            session.DeleteAll();

            var thrown = Assert.Throws<AggregateException>(session.Commit);
            Assert.That(thrown.InnerExceptions, Is.EquivalentTo(new[] { ex1, ex2 }));
        }
    }
}
