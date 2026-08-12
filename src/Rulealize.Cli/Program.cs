// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Text.Json;
using Rulealize;
using Rulealize.Abstraction;

// Materialises the plugin folder a rule set's `requires` calls for.
//
//   rulealize restore <rule set> [--out <folder>]
//
// Rulealize's README used to open with fourteen `git clone` lines and now opens with a
// `dotnet add package` per plugin. This is the third answer, and the first one the document
// gives for itself: `requires` already names every vocabulary the rule set draws on and the
// versions of each that will do, so nobody should be transcribing that list by hand.
//
// Which versions it calls for is decided by Rulealize, not here. PluginRequirement reads the
// constraints and PluginResolution chooses, both of them pure, both of them the same code
// CreateContext checks a loaded plugin against. A second reading of `^1.0` living in this
// tool would eventually differ from the runtime's, and it would show as a folder this
// assembled and the runtime refused — which is a fault with no symptom until it is too late
// to be useful.
//
// This talks to nuget.org and not to the registry. What restoring needs is which versions of
// a named plugin are published and the packages themselves, and the feed answers both; the
// registry's catalogue is for finding a plugin you could not already name.
//
// Fetching is delegated to the SDK rather than done here, and the versions are pinned exactly
// so that delegating decides nothing. NuGet then gets target frameworks and a plugin's own
// dependencies right, which this could only approximate, and what lands in the folder is what
// `dotnet publish` would have put there — the arrangement a deployed application actually has.

const string FlatContainer = "https://api.nuget.org/v3-flatcontainer";

if (args is not ["restore", string ruleSetPath, ..])
{
    Console.Error.WriteLine("usage: rulealize restore <rule set> [--out <folder>]");
    return 2;
}

string outputFolder = Option(args, "--out") ?? "plugin";

if (!File.Exists(ruleSetPath))
{
    Console.Error.WriteLine($"'{ruleSetPath}' does not exist.");
    return 1;
}

string document = await File.ReadAllTextAsync(ruleSetPath);

IReadOnlyList<PluginRequirement> required;
try
{
    required = PluginRequirement.ReadFrom(document);
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
foreach (string plugin in required.Select(static requirement => requirement.Plugin).Distinct(StringComparer.OrdinalIgnoreCase))
{
    if (await ReleasedVersions(plugin) is { Count: > 0 } versions)
    {
        published[plugin] = versions;
    }
}

PluginResolution resolution = PluginResolution.Resolve(required, published);

if (!resolution.IsComplete)
{
    Console.Error.WriteLine($"'{ruleSetPath}' cannot be restored from nuget.org:");
    foreach (UnsatisfiedRequirement missing in resolution.Unsatisfied)
    {
        Console.Error.WriteLine($"  {missing}");
    }

    // Worth saying rather than leaving to be inferred. A vocabulary implemented in an
    // application's own assembly and handed to AddPlugin is a supported arrangement, and a
    // rule set naming one is correct; there is simply nothing for this command to fetch.
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        "A plugin nothing publishes is not necessarily a mistake: a vocabulary can be implemented");
    Console.Error.WriteLine(
        "in your own assembly and passed to RuleRuntime.AddPlugin. Nothing here can fetch one.");

    if (!resolution.Plugins.IsEmpty)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"The other {resolution.Plugins.Length} would have resolved.");
    }

    return 1;
}

await Fetch(resolution, outputFolder);

foreach (ResolvedPlugin plugin in resolution.Plugins)
{
    Console.WriteLine($"  {plugin}");
}

// ASCII, deliberately. A console writes in the machine's codepage, and this line is the one
// most likely to be redirected into a log read somewhere else.
Console.WriteLine($"{resolution.Plugins.Length} plugins -> {outputFolder}");

// The folder is only right if it runs the document, and the way to find out is the way the
// application will do it. This costs one compile and turns "the packages downloaded" into
// "this rule set loads against what is now on disk".
try
{
    new RuleRuntime().LoadPluginsFrom(outputFolder).CreateContext(document);
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

static string? Option(string[] args, string name)
{
    int at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

async Task<List<Version>> ReleasedVersions(string plugin)
{
    // A plugin identifier and its package identifier are the same string. That is a
    // convention rather than anything the runtime enforces, and it is the one assumption
    // this command makes about where a plugin lives.
    string url = $"{FlatContainer}/{plugin.ToLowerInvariant()}/index.json";

    using HttpResponseMessage response = await http.GetAsync(url);
    if (!response.IsSuccessStatusCode)
    {
        return [];
    }

    using JsonDocument index = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    // Prereleases are skipped because no constraint could name one: `requires` is written in
    // three forms that all parse as System.Version, and none of them carries a suffix.
    return [.. index.RootElement.GetProperty("versions").EnumerateArray()
        .Select(static element => element.GetString()!)
        .Where(static version => !version.Contains('-', StringComparison.Ordinal))
        .Select(Version.Parse)];
}

static async Task Fetch(PluginResolution resolution, string outputFolder)
{
    string scratch = Directory.CreateTempSubdirectory("rulealize-restore").FullName;

    try
    {
        string project = Path.Combine(scratch, "Restore", "Restore.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);

        // Bracketed versions, so that NuGet is asked to fetch a decision already made rather
        // than to make one. Everything left to it — frameworks, a plugin's own dependencies —
        // is what it is better at than anything written here would be.
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
        Run("dotnet", ["publish", project, "-c", "Release", "-o", staging]);

        Directory.CreateDirectory(outputFolder);
        foreach (string file in Directory.EnumerateFiles(staging, "*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(file);

            // The stub, and the two assemblies the host necessarily already has. Copying
            // those in would put a second Rulealize.Abstraction on the probe's path, and the
            // whole reason plugins load into the default context is that there is one.
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

static void Run(string command, string[] arguments)
{
    ProcessStartInfo start = new() { FileName = command, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(start) ?? throw new InvalidOperationException($"'{command}' would not start.");
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode is not 0)
    {
        throw new InvalidOperationException($"{command} failed.{Environment.NewLine}{output}{error}");
    }
}
