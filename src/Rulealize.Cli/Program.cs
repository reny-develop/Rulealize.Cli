// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using Rulealize.Cli;

// Runs a Rulealize rule set from a command line, so that trying one does not begin with
// writing a host.
//
//   rulealize restore <rule set>            fetch what `requires` names, into a folder
//   rulealize plugins                       what a folder of vocabularies provides
//   rulealize check   <rule set>            does this document compile against it
//   rulealize moves   <rule set>            what is legal from a position
//   rulealize apply   <rule set> <input>    apply one, and write the state it reached
//   rulealize play    <rule set>            walk it, holding the position in memory
//
// The folder is the seam they share, and it is the seam a deployed application has:
// assemblies on disk, swept and instantiated. A vocabulary nobody publishes joins in by
// being copied there, which is how a plugin still being written is tried.
//
// The runtime holds no state. ApplyToState is a function of the state it was handed, so
// where a position comes from and where the next one goes are this tool's decisions and
// nobody else's: standard output by default, a file with --write, and memory in `play`.

const int DefaultLimit = 10000;

if (args.Length is 0)
{
    return Usage();
}

string folder = Option("--plugins") ?? Option("--out") ?? PluginFolder.Default;
string? statePath = Option("--state");
string? inputPath = Option("--input");
bool json = args.Contains("--json");
bool write = args.Contains("--write");

int limit = DefaultLimit;
if (Option("--limit") is string text)
{
    // GetValidInputs refuses a limit of zero or less, and refusing it here says so where
    // the number was written rather than where it was used.
    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
    {
        Console.Error.WriteLine($"--limit takes a positive whole number, not '{text}'.");
        return 2;
    }

    limit = parsed;
}

return args switch
{
    ["restore", string ruleSet, ..] => await RestoreCommand.Run(ruleSet, folder),
    ["plugins", ..] => PluginsCommand.Run(folder),
    ["check", string ruleSet, ..] => CheckCommand.Run(ruleSet, folder),
    ["moves", string ruleSet, ..] => MovesCommand.Run(ruleSet, folder, statePath, limit, json),
    ["apply", string ruleSet, string named, ..] when !named.StartsWith("--", StringComparison.Ordinal) =>
        ApplyCommand.Run(ruleSet, named, inputPath, folder, statePath, limit, write),
    ["apply", string ruleSet, ..] => ApplyCommand.Run(ruleSet, null, inputPath, folder, statePath, limit, write),
    ["play", string ruleSet, ..] => PlayCommand.Run(ruleSet, folder, statePath, limit),
    _ => Usage()
};

string? Option(string name)
{
    int at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

static int Usage()
{
    Console.Error.WriteLine("usage:");
    Console.Error.WriteLine("  rulealize restore <rule set>");
    Console.Error.WriteLine("  rulealize plugins");
    Console.Error.WriteLine("  rulealize check   <rule set>");
    Console.Error.WriteLine("  rulealize moves   <rule set>");
    Console.Error.WriteLine("  rulealize apply   <rule set> <input> | --input <file>");
    Console.Error.WriteLine("  rulealize play    <rule set>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  --plugins <folder>   where the vocabularies are        (default 'plugin')");
    Console.Error.WriteLine("  --state <file>       the position to start from        (default the rule set's)");
    Console.Error.WriteLine("  --limit <n>          candidates GetValidInputs may try (default 10000)");
    Console.Error.WriteLine("  --write              amend --state in place instead of writing to standard output");
    Console.Error.WriteLine("  --json               moves, as the runtime writes them");
    return 2;
}
