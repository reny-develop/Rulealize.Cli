// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize;
using Rulealize.Abstraction;

namespace Rulealize.Cli
{
    /// <summary>
    /// Compiles a rule set against the folder as it stands, and fetches nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same compile <c>restore</c> ends with, on its own and without the network.
    /// Restoring is something a document needs once; this is the question asked after every
    /// edit, and an edit-and-check loop that reached nuget.org would be a loop nobody runs.
    /// </para>
    /// <para>
    /// Everything decidable from the document is decided here — unknown operations, missing
    /// keys, expressions where literals belong, unbound locals, nodes used where their kind
    /// does not — so a rule set that passes has nothing left to fail on but its values.
    /// </para>
    /// </remarks>
    internal static class CheckCommand
    {
        public static int Run(string ruleSetPath, string folder)
        {
            if (!File.Exists(ruleSetPath))
            {
                Console.Error.WriteLine($"'{ruleSetPath}' does not exist.");
                return 1;
            }

            if (PluginFolder.Load(folder) is not RuleRuntime runtime)
            {
                return 1;
            }

            string document = File.ReadAllText(ruleSetPath);

            try
            {
                runtime.CreateContext(document);
            }
            catch (RuleSetBuildException failure)
            {
                Console.Error.WriteLine($"'{ruleSetPath}' does not compile against '{folder}':");
                Console.Error.WriteLine($"  {failure.Message}");

                // A missing vocabulary is the one failure whose remedy is another command,
                // and the message it arrives with names the document rather than the folder.
                if (runtime.Plugins.Length is 0)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"'{folder}' holds no vocabulary at all. "
                        + "Try 'rulealize plugins' to find out why.");
                }

                return 1;
            }

            Console.WriteLine($"'{ruleSetPath}' compiles against '{folder}' "
                + $"({PluginFolder.Count(runtime.Plugins.Length, "vocabulary", "vocabularies")}, "
                + $"{PluginFolder.Count(runtime.Operations.Length, "operation", "operations")}).");

            return 0;
        }
    }
}
