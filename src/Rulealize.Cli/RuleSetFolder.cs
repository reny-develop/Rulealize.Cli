// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize;
using Rulealize.Abstraction;

namespace Rulealize.Cli
{
    /// <summary>The documents a rule set holds, gathered from a folder of them.</summary>
    /// <remarks>
    /// <para>
    /// A rule set's <c>uses</c> names rule sets by identifier and <c>CreateContext</c> is
    /// handed their documents, so something has to turn the first into the second. A folder of
    /// them is the seam here, the way a folder of assemblies is the seam for vocabularies, and
    /// the folder the document sits in is where this looks unless <c>--rulesets</c> says
    /// otherwise.
    /// </para>
    /// <para>
    /// Nothing here fetches. A document an author keeps beside the one that holds it and a
    /// document <see cref="RuleSetFetch"/> put there are the same file to this, which is what
    /// makes <c>restore</c> the only command that has to reach the network — it fills the
    /// folder, and everything else reads one.
    /// </para>
    /// <para>
    /// Which document answers to an identifier is read out of the document and not off its
    /// file name. A name is the author's business; <c>id</c> is what the document declares
    /// and what <c>uses</c> names. Files that are not rule sets — the state and outcome
    /// documents that live in the same folder — carry no <c>id</c> and are passed over, the
    /// way a plugin sweep passes over an assembly it cannot use.
    /// </para>
    /// <para>
    /// No version is chosen here. An identifier has one document in a folder, so there is
    /// nothing to choose between — choosing happens where a version could be had, which is
    /// <see cref="RuleSetFetch"/>, and it is <see cref="RuleSetRequirement.Choose"/> that does
    /// it. Whether the document that is here satisfies what its holder asked for is checked
    /// there too, before this runs; a folder that reaches this and disagrees with itself is
    /// one <c>restore</c> was never pointed at, and the runtime refuses it when it compiles.
    /// </para>
    /// </remarks>
    internal sealed class RuleSetFolder
    {
        private static readonly RuleSetFolder Nothing =
            new(new Dictionary<string, string>(StringComparer.Ordinal), []);

        private readonly IReadOnlyList<string> _held;

        private RuleSetFolder(IReadOnlyDictionary<string, string> documents, IReadOnlyList<string> held)
        {
            Documents = documents;
            _held = held;
        }

        /// <summary>Gets the document of every rule set reachable through <c>uses</c>, by identifier.</summary>
        /// <remarks>Empty for a document that holds nothing, which is every rule set with no <c>uses</c>.</remarks>
        public IReadOnlyDictionary<string, string> Documents { get; }

        /// <summary>Gathers what a rule set holds, and what those hold in turn.</summary>
        /// <param name="ruleSetPath">The document that was asked about.</param>
        /// <param name="ruleSetDocument">Its text.</param>
        /// <param name="folder">Where to look, or null for the folder the document is in.</param>
        /// <returns>The documents, or null when why not has already been reported.</returns>
        /// <remarks>
        /// A document a document holds may hold documents of its own, so this walks the graph
        /// rather than reading one <c>uses</c>. An identifier already gathered is not read
        /// again — two documents may hold one third — which is also what stops a walk of
        /// documents that hold one another. The cycle itself is the runtime's to refuse, and
        /// it does when it compiles, naming the documents in it.
        /// </remarks>
        public static RuleSetFolder? Gather(string ruleSetPath, string ruleSetDocument, string? folder)
        {
            if (Uses(ruleSetPath, ruleSetDocument) is not IReadOnlyList<RuleSetRequirement> uses)
            {
                return null;
            }

            // A document that holds nothing costs nothing: no folder is read, and a rule set
            // written before composition existed behaves exactly as it did.
            if (uses.Count is 0)
            {
                return Nothing;
            }

            string where = folder ?? Folder(ruleSetPath);

            if (!Directory.Exists(where))
            {
                Console.Error.WriteLine($"'{ruleSetPath}' holds other rule sets, and '{where}' does not exist.");
                return null;
            }

            if (Index(where) is not Dictionary<string, Document> index)
            {
                return null;
            }

            Dictionary<string, string> documents = new(StringComparer.Ordinal);
            List<string> held = [];
            List<RuleSetRequirement> missing = [];
            Queue<RuleSetRequirement> pending = new(uses);

            while (pending.Count is not 0)
            {
                RuleSetRequirement requirement = pending.Dequeue();

                if (documents.ContainsKey(requirement.RuleSet))
                {
                    continue;
                }

                if (!index.TryGetValue(requirement.RuleSet, out Document document))
                {
                    missing.Add(requirement);
                    continue;
                }

                documents[requirement.RuleSet] = document.Text;
                held.Add($"{requirement.RuleSet}@{document.Version} ('{document.Path}')");

                if (Uses(document.Path, document.Text) is not IReadOnlyList<RuleSetRequirement> nested)
                {
                    return null;
                }

                foreach (RuleSetRequirement further in nested)
                {
                    pending.Enqueue(further);
                }
            }

            if (missing.Count is not 0)
            {
                Console.Error.WriteLine($"'{ruleSetPath}' holds rule sets that are not in '{where}':");
                foreach (RuleSetRequirement unfound in missing)
                {
                    Console.Error.WriteLine($"  {unfound}");
                }

                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    "A held rule set is a document in that folder declaring that identifier, and");
                Console.Error.WriteLine(
                    "--rulesets <folder> says where to look when they are kept somewhere else.");
                return null;
            }

