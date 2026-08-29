// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Text.Json;
using Rulealize;
using Rulealize.Abstraction;

namespace Rulealize.Cli
{
    /// <summary>
    /// Materialises the plugin folder a rule set's <c>requires</c> calls for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which versions it calls for is decided by Rulealize, not here. PluginRequirement reads
    /// the constraints and PluginResolution chooses, both of them pure, both of them the same
    /// code CreateContext checks a loaded plugin against. A second reading of <c>^1.0</c>
    /// living in this tool would eventually differ from the runtime's, and it would show as a
    /// folder this assembled and the runtime refused — a fault with no symptom until it is
    /// too late to be useful.
    /// </para>
    /// <para>
    /// What a document requires is read from every document in the graph, because a
    /// composite is compiled with its components and <c>requires</c> is the whole of what
    /// each of them may draw on. A folder assembled from the holder's list alone would be one
    /// its components do not compile against — and the compile at the end of this would be
    /// where that showed, which is the point of it.
    /// </para>
    /// <para>
    /// This talks to nuget.org and not to the registry. What restoring needs is which
    /// versions of a named plugin are published and the packages themselves, and the feed
    /// answers both; the registry's catalogue is for finding a plugin you could not already
    /// name.
    /// </para>
    /// </remarks>
    internal static class RestoreCommand
    {
        private const string FlatContainer = "https://api.nuget.org/v3-flatcontainer";

        public static async Task<int> Run(string ruleSetPath, string outputFolder, string? ruleSets)
        {
            if (!File.Exists(ruleSetPath))
            {
                Console.Error.WriteLine($"'{ruleSetPath}' does not exist.");
                return 1;
            }

            string document = await File.ReadAllTextAsync(ruleSetPath);

            // What a composite draws on includes what its components draw on. `requires` is
            // the whole of what one document may use and no more, so a folder assembled from
            // the holder's alone would be one the components do not compile against.
            if (RuleSetFolder.Gather(ruleSetPath, document, ruleSets) is not RuleSetFolder held)
            {
                return 1;
            }

            held.Report();

            List<PluginRequirement> required = [];
            try
            {
                foreach (string each in new[] { document }.Concat(held.Documents.Values))
                {
                    required.AddRange(PluginRequirement.ReadFrom(each));
                }
            }
            catch (RuleSetBuildException failure)
            {
                Console.Error.WriteLine(failure.Message);
                return 1;
            }

            if (required.Count is 0)
            {
                Console.Error.WriteLine($"'{ruleSetPath}' requires no plugin. Nothing to restore.");
                return 0;
            }

            using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };

            Dictionary<string, IReadOnlyCollection<Version>> published = new(StringComparer.OrdinalIgnoreCase);
            foreach (string plugin in required.Select(static requirement => requirement.Plugin)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (await ReleasedVersions(http, plugin) is { Count: > 0 } versions)
                {
                    published[plugin] = versions;
                }
            }

            PluginResolution resolution = PluginResolution.Resolve(required, published);

            // What the feed could not answer for, the folder may already hold. A vocabulary
            // nobody publishes is a supported arrangement, and one still being written is the
            // ordinary case of it — both arrive the same way, by being copied in, and neither
            // is a reason to refuse the plugins that did resolve.
            List<UnsatisfiedRequirement> credited = [];
            List<UnsatisfiedRequirement> missing = [];

            if (!resolution.IsComplete)
            {
                IReadOnlyDictionary<string, Version> present = PluginFolder.Present(outputFolder);

                foreach (UnsatisfiedRequirement unsatisfied in resolution.Unsatisfied)
                {
                    // Whether a version answers a constraint is Rulealize's reading, not this
                    // tool's, for the reason no version choice is made here either.
                    bool satisfied = present.TryGetValue(unsatisfied.Plugin, out Version? version)
                        && unsatisfied.Requirements.All(requirement => requirement.IsSatisfiedBy(version));

                    (satisfied ? credited : missing).Add(unsatisfied);
                }
            }

            if (missing.Count is not 0)
            {
                Console.Error.WriteLine($"'{ruleSetPath}' cannot be restored from nuget.org:");
                foreach (UnsatisfiedRequirement unsatisfied in missing)
                {
                    Console.Error.WriteLine($"  {unsatisfied}");
                }

                // Worth saying rather than leaving to be inferred. A vocabulary implemented in
                // an application's own assembly and handed to AddPlugin is a supported
                // arrangement, and a rule set naming one is correct; there is simply nothing
                // for this command to fetch.
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    "A plugin nothing publishes is not necessarily a mistake. Build it and copy its");
                Console.Error.WriteLine(
                    $"assembly into '{outputFolder}', and this will credit it and fetch the rest.");

