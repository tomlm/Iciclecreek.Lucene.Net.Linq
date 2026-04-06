using System;
using System.Collections.Generic;
using Lucene.Net.Analysis;
using Lucene.Net.Util;

namespace Lucene.Net.Linq.Analysis
{
    /// <summary>
    /// Per-field analyzer that prevents collisions of different analyzers
    /// being added for the same field. Built directly on top of
    /// <see cref="AnalyzerWrapper"/>, the Lucene 4.8 base class for
    /// composing analyzers.
    /// </summary>
    public class PerFieldAnalyzer : AnalyzerWrapper
    {
        private readonly Analyzer defaultAnalyzer;
        private readonly Dictionary<string, Analyzer> analyzerMap = new Dictionary<string, Analyzer>(StringComparer.Ordinal);

        public PerFieldAnalyzer(Analyzer defaultAnalyzer)
            : base(PER_FIELD_REUSE_STRATEGY)
        {
            this.defaultAnalyzer = defaultAnalyzer;
        }

        public virtual void AddAnalyzer(string fieldName, Analyzer analyzer)
        {
            lock (analyzerMap)
            {
                Analyzer previous;
                if (analyzerMap.TryGetValue(fieldName, out previous) && previous.GetType() != analyzer.GetType())
                {
                    throw new InvalidOperationException(string.Format(
                        "Attempt to replace analyzer for field {0} with analyzer of type {1}. Analyzer type {2} is already in use.",
                        fieldName, previous.GetType(), analyzer.GetType()));
                }
                analyzerMap[fieldName] = analyzer;
            }
        }

        public virtual void Merge(PerFieldAnalyzer other)
        {
            foreach (var kv in other.analyzerMap)
            {
                AddAnalyzer(kv.Key, kv.Value);
            }
        }

        public virtual Analyzer this[string fieldName] => GetAnalyzerForField(fieldName);

        protected override Analyzer GetWrappedAnalyzer(string fieldName) => GetAnalyzerForField(fieldName);

        protected virtual Analyzer GetAnalyzerForField(string fieldName)
        {
            lock (analyzerMap)
            {
                Analyzer analyzer;
                if (!analyzerMap.TryGetValue(fieldName, out analyzer))
                {
                    analyzer = defaultAnalyzer;
                }
                return analyzer;
            }
        }

        public override string ToString()
        {
            return "PerFieldAnalyzer(" + analyzerMap + ", default=" + defaultAnalyzer + ")";
        }
    }
}
