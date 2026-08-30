// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Rulealize.Abstraction;

namespace Rulealize.Cli
{
    /// <summary>Puts the rule sets a document holds into the folder it resolves them from.</summary>
    /// <remarks>
    /// <para>
    /// This runs before <see cref="RuleSetFolder"/> rather than inside it, and the split is the
    /// shape of the tool: <c>restore</c> is what makes a folder right, and every other command
    /// reads one. Nothing else here fetches, so nothing else needed changing.
    /// </para>
    /// <para>
    /// A rule set's identifier is the identifier of the package it is published under, so
    /// nothing has to be resolved to anything: <c>uses</c> names a package, and the feed
    /// answers for it. That convention is the registry's and is not enforced by the runtime —
    /// what is fetched is checked against what was asked for the moment it arrives, which is
    /// what <see cref="RuleSetIdentity"/> is for.
    /// </para>
    /// <para>
    /// Which version is taken is Rulealize's reading, through
    /// <see cref="RuleSetRequirement.Choose"/>. A second statement of <em>the lowest
    /// satisfying version wins</em> living in this tool would eventually differ from the one a
    /// <c>requires</c> is resolved by, and it would show as a restore that is reproducible for
    /// one kind of requirement and not the other, in one command, with nothing about it
    /// looking wrong.
    /// </para>
    /// </remarks>
    internal static class RuleSetFetch
    {
        private const string FlatContainer = "https://api.nuget.org/v3-flatcontainer";

        /// <summary>Fetches what the graph under a document names and the folder does not hold.</summary>
        /// <param name="folder">Where held documents are resolved from, and where these are written.</param>
        /// <param name="ruleSetPath">The document that was asked about, for messages.</param>
        /// <param name="ruleSetDocument">Its text.</param>
        /// <returns><see langword="false"/> when something could not be had, and why is reported.</returns>
        /// <remarks>
        /// The graph is walked as it is discovered, because it cannot be read in advance: which
        /// version of a held rule set is taken decides which document arrives, and that decides
        /// what else is named. A constraint met after its rule set was already fetched is
        /// checked against what was fetched rather than sending the walk round again — the
        /// answer is the same and the message says which two entries disagree.
        /// </remarks>
        public static async Task<bool> Into(string folder, string ruleSetPath, string ruleSetDocument)
        {
            if (Uses(ruleSetPath, ruleSetDocument) is not IReadOnlyList<RuleSetRequirement> uses)
            {
                return false;
            }

            if (uses.Count is 0)
            {
                return true;
            }

            if (Declared(folder) is not Dictionary<string, string> declaring)
            {
                return false;
            }

            using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };

            // Every constraint seen for an identifier, whether or not it has been fetched. What
            // is chosen is chosen against all of them at once, which is what makes two holders
            // asking for one rule set produce one document rather than the last one to be asked.
            Dictionary<string, List<RuleSetRequirement>> wanted = new(StringComparer.Ordinal);
            Dictionary<string, RuleSetIdentity> arrived = new(StringComparer.Ordinal);
            List<string> written = [];
            Queue<Held> pending = new(uses.Select(entry => new Held(entry, ruleSetPath)));

            while (pending.Count is not 0)
            {
                (RuleSetRequirement entry, string holder) = pending.Dequeue();

                if (!wanted.TryGetValue(entry.RuleSet, out List<RuleSetRequirement>? constraints))
                {
                    wanted[entry.RuleSet] = constraints = [];
                }

                constraints.Add(entry);

                // Already here, either because its author keeps it beside the document that
                // holds it or because an earlier step of this walk fetched it. Either way it is
                // not fetched again and it is not written over: a document in the folder is
                // somebody's, and restoring is not the moment to find out whose.
                if (arrived.TryGetValue(entry.RuleSet, out RuleSetIdentity? already))
                {
                    if (!Accepts(already, entry, holder, declaring[entry.RuleSet]))
                    {
                        return false;
                    }

                    continue;
                }

                if (declaring.TryGetValue(entry.RuleSet, out string? path))
                {
                    if (Identity(path) is not RuleSetIdentity local)
                    {
                        return false;
                    }

                    arrived[entry.RuleSet] = local;

                    if (!Accepts(local, entry, holder, path))
                    {
                        return false;
                    }

                    if (Uses(path, File.ReadAllText(path)) is not IReadOnlyList<RuleSetRequirement> nested)
                    {
                        return false;
                    }

                    foreach (RuleSetRequirement further in nested)
                    {
                        pending.Enqueue(new Held(further, path));
                    }

                    continue;
                }

                if (await Fetch(http, entry, constraints, holder, folder, declaring) is not (string file, string text))
                {
                    return false;
                }

                RuleSetIdentity identity = RuleSetIdentity.ReadFrom(text);
                arrived[entry.RuleSet] = identity;
                declaring[entry.RuleSet] = file;
                written.Add($"{identity.RuleSet} -> {Show(file)}");

                if (Uses(file, text) is not IReadOnlyList<RuleSetRequirement> held)
                {
                    return false;
                }

                foreach (RuleSetRequirement further in held)
                {
                    pending.Enqueue(new Held(further, file));
                }
            }