                if (!resolution.Plugins.IsEmpty)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"The other {resolution.Plugins.Length} would have resolved.");
                }

                return 1;
            }

            await Fetch(resolution, outputFolder);

            foreach (UnsatisfiedRequirement local in credited)
            {
                Console.WriteLine($"  {local.Plugin} (already in {outputFolder})");
            }

            foreach (ResolvedPlugin plugin in resolution.Plugins)
            {
                Console.WriteLine($"  {plugin}");
            }

            // ASCII, deliberately. A console writes in the machine's codepage, and this line
            // is the one most likely to be redirected into a log read somewhere else.
            Console.WriteLine(credited.Count is 0
                ? $"{resolution.Plugins.Length} plugins -> {outputFolder}"
                : $"{resolution.Plugins.Length} plugins -> {outputFolder}, {credited.Count} already there");

            // The folder is only right if it runs the document, and the way to find out is the
            // way the application will do it. This costs one compile and turns "the packages
            // downloaded" into "this rule set loads against what is now on disk".
            try
            {
                new RuleRuntime().LoadPluginsFrom(outputFolder).CreateContext(document, held.Documents);
            }
            catch (Exception failure) when (failure is RuleSetBuildException or PluginLoadException)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"The folder was written, and '{ruleSetPath}' does not compile against it:");
                Console.Error.WriteLine(failure.Message);
                return 1;
            }

            Console.WriteLine($"'{ruleSetPath}' compiles against it.");
            return 0;
        }

        private static async Task<List<Version>> ReleasedVersions(HttpClient http, string plugin)
        {
            // A plugin identifier and its package identifier are the same string. That is a
            // convention rather than anything the runtime enforces, and it is the one
            // assumption this command makes about where a plugin lives.
            string url = $"{FlatContainer}/{plugin.ToLowerInvariant()}/index.json";

            using HttpResponseMessage response = await http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using JsonDocument index = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            // Prereleases are skipped because no constraint could name one: `requires` is
            // written in three forms that all parse as System.Version, and none of them
            // carries a suffix.
            return [.. index.RootElement.GetProperty("versions").EnumerateArray()
                .Select(static element => element.GetString()!)
                .Where(static version => !version.Contains('-', StringComparison.Ordinal))
                .Select(Version.Parse)];
        }

        private static async Task Fetch(PluginResolution resolution, string outputFolder)
        {
            string scratch = Directory.CreateTempSubdirectory("rulealize-restore").FullName;

            try
            {
                string project = Path.Combine(scratch, "Restore", "Restore.csproj");
                Directory.CreateDirectory(Path.GetDirectoryName(project)!);

                // Bracketed versions, so that NuGet is asked to fetch a decision already made
                // rather than to make one. Everything left to it — frameworks, a plugin's own
                // dependencies — is what it is better at than anything written here would be.
                IEnumerable<string> references = resolution.Plugins.Select(static plugin =>
                    $"""    <PackageReference Include="{plugin.Plugin}" Version="[{plugin.Version}]" />""");

                await File.WriteAllTextAsync(project, $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                        <EnableDefaultItems>false</EnableDefaultItems>
                      </PropertyGroup>
                      <ItemGroup>
                    {string.Join(Environment.NewLine, references)}
                      </ItemGroup>
                    </Project>
                    """);

                string staging = Path.Combine(scratch, "out");
                Execute("dotnet", ["publish", project, "-c", "Release", "-o", staging]);

                Directory.CreateDirectory(outputFolder);
                foreach (string file in Directory.EnumerateFiles(staging, "*.dll"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);

                    // The stub, and the two assemblies the host necessarily already has.
                    // Copying those in would put a second Rulealize.Abstraction on the probe's
                    // path, and the whole reason plugins load into the default context is that
                    // there is one.
                    if (name is "Restore" or "Rulealize" or "Rulealize.Abstraction")
                    {
                        continue;
                    }

                    File.Copy(file, Path.Combine(outputFolder, Path.GetFileName(file)), overwrite: true);
                }
            }
            finally
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        private static void Execute(string command, string[] arguments)
        {
            ProcessStartInfo start = new()
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException($"'{command}' would not start.");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode is not 0)
            {
                throw new InvalidOperationException($"{command} failed.{Environment.NewLine}{output}{error}");
            }
        }
    }
}
