using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Shadowbound.SyntaxCheck
{
    /// <summary>
    /// Parses every C# file under a directory and reports syntax errors.
    ///
    /// Written because the Unity layer cannot be compiled in an environment
    /// without Unity. Parsing is not compiling - it proves nothing about whether
    /// the APIs called exist - but it does prove the files are valid C#, which is
    /// the failure that costs the most time to find inside an editor.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string root = args.Length > 0
                ? args[0]
                : Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Scripts");

            if (!Directory.Exists(root))
            {
                Console.Error.WriteLine("check-syntax: no such directory: " + root);
                return 2;
            }

            // Unity 6 compiles C# 9. Parsing at 9 means a C# 10 feature is reported
            // here rather than as a wall of errors on first import.
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                DocumentationMode.None,
                SourceCodeKind.Regular,
                new[] { "UNITY_EDITOR", "UNITY_ANDROID", "UNITY_6000_0_OR_NEWER" });

            string[] files = Directory
                .GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            int errorCount = 0;
            int fileCount = 0;

            foreach (string file in files)
            {
                string text;

                try
                {
                    text = File.ReadAllText(file);
                }
                catch (IOException exception)
                {
                    Console.Error.WriteLine("check-syntax: could not read " + file + ": " + exception.Message);
                    errorCount++;
                    continue;
                }

                fileCount++;

                SyntaxTree tree = CSharpSyntaxTree.ParseText(text, parseOptions, file);

                foreach (Diagnostic diagnostic in tree.GetDiagnostics())
                {
                    if (diagnostic.Severity != DiagnosticSeverity.Error)
                    {
                        continue;
                    }

                    FileLinePositionSpan span = diagnostic.Location.GetLineSpan();

                    Console.Error.WriteLine(
                        Relative(root, file) + "(" + (span.StartLinePosition.Line + 1) + "," +
                        (span.StartLinePosition.Character + 1) + "): " + diagnostic.Id + ": " +
                        diagnostic.GetMessage());

                    errorCount++;
                }
            }

            if (errorCount > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    "check-syntax: FAILED - " + errorCount + " syntax error(s) in " + fileCount + " file(s).");
                return 1;
            }

            Console.WriteLine(
                "check-syntax: OK (" + fileCount + " files parse as C# 9, no syntax errors)");
            return 0;
        }

        /// <summary>Shortens a path for readable output.</summary>
        private static string Relative(string root, string file)
        {
            string normalisedRoot = root.Replace('\\', '/').TrimEnd('/') + "/";
            string normalisedFile = file.Replace('\\', '/');

            return normalisedFile.StartsWith(normalisedRoot, StringComparison.Ordinal)
                ? normalisedFile.Substring(normalisedRoot.Length)
                : normalisedFile;
        }
    }
}
