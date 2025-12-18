using System;

namespace MochiSharp.Managed.Core
{
    /// <summary>
    /// Provides debugging utilities for MochiSharp.
    /// </summary>
    public static class Debug
    {
        /// <summary>
        /// Logs a debug message to the console.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Log(string message)
        {
            Console.WriteLine($"[MochiSharp] {message}");
        }

        /// <summary>
        /// Logs a warning message to the console.
        /// </summary>
        /// <param name="message">The warning message to log.</param>
        public static void LogWarning(string message)
        {
            Console.WriteLine($"[MochiSharp WARNING] {message}");
        }

        /// <summary>
        /// Logs an error message to the console.
        /// </summary>
        /// <param name="message">The error message to log.</param>
        public static void LogError(string message)
        {
            Console.WriteLine($"[MochiSharp ERROR] {message}");
        }

        /// <summary>
        /// Asserts that a condition is true. If the condition is false, an exception is thrown.
        /// </summary>
        /// <param name="condition">The condition to assert.</param>
        /// <param name="message">The message to include in the exception if the assertion fails.</param>
        public static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Assertion failed: {message}");
            }
        }
    }
}