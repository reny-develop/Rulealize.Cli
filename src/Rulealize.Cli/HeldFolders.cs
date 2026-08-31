// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize.Cli
{
    /// <summary>Where the documents a rule set holds are looked for, and which of them wins.</summary>
    /// <remarks>
    /// <para>
    /// Two folders, because a component is one of two things and they do not belong in one
    /// place. One you wrote is <b>source</b>: it sits beside the document that holds it, in
    /// your repository, and nothing here may touch it. One somebody published is
    /// <b>fetched</b>: it belongs where a restored dependency belongs, which is a folder the
    /// tool owns and your history ignores — the same place a plugin goes, and for the same
    /// reason no package manager writes a dependency into <c>src</c>.
    /// </para>
    /// <para>
    /// Until 0.9.0 there was one folder and <c>restore</c> wrote into it, which meant fetching
    /// what a <c>uses</c> named put somebody else's documents next to yours. For a rule set
    /// published from a repository laid out the ordinary way, that folder is also the one
    /// being packed, so the next <c>dotnet pack</c> shipped them.
    /// </para>
    /// <para>
    /// <b>Yours wins.</b> A document beside the one that holds it shadows anything fetched
    /// under the same identifier, because a component you are writing is the one you meant —
    /// and a restore that silently preferred the published copy would be a restore that
    /// undid your afternoon.
    /// </para>
    /// </remarks>
    internal static class HeldFolders
    {
        /// <summary>Where <c>restore</c> writes, and where every command reads after it.</summary>
        /// <remarks>
        /// Beside <c>plugin</c>, and named for what the runtime calls these: a composite holds
        /// components. Not <c>ruleset</c>, which is what a package calls the folder holding the
        /// document it ships — one word for the thing you publish and the things you fetched
        /// would be one word too few.
        /// </remarks>
        public const string Fetched = "component";

        /// <summary>The folders a held rule set is resolved from, nearest first.</summary>
        /// <param name="ruleSetPath">The document that holds them.</param>
        /// <param name="ruleSets">What <c>--rulesets</c> said, or null.</param>
        /// <returns>
        /// The document's own folder, then the fetched one. The same folder twice is one
        /// folder, which is what <c>--rulesets .</c> beside a document in <c>.</c> asks for.
        /// </returns>
        public static IReadOnlyList<string> Reading(string ruleSetPath, string? ruleSets)
        {
            string own = Own(ruleSetPath);
            string fetched = Writing(ruleSets);

            return string.Equals(Path.GetFullPath(own), Path.GetFullPath(fetched), StringComparison.OrdinalIgnoreCase)
                ? [own]
                : [own, fetched];
        }

        /// <summary>The one folder <c>restore</c> may write a fetched document into.</summary>
        /// <remarks>
        /// Never the document's own. <c>--rulesets</c> moves it, which is how a project that
        /// wants its components somewhere else says so, and it is still not a source folder
        /// unless somebody points it at one on purpose.
        /// </remarks>
        public static string Writing(string? ruleSets) => ruleSets ?? Fetched;

        /// <summary>The folder a document sits in, which is where its author's components are.</summary>
        public static string Own(string ruleSetPath) =>
            Path.GetDirectoryName(ruleSetPath) is { Length: > 0 } directory ? directory : ".";
    }
}
