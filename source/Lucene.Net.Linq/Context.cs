using System;
using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Index;
using Lucene.Net.Linq.Util;
using Microsoft.Extensions.Logging;
using Lucene.Net.Search;
using Lucene.Net.Store;

namespace Lucene.Net.Linq
{
    internal class Context : IDisposable
    {
        private static readonly ILogger Log = Logging.CreateLogger<Context>();

        public event EventHandler<SearcherLoadEventArgs> SearcherLoading;

        private readonly Directory directory;
        private readonly object transactionLock;

        private readonly object searcherLock = new object();
        private readonly object reloadLock = new object();
        private SearcherClientTracker tracker;
        private IndexReader reader;
        private bool disposed;

        public Context(Directory directory, object transactionLock)
        {
            this.directory = directory;
            this.transactionLock = transactionLock;
            Settings = new LuceneDataProviderSettings();
        }

        public void Dispose()
        {
            lock (searcherLock)
            {
                AssertNotDisposed();

                disposed = true;

                if (tracker == null) return;

                if (!tracker.TryDispose())
                {
                    Log.LogWarning("Context is being disposed before all handles were released.");
                }

                tracker = null;
            }
        }

        public LuceneDataProviderSettings Settings { get; set; }

        public Directory Directory
        {
            get { return directory; }
        }

        public object TransactionLock
        {
            get { return transactionLock; }
        }

        public ISearcherHandle CheckoutSearcher()
        {
            AssertNotDisposed();
            return new SearcherHandle(CurrentTracker);
        }

        public virtual void Reload()
        {
            lock (reloadLock)
            {
                AssertNotDisposed();
                Log.LogInformation("Reloading index.");

                IndexSearcher searcher;
                if (reader == null)
                {
                    searcher = CreateSearcher();
                    reader = searcher.IndexReader;
                }
                else if (!ReopenSearcher(out searcher))
                {
                    return;
                }

                var newTracker = new SearcherClientTracker(searcher);

                var tmpHandler = SearcherLoading;

                if (tmpHandler != null)
                {
                    Log.LogDebug("Invoking SearcherLoading event.");
                    tmpHandler(this, new SearcherLoadEventArgs(newTracker.Searcher));
                }

                lock (searcherLock)
                {
                    if (tracker != null)
                    {
                        tracker.Dispose();
                    }

                    tracker = newTracker;
                }
            }

            Log.LogDebug("Index reloading completed.");
        }

        internal SearcherClientTracker CurrentTracker
        {
            get
            {
                lock (searcherLock)
                {
                    AssertNotDisposed();

                    if (tracker == null)
                    {
                        var searcher = CreateSearcher();
                        reader = searcher.IndexReader;
                        tracker = new SearcherClientTracker(searcher);
                    }
                    return tracker;
                }
            }
        }

        protected virtual IndexSearcher CreateSearcher()
        {
            return new IndexSearcher(DirectoryReader.Open(directory));
        }

        /// <summary>
        /// Reopen the <see cref="IndexReader"/>. If the index has not changed,
        /// return <c>false</c>. If the index has changed, set <paramref name="searcher"/>
        /// with a new <see cref="IndexSearcher"/> instance and return <c>true</c>.
        /// </summary>
        protected virtual bool ReopenSearcher(out IndexSearcher searcher)
        {
            searcher = null;
            var oldReader = (DirectoryReader)reader;
            var newReader = DirectoryReader.OpenIfChanged(oldReader);
            if (newReader == null)
            {
                return false;
            }
            reader = newReader;
            searcher = new IndexSearcher(newReader);
            return true;
        }

        private void AssertNotDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        internal class SearcherHandle : ISearcherHandle
        {
            private readonly SearcherClientTracker tracker;
            private bool disposed;

            public SearcherHandle(SearcherClientTracker tracker)
            {
                this.tracker = tracker;
                tracker.AddClient(this);
            }

            public IndexSearcher Searcher
            {
                get { return tracker.Searcher; }
            }

            public void Dispose()
            {
                if (disposed) throw new ObjectDisposedException(typeof(ISearcherHandle).Name);
                disposed = true;
                tracker.RemoveClient(this);
            }
        }

        internal class SearcherClientTracker : IDisposable
        {
            private static readonly IList<SearcherClientTracker> undisposedTrackers = new List<SearcherClientTracker>();

            private readonly object sync = new object();
            private readonly List<WeakReference> searcherReferences = new List<WeakReference>();
            private readonly IndexSearcher searcher;
            private bool disposePending;
            private bool disposed;

            public SearcherClientTracker(IndexSearcher searcher)
            {
                this.searcher = searcher;

                lock(typeof(SearcherClientTracker))
                {
                    undisposedTrackers.Add(this);
                }
            }

            public IndexSearcher Searcher
            {
                get { return searcher; }
            }

            public void AddClient(object client)
            {
                lock (sync)
                    searcherReferences.Add(new WeakReference(client));
            }

            public void RemoveClient(object client)
            {
                lock (sync)
                {
                    searcherReferences.Remove(searcherReferences.First(wr => ReferenceEquals(wr.Target, client)));
                    RemoveDeadReferences();

                    if (disposePending)
                    {
                        Dispose();
                    }
                }
            }

            public void Dispose()
            {
                TryDispose();
            }

            public bool TryDispose()
            {
                lock (sync)
                {
                    disposePending = false;

                    if (disposed)
                    {
                        throw new ObjectDisposedException(GetType().Name);
                    }

                    RemoveDeadReferences();
                    if (searcherReferences.Count == 0)
                    {
                        lock (typeof(SearcherClientTracker))
                        {
                            undisposedTrackers.Remove(this);
                        }

                        // In Lucene 4.8 IndexSearcher itself is no longer
                        // IDisposable; the underlying IndexReader holds the
                        // resources and is what we close.
                        searcher.IndexReader.Dispose();

                        disposed = true;
                    }
                    else
                    {
                        disposePending = true;
                    }

                    return disposed;
                }
            }

            internal int ReferenceCount
            {
                get
                {
                    lock (sync) return searcherReferences.Count;
                }
            }

            private void RemoveDeadReferences()
            {
                searcherReferences.RemoveAll(wr => !wr.IsAlive);
            }
        }
    }

    internal interface ISearcherHandle : IDisposable
    {
        IndexSearcher Searcher { get; }
    }

}
