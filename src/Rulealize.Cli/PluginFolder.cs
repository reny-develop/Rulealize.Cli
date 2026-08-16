// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Reflection;
using Rulealize;
using Rulealize.Abstraction.Plugin;

namespace Rulealize.Cli
{
    /// <summary>Reading a folder of vocabularies, and saying what is wrong when it is empty.</summary>
    internal static class PluginFolder
    {
        public const string Default = "plugin";

        /// <summary>Loads a folder the way a deployed application does.</summary>
        /// <param name="folder">The folder.</param>
        /// <returns>
        /// The runtime, or <see langword="null"/> when the folder could not be read — in
        /// which case why has already been written to standard error. A folder that holds
        /// nothing is read successfully; that it is empty is the caller's news to break.
        /// </returns>
        public static RuleRuntime? Load(string folder)
        {
            if (!Directory.Exists(folder))
            {
                Console.Error.WriteLine($"'{folder}' does not exist. Run 'rulealize restore' first, "
                    + "or put a plugin assembly there yourself.");
                return null;
            }

            try
            {
                return new RuleRuntime().LoadPluginsFrom(folder);
            }
            catch (PluginLoadException failure)
            {
                Console.Error.WriteLine($"'{folder}' could not be read: {failure.Message}");
                return null;
            }
        }

        /// <summary>What the folder already provides, by plugin identifier.</summary>
        /// <param name="folder">The folder.</param>
        /// <returns>The version of each vocabulary found, or an empty map.</returns>
        /// <remarks>
        /// <para>
        /// Read from a copy, because <c>Assembly.LoadFrom</c> holds a file open for the life
        /// of the process and this is a folder the caller is about to write into. The copy is
        /// then the thing that cannot be deleted, which is a temporary directory's problem
        /// rather than the user's.
        /// </para>
        /// <para>
        /// A manifest is an instance property, so there is no reading one without
        /// instantiating the plugin. That is why this cannot be done from metadata alone.
        /// </para>
        /// </remarks>
        public static IReadOnlyDictionary<string, Version> Present(string folder)
        {
            Dictionary<string, Version> found = new(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(folder))
            {
                return found;
            }

            string scratch = Directory.CreateTempSubdirectory("rulealize-present").FullName;

            try
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*.dll"))
                {
                    File.Copy(file, Path.Combine(scratch, Path.GetFileName(file)), overwrite: true);
                }

                foreach (PluginManifest manifest in new RuleRuntime().LoadPluginsFrom(scratch).Plugins)
                {
                    found[manifest.Id] = manifest.Version;
                }
            }
            catch (Exception exception) when (exception is PluginLoadException or IOException)
            {
                // A folder that cannot be read provides nothing, which is the answer this
                // returns anyway. Saying so is the caller's business, not this method's.
            }
            finally
            {
                try
                {
                    Directory.Delete(scratch, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Loaded assemblies are open. Left for the operating system.
                }
            }

            return found;
        }

        /// <summary>Counts the assemblies a sweep of this folder would look at.</summary>
        /// <param name="folder">The folder.</param>
        /// <returns>The file names.</returns>
        public static string[] Assemblies(string folder) =>
            Directory.Exists(folder) ? Directory.GetFiles(folder, "*.dll") : [];

        /// <summary>
        /// Says why a folder holding assemblies yielded no vocabulary.
        /// </summary>
        /// <param name="assemblies">What the folder holds.</param>
        /// <remarks>
        /// <para>
        /// The runtime sweeps a folder speculatively — pointing it at an application's own
        /// output is a reasonable thing to do, and such a folder is full of assemblies that
        /// have nothing to do with Rulealize — so a type it cannot use is passed over without
        /// a word. That is right for the runtime and wrong here, where somebody asked this
        /// question on purpose about a folder they assembled.
        /// </para>
        /// <para>
        /// The conditions restated below are <c>PluginProbe.IsPlugin</c>'s. They are checked
        /// again rather than reported by the runtime because the runtime never decided
        /// anything: it looked for types matching them and found none.
        /// </para>
        /// </remarks>
        public static void Explain(string[] assemblies)
        {
            Console.Error.WriteLine("Nothing loaded, and the runtime does not say why: a folder sweep passes");
            Console.Error.WriteLine("over what it cannot use. What is in there:");
            Console.Error.WriteLine();

            foreach (string path in assemblies)
            {
                Type[] types;
                try
                {
                    types = Assembly.LoadFrom(Path.GetFullPath(path)).GetTypes();
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"  {Path.GetFileName(path)}");
                    Console.Error.WriteLine($"      could not be read: {exception.Message}");
                    continue;
                }

                Type[] candidates =
                    [.. types.Where(static type => typeof(IRulealizePlugin).IsAssignableFrom(type) && !type.IsInterface)];

                if (candidates.Length is 0)
                {
                    Console.Error.WriteLine($"  {Path.GetFileName(path)}");
                    Console.Error.WriteLine("      holds no implementation of IRulealizePlugin.");
                    continue;
                }

                foreach (Type type in candidates)
                {
                    Console.Error.WriteLine($"  {type.FullName}");
                    Console.Error.WriteLine($"      {Fault(type)}");
                }
            }
        }

        /// <summary>Counts something, in words, so that a line reads as English.</summary>
        /// <param name="number">How many.</param>
        /// <param name="one">The singular noun.</param>
        /// <param name="many">The plural noun.</param>
        /// <returns>The phrase.</returns>
        public static string Count(int number, string one, string many) =>
            number is 1 ? $"1 {one}" : $"{number} {many}";

        private static string Fault(Type type)
        {
            if (type.IsAbstract)
            {
                return "is abstract. A sweep instantiates what it finds, so it must not be.";
            }

            if (type.IsNested)
            {
                return "is a nested type, which is never public to reflection however it is "
                    + "declared. Move it out to the namespace.";
            }

            if (!type.IsPublic)
            {
                return "is not public. A sweep only takes public types.";
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                return "has no parameterless constructor. A sweep has nothing to pass to one.";
            }

            return "satisfies everything a sweep asks of a plugin type, so the fault is elsewhere.";
        }
    }
}
