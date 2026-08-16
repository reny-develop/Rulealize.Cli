// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Cli;

// Runs a Rulealize rule set from a command line, so that trying one does not begin with
// writing a host.
//
//   rulealize restore <rule set> [--plugins <folder>]   fetch what `requires` names
//   rulealize plugins            [--plugins <folder>]   what a folder provides
//   rulealize check   <rule set> [--plugins <folder>]   does this document compile against it
//
// The folder is the seam all three share, and it is the seam a deployed application has:
// assemblies on disk, swept and instantiated. A vocabulary nobody publishes joins in by
// being copied there, which is the same arrangement as one that was fetched, and is how a
// plugin still being written is tried.

if (args.Length is 0)
{
    return Usage();
}

string folder = Option("--plugins") ?? Option("--out") ?? PluginFolder.Default;

return args switch
{
    ["restore", string ruleSet, ..] => await RestoreCommand.Run(ruleSet, folder),
    ["plugins", ..] => PluginsCommand.Run(folder),
    ["check", string ruleSet, ..] => CheckCommand.Run(ruleSet, folder),
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
    Console.Error.WriteLine("  rulealize restore <rule set> [--plugins <folder>]");
    Console.Error.WriteLine("  rulealize plugins            [--plugins <folder>]");
    Console.Error.WriteLine("  rulealize check   <rule set> [--plugins <folder>]");
    Console.Error.WriteLine();
    Console.Error.WriteLine($"The folder defaults to '{PluginFolder.Default}'.");
    return 2;
}
