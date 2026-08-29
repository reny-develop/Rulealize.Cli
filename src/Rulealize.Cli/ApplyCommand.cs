// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using Rulealize;
using Rulealize.Abstraction;

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
    /// An input is named the way <c>moves</c> prints it: the legal inputs are enumerated and
    /// the one the text names is asked for its document, so what can be named is exactly what
    /// was offered. The text is read far enough to tell which candidate it means — a name and
    /// its arguments by name — and no further. Nothing is built from it, so an input the rule
    /// set does not offer cannot be assembled here however carefully it is written, and that
    /// one thing — an input that ought to be refused — stays with <c>--input</c> and a
    /// document.
    /// </para>
    /// </remarks>
    internal static class ApplyCommand
    {
        public static int Run(
            string ruleSetPath,
            string? named,
            string? inputPath,
            string? outcomePath,
            string folder,
            string? ruleSets,
            string? statePath,
            int limit,
            int outcomeLimit,
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

            if (Session.Open(ruleSetPath, folder, ruleSets, statePath) is not Session session)
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

            string? outcomeDocument = null;

            if (outcomePath is not null)
            {
                if (!File.Exists(outcomePath))
                {
                    Console.Error.WriteLine($"'{outcomePath}' does not exist.");
                    return 1;
                }

                outcomeDocument = File.ReadAllText(outcomePath);
            }

            // Applied here rather than through Session.Guarded, which reports every failure
            // the same way. A refusal is not a failure of the same kind, and this is the one
            // command with something better to say about it.
            TransitionResult result;

            try
            {
                result = outcomeDocument is null
                    ? session.Context.ApplyToState(inputDocument, session.State)
                    : session.Context.ApplyToState(inputDocument, session.State, outcomeDocument);
            }
            catch (IllegalInputException refused)
            {
                // Not a malformed document and not a fault: everything read and evaluated
                // cleanly, and the rule set's answer was no. Arriving here through --input is
                // the supported way of asking whether it says no, so the refusal is reported
                // as the answer it is -- with a non-zero status, because no state came of it.
                Console.Error.WriteLine($"refused from {session.Source}: {refused.Message}");
                Report(session, limit);
                return 1;
            }
            catch (InvalidOperationException)
            {
                // The rule set says the next state is not the mover's alone to settle, and
                // this is the overload that cannot say which of them happened.
                return Enumerate(session, inputDocument, named ?? inputPath!, outcomeLimit);
            }
            catch (Exception failure) when (failure is RuleDocumentException or RuleEvaluationException)
            {
                Console.Error.WriteLine(failure.Message);
                return 1;
            }

            Console.Error.WriteLine($"{named ?? inputPath} applied to {session.Source}"
                + (result.IsTerminal ? $" -- terminal{(result.Result is null ? string.Empty : $" ({result.Result})")}" : string.Empty));

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

            InputShorthand? written = InputShorthand.Parse(named);

            foreach (ValidInput move in moves!)
            {
                // The rendering first, which matches whatever `moves` printed however odd a
                // value in it looks. Then the arguments by name, which is what makes the
                // order they were written in stop mattering.
                if (string.Equals(move.ToString(), named, StringComparison.Ordinal)
                    || written?.Matches(move) is true)
                {
                    return move.ToInputDocument(session.Context.RuleSet);
                }
            }

            // Not "that is not legal". The list underneath is what the rule set offers, and
            // saying illegal above a list holding the move that was named is the kind of
            // output that sends somebody looking for a bug in their rule set. Nothing here
            // knows whether the input is legal; what failed is the naming, so that is what
            // this says, and where the name landed and the arguments did not it says which.
            string? offered = written is not null
                && moves.Any(move => string.Equals(move.Input, written.Input, StringComparison.Ordinal))
                    ? written.Input
                    : null;

            Console.Error.WriteLine($"'{named}' does not name an input {session.Source} offers."
                + (offered is null ? string.Empty : $" '{offered}' is, but not with those arguments."));

            Report(moves, offered);

            // The distinction is worth drawing: a refusal that is the rule set working is not
            // the same as one that is a typo, and only a document can ask for the first.
            Console.Error.WriteLine();
            Console.Error.WriteLine("To apply something the rule set refuses -- to check that it does -- write the");
            Console.Error.WriteLine("input document and pass --input <file>.");
            return null;
        }

        private static void Report(Session session, int limit)
        {
            if (Session.Guarded(() => session.Context.GetValidInputs(session.State, limit), out ValidInputSet? moves)
                is 0)
            {
                Report(moves!);
            }
        }

        /// <summary>Lists what is legal, or the one input's worth of it that was asked about.</summary>
        /// <remarks>
        /// Narrowing to the input named is the difference between an answer and a wall: a
        /// rule set with five inputs over a handful of domains offers hundreds of moves, and
        /// somebody who got one argument wrong needs to see that argument's neighbours rather
        /// than all of them. Where the name itself was wrong there is nothing to narrow to,
        /// and the whole list is the answer.
        /// </remarks>
        private static void Report(ValidInputSet moves, string? only = null)
        {
            if (moves.Count is 0)
            {
                Console.Error.WriteLine("Nothing is legal here.");
                return;
            }

            Console.Error.WriteLine(only is null ? "These are:" : $"'{only}' is offered as:");
            foreach (ValidInput move in moves)
            {
                if (only is null || string.Equals(move.Input, only, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"  {move}");
                }
            }
        }

        /// <summary>Says what could happen, for an input this command cannot settle alone.</summary>
        /// <remarks>
        /// Neither a fault nor a bad document. The rule set says the next state is not
        /// whoever moves' to decide, and picking one of the alternatives would be this
        /// command inventing an answer nobody enumerated — which is the one thing the runtime
        /// is built not to do. So it prints what could have happened, says how to name the
        /// one that did, and stops.
        /// </remarks>
        private static int Enumerate(Session session, string inputDocument, string named, int outcomeLimit)
        {
            Console.Error.WriteLine(
                $"'{named}' resolves something nobody chose, so applying it takes an outcome as well.");

            if (Session.Guarded(
                    () => session.Context.GetOutcomes(inputDocument, session.State, outcomeLimit),
                    out OutcomeSet? outcomes)
                is not 0)
            {
                return 1;
            }

            Console.Error.WriteLine();
            Console.Error.WriteLine("What could happen, most likely first:");
            foreach (Outcome outcome in outcomes!)
            {
                Console.Error.WriteLine($"  {outcome}");
            }

            if (outcomes.Truncated)
            {
                Console.Error.WriteLine(
                    $"  -- these cover {outcomes.Coverage.ToString("0.###", CultureInfo.InvariantCulture)}"
                    + " of the probability; pass --outcomes to raise the limit");
            }

            Console.Error.WriteLine();
            Console.Error.WriteLine("Write the one that happened as a rulealize/outcome/v1 document and pass it");
            Console.Error.WriteLine("with --outcome <file>. 'play' asks rather than stopping.");
            return 2;
        }
    }
}