            return new RuleSetFolder(documents, held);
        }

        /// <summary>Says what is held, for a command whose answer covers more than one document.</summary>
        /// <remarks>
        /// Worth saying rather than leaving to be inferred. A composite compiles its
        /// components, so an answer about one document is an answer about all of them, and
        /// which files those were is the part nothing else on the line says.
        /// </remarks>
        public void Report()
        {
            if (_held.Count is 0)
            {
                return;
            }

            Console.WriteLine($"holding {PluginFolder.Count(_held.Count, "rule set", "rule sets")}:");
            foreach (string line in _held)
            {
                Console.WriteLine($"  {line}");
            }
        }

        private static IReadOnlyList<RuleSetRequirement>? Uses(string path, string document)
        {
            try
            {
                return RuleSetRequirement.ReadFrom(document);
            }
            catch (RuleSetBuildException failure)
            {
                Console.Error.WriteLine($"'{path}' does not say what it holds:");
                Console.Error.WriteLine($"  {failure.Message}");
                return null;
            }
        }

        private static string Folder(string ruleSetPath) =>
            Path.GetDirectoryName(ruleSetPath) is { Length: > 0 } directory ? directory : ".";

        private static Dictionary<string, Document>? Index(string folder)
        {
            Dictionary<string, Document> index = new(StringComparer.Ordinal);

            foreach (string path in Directory.EnumerateFiles(folder, "*.json"))
            {
                if (Identity(path) is not (string id, string version, string text))
                {
                    continue;
                }

                string shown = Show(path);

                if (index.TryGetValue(id, out Document taken))
                {
                    // An identifier has one document, the way it has one owner everywhere
                    // else. Taking whichever the file system listed first would settle
                    // something no document said, and settle it quietly.
                    Console.Error.WriteLine($"Two documents in '{folder}' declare '{id}':");
                    Console.Error.WriteLine($"  {taken.Path}");
                    Console.Error.WriteLine($"  {shown}");
                    return null;
                }

                index[id] = new Document(shown, version, text);
            }

            return index;
        }

        private static (string Id, string Version, string Text)? Identity(string path)
        {
            // Speculative, so anything unreadable is passed over: a folder of rule sets holds
            // the states and outcomes written beside them, and neither is a mistake to find.
            // A document meant to be a rule set and unreadable is not lost for long — the
            // walk above reports the identifier as missing, naming the document that wanted it.
            try
            {
                string text = File.ReadAllText(path);

                using JsonDocument document = JsonDocument.Parse(
                    text,
                    new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                if (document.RootElement.ValueKind is not JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("id", out JsonElement id)
                    || id.ValueKind is not JsonValueKind.String)
                {
                    return null;
                }

                string version = document.RootElement.TryGetProperty("version", out JsonElement declared)
                    && declared.ValueKind is JsonValueKind.String
                        ? declared.GetString()!
                        : "?";

                return (id.GetString()!, version, text);
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string Show(string path) =>
            path.StartsWith($".{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? path[2..] : path;

        private readonly record struct Document(string Path, string Version, string Text);
    }
}
