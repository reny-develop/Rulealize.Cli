// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize;
using Rulealize.Abstraction.Plugin;

namespace Rulealize.Cli
{
    /// <summary>
    /// Says what a folder of vocabularies provides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two questions, and neither has an answer anywhere else. Which vocabularies loaded is
    /// a folder's business and no document's, so a rule set cannot be asked. What each one
    /// registered exists only as the calls it made while registering — nothing declares it —
    /// so <c>RuleRuntime.Operations</c> is the only account there is, and it is the one a
    /// rule set will be compiled against.
    /// </para>
    /// <para>
    /// Which makes this the place a forgotten <c>AddExpression</c> shows up. The operation is
    /// written, the assembly is in the folder, and the name is simply absent from the list.
    /// </para>
    /// </remarks>
    internal static class PluginsCommand
    {
        public static int Run(string folder)
        {
            if (PluginFolder.Load(folder) is not RuleRuntime runtime)
            {
                return 1;
            }

            string[] assemblies = PluginFolder.Assemblies(folder);

            Console.WriteLine($"{folder}");
            Console.WriteLine($"  {PluginFolder.Count(assemblies.Length, "assembly", "assemblies")}, "
                + $"{PluginFolder.Count(runtime.Plugins.Length, "vocabulary", "vocabularies")}");
            Console.WriteLine();

            if (runtime.Plugins.Length is 0)
            {
                PluginFolder.Explain(assemblies);
                return 1;
            }

            foreach (PluginManifest manifest in runtime.Plugins)
            {
                string prefix = manifest.ReservedPrefix is char character ? $"  {character}" : string.Empty;
                Console.WriteLine($"  {manifest.Id} {manifest.Version}  ({manifest.Namespace}){prefix}");

                foreach (OperationDescriptor operation in runtime.Operations.Where(o => o.Plugin.Id == manifest.Id))
                {
                    Console.WriteLine($"      {operation.Op,-28} {operation.Kind.ToString().ToLowerInvariant()}");
                }

                Console.WriteLine();
            }

            Console.WriteLine($"{PluginFolder.Count(runtime.Operations.Length, "operation", "operations")} in total.");
            return 0;
        }
    }
}
