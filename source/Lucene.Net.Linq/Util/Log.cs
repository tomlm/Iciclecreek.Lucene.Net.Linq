using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lucene.Net.Linq.Util
{
    /// <summary>
    /// Entry point for configuring how Lucene.Net.Linq emits log messages.
    /// Set <see cref="LoggerFactory"/> once at application startup (before
    /// constructing any Lucene.Net.Linq types) to direct logging into your
    /// host's logging pipeline. By default a <see cref="NullLoggerFactory"/>
    /// is used and all log calls become no-ops.
    /// </summary>
    public static class Logging
    {
        private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

        public static ILoggerFactory LoggerFactory
        {
            get => _loggerFactory;
            set => _loggerFactory = value ?? NullLoggerFactory.Instance;
        }

        internal static ILogger CreateLogger<T>() => _loggerFactory.CreateLogger<T>();
        internal static ILogger CreateLogger(Type type) => _loggerFactory.CreateLogger(type.FullName);
    }
}