            foreach (string line in written)
            {
                Console.WriteLine($"  {line}");
            }

            if (written.Count is not 0)
            {
                // ASCII, deliberately, for the reason the plugin line is: a console writes in
                // the machine's codepage and this is a line that gets redirected into a log.
                Console.WriteLine($"{PluginFolder.Count(written.Count, "rule set", "rule sets")} -> {folder}");
            }

            return true;
        }

        // ── the feed ───────────────────────────────────────────────────────────────────

        private static async Task<(string File, string Text)?> Fetch(
            HttpClient http,
            RuleSetRequirement entry,
            List<RuleSetRequirement> constraints,
            string holder,
            string folder,
            Dictionary<string, string> declaring)
        {
            List<Version> published = await ReleasedVersions(http, entry.RuleSet);

            if (published.Count is 0)
            {
                Console.Error.WriteLine(
                    $"'{Show(holder)}' holds '{entry.RuleSet}', and nuget.org has no released package of that name.");
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    "A rule set is published under the identifier its document declares, and is fetched by it.");
                Console.Error.WriteLine(
                    $"A document that nobody publishes is not a mistake — put it in '{folder}' and this will find it.");
                return null;
            }

            // Rulealize's reading, not this tool's. Null covers both shortfalls, and the caller
            // has both sides to tell them apart with.
            if (RuleSetRequirement.Choose(constraints, published) is not Version version)
            {
                Console.Error.WriteLine($"No released version of '{entry.RuleSet}' satisfies what holds it:");
                foreach (RuleSetRequirement wanted in constraints)
                {
                    Console.Error.WriteLine($"  {wanted}");
                }

                Console.Error.WriteLine($"  published: {string.Join(", ", published)}");
                return null;
            }

