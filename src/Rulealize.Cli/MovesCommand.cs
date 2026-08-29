// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize;

namespace Rulealize.Cli
{
    /// <summary>
    /// What is legal from a position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The moves go to standard output, one per line, and everything about them goes to
    /// standard error. A count is not a move, and a caller counting lines should get the
    /// number it asked about.
    /// </para>
    /// <para>
    /// Each line is exactly what <c>apply</c> accepts, which is the round trip
    /// <c>ValidInput</c> already guarantees: the text is what the runtime writes for an
    /// input, and matching it back is how the input document is rebuilt.
    /// </para>
    /// </remarks>
    internal static class MovesCommand
    {
        public static int Run(
            string ruleSetPath,
            string folder,
            string? ruleSets,
            string? statePath,
            int limit,
            bool json)
        {
            if (Session.Open(ruleSetPath, folder, ruleSets, statePath) is not Session session)
            {
                return 1;
            }

            if (Session.Guarded(() => session.Context.GetValidInputs(session.State, limit), out ValidInputSet? moves)
                is not 0)
            {
                return 1;
            }

            if (json)
            {
                Console.WriteLine(moves!.ToJson());
                return 0;
            }

            if (Session.Guarded(() => session.Context.GetTerminalStatus(session.State), out TerminalStatus status)
                is not 0)
            {
                return 1;
            }

            Console.Error.WriteLine($"{session.Context.RuleSet} from {session.Source} ({status})");

            foreach (ValidInput move in moves!)
            {
                Console.WriteLine(move);
            }

            Console.Error.WriteLine(moves.Count is 0
                ? "no legal input"
                : $"{PluginFolder.Count(moves.Count, "legal input", "legal inputs")}, "
                    + $"{moves.Evaluated} candidates evaluated");

            // Silence here would be the worst of the failures this command can have: a short
            // list that looks like an answer. GetValidInputs stops at the limit and says so,
            // and so does this.
            if (moves.Truncated)
            {
                Console.Error.WriteLine($"truncated at {limit} candidates -- pass --limit to raise it");
            }

            return 0;
        }
    }
}
