using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Lucene.Net.Linq
{
    /// <summary>
    /// One facet bucket: a value (or range label) and the number of
    /// matching documents in it.
    /// </summary>
    public sealed class FacetValue
    {
        public FacetValue(string label, int count)
        {
            Label = label;
            Count = count;
        }

        /// <summary>
        /// The facet value as written to the index, or the label of a <see cref="FacetRange"/>.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Number of matching documents with this value or in this range.
        /// </summary>
        public int Count { get; }

        public override string ToString() => Label + " (" + Count + ")";
    }

    /// <summary>
    /// A labelled bucket for range facet counts. Bounds are values of the
    /// property's type (or convertible to it); <c>null</c> means unbounded.
    /// </summary>
    public sealed class FacetRange
    {
        /// <param name="label">Label returned in <see cref="FacetValue.Label"/>.</param>
        /// <param name="min">Lower bound, or <c>null</c> for no lower bound.</param>
        /// <param name="max">Upper bound, or <c>null</c> for no upper bound.</param>
        /// <param name="minInclusive">Whether <paramref name="min"/> is included.</param>
        /// <param name="maxInclusive">Whether <paramref name="max"/> is included.</param>
        public FacetRange(string label, object min, object max, bool minInclusive = true, bool maxInclusive = false)
        {
            if (string.IsNullOrEmpty(label)) throw new ArgumentException("A facet range needs a label.", nameof(label));

            Label = label;
            Min = min;
            Max = max;
            MinInclusive = minInclusive;
            MaxInclusive = maxInclusive;
        }

        public string Label { get; }
        public object Min { get; }
        public object Max { get; }
        public bool MinInclusive { get; }
        public bool MaxInclusive { get; }

        /// <summary>
        /// One bucket per calendar day, from the day containing <paramref name="from"/>
        /// through the day containing <paramref name="to"/>. Labels are <c>yyyy-MM-dd</c>.
        /// </summary>
        public static IReadOnlyList<FacetRange> Daily(DateTime from, DateTime to)
            => Buckets(from.Date, to, d => d.AddDays(1), "yyyy-MM-dd");

        /// <summary>
        /// One bucket per calendar month, from the month containing <paramref name="from"/>
        /// through the month containing <paramref name="to"/>. Labels are <c>yyyy-MM</c>.
        /// </summary>
        public static IReadOnlyList<FacetRange> Monthly(DateTime from, DateTime to)
            => Buckets(new DateTime(from.Year, from.Month, 1, 0, 0, 0, from.Kind), to, d => d.AddMonths(1), "yyyy-MM");

        /// <summary>
        /// One bucket per calendar year, from the year containing <paramref name="from"/>
        /// through the year containing <paramref name="to"/>. Labels are <c>yyyy</c>.
        /// </summary>
        public static IReadOnlyList<FacetRange> Yearly(DateTime from, DateTime to)
            => Buckets(new DateTime(from.Year, 1, 1, 0, 0, 0, from.Kind), to, d => d.AddYears(1), "yyyy");

        private static IReadOnlyList<FacetRange> Buckets(DateTime start, DateTime to, Func<DateTime, DateTime> next, string format)
        {
            if (to < start) throw new ArgumentException("'to' must not be before 'from'.", nameof(to));

            var ranges = new List<FacetRange>();

            for (var current = start; current <= to; current = next(current))
            {
                ranges.Add(new FacetRange(current.ToString(format, CultureInfo.InvariantCulture), current, next(current)));
            }

            return ranges;
        }
    }

    /// <summary>
    /// Describes which facets to count. Passed to the configure callback of
    /// <see cref="LuceneMethods.Facets{T}"/>.
    /// </summary>
    public sealed class FacetRequest<T>
    {
        private readonly List<FacetSpec> specs = new List<FacetSpec>();

        internal FacetRequest()
        {
        }

        internal IReadOnlyList<FacetSpec> Specs => specs;

        /// <summary>
        /// Counts matching documents per value of <paramref name="selector"/>,
        /// ordered by count descending, then by value.
        /// </summary>
        /// <param name="selector">A property mapped with <c>Facet = true</c>; may be a collection.</param>
        /// <param name="top">Maximum number of values to return; <c>null</c> returns all values.</param>
        public FacetRequest<T> Count<TProperty>(Expression<Func<T, TProperty>> selector, int? top = null)
        {
            if (top.HasValue && top.Value <= 0) throw new ArgumentOutOfRangeException(nameof(top), "top must be positive.");

            return Add(new FacetSpec(GetPropertyName(selector), top, null));
        }

        /// <summary>
        /// Counts matching documents per range of <paramref name="selector"/>.
        /// Results are returned in the order of <paramref name="ranges"/>; a document
        /// is counted in every range that contains its value.
        /// </summary>
        /// <param name="selector">A single-valued numeric or date property mapped with <c>Facet = true</c>.</param>
        /// <param name="ranges">The buckets, e.g. <see cref="FacetRange.Monthly"/>.</param>
        public FacetRequest<T> Ranges<TProperty>(Expression<Func<T, TProperty>> selector, IEnumerable<FacetRange> ranges)
        {
            var list = ranges?.ToList() ?? throw new ArgumentNullException(nameof(ranges));

            if (list.Count == 0) throw new ArgumentException("At least one range is required.", nameof(ranges));

            return Add(new FacetSpec(GetPropertyName(selector), null, list));
        }

        private FacetRequest<T> Add(FacetSpec spec)
        {
            if (specs.Any(s => s.PropertyName == spec.PropertyName))
            {
                throw new ArgumentException("Property " + spec.PropertyName + " is already in this facet request.");
            }

            specs.Add(spec);
            return this;
        }

        internal static string GetPropertyName(LambdaExpression selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            var body = selector.Body;

            while (body is UnaryExpression unary && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
            {
                body = unary.Operand;
            }

            if (body is MemberExpression member
                && member.Member is PropertyInfo
                && member.Expression == selector.Parameters[0])
            {
                return member.Member.Name;
            }

            throw new ArgumentException("Facet selectors must be a property of the document, such as p => p.Tags. Got: " + selector, nameof(selector));
        }
    }

    /// <summary>
    /// Facet counts produced by <see cref="LuceneMethods.Facets{T}"/>. Filled when the
    /// query executes; each execution replaces the previous counts.
    /// </summary>
    public sealed class FacetResults<T> : IFacetResultSink
    {
        private volatile IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> results;

        internal FacetResults()
        {
        }

        /// <summary>
        /// <c>true</c> once the query has executed and counts are available.
        /// </summary>
        public bool IsAvailable => results != null;

        /// <summary>
        /// Counts for <paramref name="selector"/>: value counts ordered by count
        /// descending, or range counts in the order the ranges were given.
        /// </summary>
        public IReadOnlyList<FacetValue> For<TProperty>(Expression<Func<T, TProperty>> selector)
            => For(FacetRequest<T>.GetPropertyName(selector));

        /// <summary>
        /// Counts for the property named <paramref name="propertyName"/>.
        /// </summary>
        public IReadOnlyList<FacetValue> For(string propertyName)
        {
            var current = results ?? throw new InvalidOperationException(
                "Facet counts are not available until the query has been executed (for example with ToList(), Count() or foreach).");

            if (!current.TryGetValue(propertyName, out var values))
            {
                throw new ArgumentException("Property " + propertyName + " was not part of the facet request.", nameof(propertyName));
            }

            return values;
        }

        void IFacetResultSink.SetResults(IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> values)
        {
            results = values;
        }
    }

    internal interface IFacetResultSink
    {
        void SetResults(IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> values);
    }

    internal sealed class FacetSpec
    {
        public FacetSpec(string propertyName, int? top, IReadOnlyList<FacetRange> ranges)
        {
            PropertyName = propertyName;
            Top = top;
            Ranges = ranges;
        }

        public string PropertyName { get; }
        public int? Top { get; }

        /// <summary>
        /// Range buckets, or <c>null</c> for value counts.
        /// </summary>
        public IReadOnlyList<FacetRange> Ranges { get; }
    }

    /// <summary>
    /// The facet request carried through the query model by the facets clause.
    /// </summary>
    internal sealed class FacetQuery
    {
        public FacetQuery(IReadOnlyList<FacetSpec> specs, IFacetResultSink sink)
        {
            Specs = specs;
            Sink = sink;
        }

        public IReadOnlyList<FacetSpec> Specs { get; }
        public IFacetResultSink Sink { get; }
    }
}
