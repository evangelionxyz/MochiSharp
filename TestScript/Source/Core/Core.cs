// Copyright (c) 2025 Evangelion Manuhutu

using System;
using System.Collections.Generic;
using System.Text;

namespace TestScript.Core
{
    public class Test
    {
        public static int TestMethod()
        {
            Console.WriteLine("Hello from C#!");
            return 42;
        }

        // Example: Method with parameters
        public static int Add(int a, int b)
        {
            Console.WriteLine($"C# Add called with {a} and {b}");
            return a + b;
        }

        // Example: Method with string parameter and return
        public static string Greet(string name)
        {
            string greeting = $"Hello, {name}! Welcome to Criollo.";
            Console.WriteLine(greeting);
            return greeting;
        }

        // Example: Method with no return value
        public static void LogMessage(string message)
        {
            Console.WriteLine($"[C# Log W L W L] {message}");
        }
    }
}
