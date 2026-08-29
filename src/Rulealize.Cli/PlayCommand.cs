// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using Rulealize;

namespace Rulealize.Cli
{
    /// <summary>
    /// Walks a rule set from a position, holding the state in memory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same two calls <c>moves</c> and <c>apply</c> make, in a loop, with the state
    /// staying where <c>ApplyToState</c> returned it instead of going to a file. No file is
    /// written and none is needed, which is the point: a rule set can be walked end to end
    /// by somebody who has not decided to keep anything yet.
    /// </para>
    /// <para>
    /// That the state lives in memory here and in a file there is not two designs. The
    /// runtime holds no state either way; these are two answers to the same question of
    /// where the caller puts what it was handed back.
    /// </para>
    /// </remarks>
    internal static class PlayCommand
    {
        public static int Run(
            string ruleSetPath,
            string folder,
            string? ruleSets,
            string? statePath,
            int limit,
            int outcomeLimit)
        {
            if (Session.Open(ruleSetPath, folder, ruleSets, statePath) is not Session session)
            {
                return 1;
            }

            RuleContext context = session.Context;
            string state = session.State;

            Console.WriteLine($"{context.RuleSet} from {session.Source}");
            Console.WriteLine("Choose by number. 'state' prints the position, 'q' stops.");

            while (true)
            {
                Console.WriteLine();

                if (Session.Guarded(() => context.GetTerminalStatus(state), out TerminalStatus status) is not 0)
                {
                    return 1;
                }

                if (status.IsTerminal)
                {
                    Console.WriteLine(status.Result is null ? "terminal." : $"terminal: {status.Result}");
                    return 0;
                }

                if (Session.Guarded(() => context.GetValidInputs(state, limit), out ValidInputSet? moves) is not 0)
                {
                    return 1;
                }

                if (moves!.Count is 0)
                {
                    // Not terminal and nothing to do. The rule set says the position is live
                    // and offers no way out of it, which is a fact about the rule set worth
                    // stopping on rather than looping over.
                    Console.WriteLine("No legal input, and the position is not terminal.");
                    return 0;
                }

                for (int i = 0; i < moves.Count; i++)
                {
                    Console.WriteLine($"  {i + 1,3}. {moves[i]}");
                }

                if (moves.Truncated)
                {
                    Console.WriteLine($"       truncated at {limit} candidates -- pass --limit to raise it");
                }

                Console.Write("> ");
                string? line = Console.ReadLine();

                if (line is null or "q" or "quit")
                {
                    return 0;
                }

                if (line is "state")
                {
                    Console.WriteLine(state);
                    continue;
                }

                if (!int.TryParse(line, out int choice) || choice < 1 || choice > moves.Count)
                {
                    Console.WriteLine($"Choose 1 to {moves.Count}.");
                    continue;
                }

                string input = moves[choice - 1].ToInputDocument(context.RuleSet);
                string before = state;

                // GetValidInputs said who may do what; this says what may then happen. An
                // input that draws nothing has exactly one outcome, of probability one, so
                // the shape of this loop is the same whether the rule set has chance in it
                // or not -- only whether there is anything to ask about differs.
                if (Session.Guarded(
                        () => context.GetOutcomes(input, before, outcomeLimit),
                        out OutcomeSet? outcomes)
                    is not 0)
                {
                    return 1;
                }

                if (outcomes!.Count is 1)
                {
                    state = outcomes[0].Result.State;
                    continue;
                }

                // Nothing here rolls anything. The alternatives were enumerated by the
                // runtime and one of them is chosen by whoever is playing, which is the same
                // arrangement a host with a random number generator in it has.
                Console.WriteLine();
                Console.WriteLine("  Nobody chooses what happens next. Which did?");
                for (int i = 0; i < outcomes.Count; i++)
                {
                    Console.WriteLine($"  {i + 1,3}. {outcomes[i]}");
                }

                if (outcomes.Truncated)
                {
                    Console.WriteLine(
                        $"       these cover {outcomes.Coverage.ToString("0.###", CultureInfo.InvariantCulture)}"
                        + " of the probability -- pass --outcomes to raise it");
                }

                Console.Write("> ");
                string? which = Console.ReadLine();

                if (which is null or "q" or "quit")
                {
                    return 0;
                }

                if (!int.TryParse(which, out int branch) || branch < 1 || branch > outcomes.Count)
                {
                    Console.WriteLine($"Choose 1 to {outcomes.Count}.");
                    continue;
                }

                state = outcomes[branch - 1].Result.State;
            }
        }
    }
}
