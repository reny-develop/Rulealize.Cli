// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize;
using Rulealize.Abstraction;

namespace Rulealize.Cli
{
    /// <summary>A compiled rule set and the state to start from.</summary>
    /// <remarks>
    /// <para>
    /// What every command that runs something needs, and the whole of it: a folder of
    /// vocabularies, a document compiled against them, and a position. The runtime holds no
    /// state of its own — <c>ApplyToState</c> is a function of the state it was handed — so
    /// where the position comes from is entirely this tool's business, and saying which one
    /// was used is part of the answer.
    /// </para>
    /// </remarks>
    internal sealed class Session
    {
        private Session(RuleContext context, string state, string source)
        {
            Context = context;
            State = state;
            Source = source;
        }

        /// <summary>Gets the compiled rule set.</summary>
        public RuleContext Context { get; }

        /// <summary>Gets the state document to start from.</summary>
        public string State { get; }

        /// <summary>Gets where that state came from, for saying so.</summary>
        public string Source { get; }

        /// <summary>Opens a rule set against a plugin folder, at a position.</summary>
        /// <param name="ruleSetPath">The rule set document.</param>
        /// <param name="folder">The plugin folder.</param>
        /// <param name="statePath">A state document, or null for the rule set's own opening position.</param>
        /// <returns>The session, or null when why not has already been reported.</returns>
        public static Session? Open(string ruleSetPath, string folder, string? statePath)
        {
            if (!File.Exists(ruleSetPath))
            {
                Console.Error.WriteLine($"'{ruleSetPath}' does not exist.");
                return null;
            }

            if (PluginFolder.Load(folder) is not RuleRuntime runtime)
            {
                return null;
            }

            RuleContext context;
            try
            {
                context = runtime.CreateContext(File.ReadAllText(ruleSetPath));
            }
            catch (RuleSetBuildException failure)
            {
                Console.Error.WriteLine($"'{ruleSetPath}' does not compile against '{folder}':");
                Console.Error.WriteLine($"  {failure.Message}");
                return null;
            }

            if (statePath is null)
            {
                // The rule set's own opening position. Not invented here: `state.initial` is
                // something the document declares, and this is the runtime writing it out.
                return new Session(context, context.InitialState, "the initial state");
            }

            if (!File.Exists(statePath))
            {
                Console.Error.WriteLine($"'{statePath}' does not exist.");
                return null;
            }

            return new Session(context, File.ReadAllText(statePath), $"'{statePath}'");
        }

        /// <summary>Runs something that may reject the state document it was given.</summary>
        /// <typeparam name="T">What it returns.</typeparam>
        /// <param name="work">The work.</param>
        /// <param name="value">Receives the result.</param>
        /// <returns>Zero on success.</returns>
        /// <remarks>
        /// <para>
        /// A state document is the one input here that nothing checked earlier. It arrives
        /// from a file the caller wrote, and the schema it has to satisfy belongs to the rule
        /// set, so the first thing that reads it is the first thing that can refuse it.
        /// </para>
        /// <para>
        /// <see cref="IllegalInputException"/> is caught as well, and reported as flatly as
        /// the rest, because a caller who wants to say more about a refusal has to catch it
        /// before reaching here — which <c>apply</c> does. What this stops is the case where
        /// nobody has: a rule set saying no is an answer, and answering with a stack trace
        /// would make it look like a defect in the tool.
        /// </para>
        /// </remarks>
        public static int Guarded<T>(Func<T> work, out T? value)
        {
            value = default;

            try
            {
                value = work();
                return 0;
            }
            catch (Exception failure)
                when (failure is RuleDocumentException or RuleEvaluationException or IllegalInputException)
            {
                Console.Error.WriteLine(failure.Message);
                return 1;
            }
        }
    }
}
