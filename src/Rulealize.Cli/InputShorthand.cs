// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize;

namespace Rulealize.Cli
{
    /// <summary>
    /// An input named the way <c>moves</c> prints it, read back as a name and its arguments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This reads the text; it never builds a document from it. What it produces is a
    /// question to ask each candidate — is this you? — so what can be named is still exactly
    /// what <c>GetValidInputs</c> offered, and an input the rule set does not offer cannot be
    /// assembled here by writing it out carefully.
    /// </para>
    /// <para>
    /// Reading it is what makes the arguments a set rather than a spelling. Comparing
    /// renderings made the order of the arguments part of the name, which is a contract the
    /// text does not carry and the caller cannot see: two arguments the other way round is
    /// the same move, and telling somebody it is not sends them looking for a bug in their
    /// rule set.
    /// </para>
    /// <para>
    /// A value with a comma in it would be split in the wrong place, so this fails rather
    /// than guessing, and the exact rendering still matches. That is the whole reason
    /// <see cref="Parse"/> returns nothing instead of a best effort: a shorthand that half
    /// parsed would match the wrong candidate, and no shorthand at all matches none.
    /// </para>
    /// </remarks>
    internal sealed class InputShorthand
    {
        private InputShorthand(string input, ImmutableArray<KeyValuePair<string, string>> arguments)
        {
            Input = input;
            Arguments = arguments;
        }

        /// <summary>Gets the name of the input, as written.</summary>
        public string Input { get; }

        /// <summary>Gets the arguments, in whatever order they were written.</summary>
        public ImmutableArray<KeyValuePair<string, string>> Arguments { get; }

        /// <summary>Reads a shorthand, or nothing where the text is not one.</summary>
        /// <param name="text">The text as it was written on the command line.</param>
        /// <returns>The shorthand, or <see langword="null"/>.</returns>
        public static InputShorthand? Parse(string text)
        {
            string trimmed = text.Trim();

            if (trimmed.Length is 0)
            {
                return null;
            }

            int open = trimmed.IndexOf('(', StringComparison.Ordinal);

            if (open < 0)
            {
                // An input with no parameters is its own name, which is what `moves` prints
                // for one.
                return Named(trimmed) ? new InputShorthand(trimmed, []) : null;
            }

            if (trimmed[^1] is not ')')
            {
                return null;
            }

            string name = trimmed[..open].Trim();

            if (!Named(name))
            {
                return null;
            }

            string inside = trimmed[(open + 1)..^1].Trim();

            if (inside.Length is 0)
            {
                return new InputShorthand(name, []);
            }

            ImmutableArray<KeyValuePair<string, string>>.Builder arguments =
                ImmutableArray.CreateBuilder<KeyValuePair<string, string>>();

            foreach (string part in inside.Split(','))
            {
                int colon = part.IndexOf(':', StringComparison.Ordinal);

                if (colon < 0)
                {
                    return null;
                }

                string key = part[..colon].Trim();

                if (!Named(key) || arguments.Any(written => string.Equals(written.Key, key, StringComparison.Ordinal)))
                {
                    return null;
                }

                arguments.Add(new KeyValuePair<string, string>(key, part[(colon + 1)..].Trim()));
            }

            return new InputShorthand(name, arguments.ToImmutable());
        }

        /// <summary>Says whether this names that candidate.</summary>
        /// <param name="candidate">One of the inputs the rule set offered.</param>
        /// <returns><see langword="true"/> when it is the one written.</returns>
        public bool Matches(ValidInput candidate)
        {
            if (!string.Equals(candidate.Input, Input, StringComparison.Ordinal)
                || candidate.Arguments.Count != Arguments.Length)
            {
                return false;
            }

            // By name, so writing them in the other order names the same move. The counts
            // being equal already, matching every argument written leaves none unaccounted
            // for on either side.
            foreach ((string key, string value) in Arguments)
            {
                if (!candidate.Arguments.TryGetValue(key, out string? offered)
                    || !string.Equals(offered, value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Says whether a token could be a name rather than the middle of a value.</summary>
        /// <remarks>
        /// Deliberately loose: this is not the rule set's grammar and has no business
        /// enforcing one. It rejects what a mis-split would produce -- something with a space
        /// or a bracket in it -- and lets everything else through to be matched or not.
        /// </remarks>
        private static bool Named(string token) =>
            token.Length is not 0
            && !token.Any(static character =>
                char.IsWhiteSpace(character) || character is '(' or ')' or ',' or ':');
    }
}
