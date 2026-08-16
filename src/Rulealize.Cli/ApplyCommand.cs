// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize;

namespace Rulealize.Cli
{
    /// <summary>
    /// Applies one input to a state and writes the state it arrived at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The new state goes to standard output and everything said about it to standard error,
    /// so that redirecting gives a state document and nothing else. With <c>--write</c> the
    /// state file is amended in place instead, which is what turns a sequence of moves into
    /// one file name rather than one per move.
    /// </para>
    /// <para>
    /// An input is named the way <c>moves</c> prints it. Nothing parses that text: the legal
    /// inputs are enumerated and the one whose text matches is asked for its document, so
    /// what can be named is exactly what was offered. That leaves the one thing this cannot
    /// express — an input that ought to be refused — to <c>--input</c> and a document.
    /// </para>
    /// </remarks>
    internal static class ApplyCommand
    {
        public static int Run(
            string ruleSetPath,
            string? named,
            string? inputPath,
            string folder,
            string? statePath,
            int limit,
            bool write)
        {
            if (named is null && inputPath is null)
            {
                Console.Error.WriteLine("apply needs an input: either the text 'moves' printed, or --input <file>.");
                return 2;
            }

            if (write && statePath is null)
            {
                Console.Error.WriteLine("--write needs --state <file>: there is no file to write back to.");
                return 2;
            }

            if (Session.Open(ruleSetPath, folder, statePath) is not Session session)
            {
                return 1;
            }

            string inputDocument;

            if (inputPath is not null)
            {
                if (!File.Exists(inputPath))
                {
                    Console.Error.WriteLine($"'{inputPath}' does not exist.");
                    return 1;
                }

                inputDocument = File.ReadAllText(inputPath);
            }
            else if (Resolve(session, named!, limit) is string resolved)
            {
                inputDocument = resolved;
            }
            else
            {
                return 1;
            }

            if (Session.Guarded(
                    () => session.Context.ApplyToState(inputDocument, session.State),
                    out TransitionResult? result) is not 0)
            {
                return 1;
            }

            Console.Error.WriteLine($"{named ?? inputPath} applied to {session.Source}"
                + (result!.IsTerminal ? $" -- terminal{(result.Result is null ? string.Empty : $" ({result.Result})")}" : string.Empty));

            if (write)
            {
                File.WriteAllText(statePath!, result.State);
                Console.Error.WriteLine($"'{statePath}' updated");
                return 0;
            }

            Console.WriteLine(result.State);
            return 0;
        }

        private static string? Resolve(Session session, string named, int limit)
        {
            if (Session.Guarded(() => session.Context.GetValidInputs(session.State, limit), out ValidInputSet? moves)
                is not 0)
            {
                return null;
            }

            foreach (ValidInput move in moves!)
            {
                if (string.Equals(move.ToString(), named, StringComparison.Ordinal))
                {
                    return move.ToInputDocument(session.Context.RuleSet);
                }
            }

            Console.Error.WriteLine($"'{named}' is not legal from {session.Source}.");

            if (moves.Count is 0)
            {
                Console.Error.WriteLine("Nothing is.");
                return null;
            }

            Console.Error.WriteLine("These are:");
            foreach (ValidInput move in moves)
            {
                Console.Error.WriteLine($"  {move}");
            }

            // The distinction is worth drawing: a refusal that is the rule set working is not
            // the same as one that is a typo, and only a document can ask for the first.
            Console.Error.WriteLine();
            Console.Error.WriteLine("To apply something the rule set refuses -- to check that it does -- write the");
            Console.Error.WriteLine("input document and pass --input <file>.");
            return null;
        }
    }
}
