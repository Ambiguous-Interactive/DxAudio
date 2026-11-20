namespace WallstopStudios.DxAudio.Tooling
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--contract")
            {
                return ContractSnapshot.Print(args[1]);
            }

            Assembly assembly = Assembly.LoadFrom(args[0]);
            HashSet<string> expected = new(StringComparer.Ordinal);

            foreach (Type type in assembly.GetTypes())
            {
                if (!type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                {
                    DiagnosticAnalyzer analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type);
                    foreach (DiagnosticDescriptor descriptor in analyzer.SupportedDiagnostics)
                    {
                        expected.Add(descriptor.Id);
                    }
                }
            }

            string[] configured = XDocument
                .Load(args[1])
                .Descendants("Rule")
                .Select(rule => (Id: (string)rule.Attribute("Id"), Action: (string)rule.Attribute("Action")))
                .Where(rule => rule.Action == "Error")
                .Select(rule => rule.Id)
                .ToArray();

            if (0 < expected.Count && configured.Length == expected.Count && expected.SetEquals(configured))
            {
                Console.WriteLine($"Verified {expected.Count} analyzer diagnostics.");
                return 0;
            }

            Console.Error.WriteLine($"Analyzer policy mismatch. Binary: {string.Join(", ", expected.OrderBy(id => id))}. Policy: {string.Join(", ", configured)}.");
            return 1;
        }
    }
}