            string text;
            try
            {
                text = await Document(http, entry.RuleSet, version);
            }
            catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException
                or TaskCanceledException)
            {
                Console.Error.WriteLine($"'{entry.RuleSet}' {version} could not be read: {failure.Message}");
                return null;
            }

            RuleSetIdentity identity;
            try
            {
                identity = RuleSetIdentity.ReadFrom(text);
            }
            catch (RuleSetBuildException failure)
            {
                Console.Error.WriteLine($"'{entry.RuleSet}' {version} does not hold a readable rule set:");
                Console.Error.WriteLine($"  {failure.Message}");
                return null;
            }

            foreach (RuleSetRequirement wanted in constraints)
            {
                if (!Accepts(identity, wanted, holder, $"{entry.RuleSet} {version}"))
                {
                    return null;
                }
            }

            // Named for the identifier and not for whatever the package called the file, so a
            // second restore finds it and a person reading the folder can see which of these
            // are theirs. A file already under that name belonging to something else is not
            // written over: it is somebody's, and this is not the moment to decide it is not.
            string file = Path.Combine(folder, $"{identity.Id}.json");

            if (File.Exists(file) && !declaring.ContainsValue(file))
            {
                Console.Error.WriteLine(
                    $"'{Show(file)}' is where '{identity.Id}' would go, and something else is already there.");
                return null;
            }

            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(file, text);
            return (file, text);
        }

        private static async Task<List<Version>> ReleasedVersions(HttpClient http, string ruleSet)
        {
            string url = $"{FlatContainer}/{ruleSet.ToLowerInvariant()}/index.json";

            using HttpResponseMessage answer = await http.GetAsync(url);
            if (answer.StatusCode is HttpStatusCode.NotFound)
            {
                return [];
            }

            answer.EnsureSuccessStatusCode();

            using JsonDocument index = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

            // Prereleases are skipped for the reason a `requires` skips them: the three
            // constraint forms all parse as System.Version and none of them carries a suffix,
            // so no `uses` entry could name one.
            return [.. index.RootElement.GetProperty("versions").EnumerateArray()
                .Select(static element => element.GetString()!)
                .Where(static version => !version.Contains('-', StringComparison.Ordinal))
                .Select(Version.Parse)];
        }

        /// <summary>Reads the one document a rule set package distributes.</summary>
        /// <remarks>
        /// No <c>lib</c> folder and exactly one <c>.json</c> under <c>ruleset</c> is what a rule
        /// set package is. A package holding two would need something to say which of them the
        /// identifier meant, which is the lookup this whole convention exists to not have.
        /// </remarks>
        private static async Task<string> Document(HttpClient http, string ruleSet, Version version)
        {
            string lower = ruleSet.ToLowerInvariant();
            string url = $"{FlatContainer}/{lower}/{version}/{lower}.{version}.nupkg";

            using MemoryStream buffer = new(await http.GetByteArrayAsync(url));
            using ZipArchive archive = new(buffer);

            ZipArchiveEntry[] documents =
            [
                .. archive.Entries.Where(static entry =>
                    entry.FullName.StartsWith("ruleset/", StringComparison.OrdinalIgnoreCase)
                    && entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && entry.FullName.Count(static character => character is '/') is 1)
            ];

            if (documents.Length is not 1)
            {
                throw new InvalidOperationException(
                    $"it holds {documents.Length} documents in `ruleset`, and a rule set package holds one.");
            }

            using StreamReader reader = new(documents[0].Open());
            return await reader.ReadToEndAsync();
        }

        // ── reading what is already here ───────────────────────────────────────────────

        /// <summary>Which identifier each document in the folder answers to.</summary>
        /// <remarks>
        /// By what the document declares and never by its file name, the way
        /// <see cref="RuleSetFolder"/> resolves one and for the same reason. A file the identity
        /// reader refuses is passed over: a folder of rule sets holds the states and outcomes
        /// written beside them, and neither is a mistake to find.
        /// </remarks>
        private static Dictionary<string, string>? Declared(string folder)
        {
            Dictionary<string, string> declaring = new(StringComparer.Ordinal);

            if (!Directory.Exists(folder))
            {
                return declaring;
            }

            foreach (string path in Directory.EnumerateFiles(folder, "*.json"))
            {
                if (Identity(path) is not RuleSetIdentity identity)
                {
                    continue;
                }

                if (declaring.TryGetValue(identity.Id, out string? taken))
                {
                    Console.Error.WriteLine($"Two documents in '{folder}' declare '{identity.Id}':");
                    Console.Error.WriteLine($"  {Show(taken)}");
                    Console.Error.WriteLine($"  {Show(path)}");
                    return null;
                }

                declaring[identity.Id] = path;
            }

            return declaring;
        }

        private static RuleSetIdentity? Identity(string path)
        {
            try
            {
                return RuleSetIdentity.ReadFrom(File.ReadAllText(path));
            }
            catch (Exception exception)
                when (exception is RuleSetBuildException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Whether what is here is what was asked for, said where it arrived.</summary>
        /// <remarks>
        /// Both halves at once, because either alone is answered too easily: an identifier that
        /// matches says nothing about which revision this is, and a version that satisfies says
        /// nothing about whose it is. It is the check <c>CreateContext</c> makes when it
        /// compiles, made at the point where the document that failed it can be named.
        /// </remarks>
        private static bool Accepts(RuleSetIdentity identity, RuleSetRequirement entry, string holder, string what)
        {
            if (identity.Satisfies(entry))
            {
                return true;
            }

            Console.Error.WriteLine($"'{Show(holder)}' holds {entry}, and '{Show(what)}' is {identity}.");
            return false;
        }

        private static IReadOnlyList<RuleSetRequirement>? Uses(string path, string document)
        {
            try
            {
                return RuleSetRequirement.ReadFrom(document);
            }
            catch (RuleSetBuildException failure)
            {
                Console.Error.WriteLine($"'{Show(path)}' does not say what it holds:");
                Console.Error.WriteLine($"  {failure.Message}");
                return null;
            }
        }

        private static string Show(string path) =>
            path.StartsWith($".{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? path[2..] : path;

        /// <summary>One entry of a <c>uses</c>, and the document that wrote it.</summary>
        /// <remarks>
        /// The holder is carried so that a refusal can name both ends. A constraint on its own
        /// says what was wanted and not who wanted it, and in a graph three documents deep that
        /// is the half somebody needs.
        /// </remarks>
        private readonly record struct Held(RuleSetRequirement Entry, string Holder);
    }
}
