# Rulealize.Cli

Works with [Rulealize](https://github.com/reny-develop/Rulealize) rule sets and the
vocabularies they draw on, from a command line.

```sh
dotnet tool install -g Rulealize.Cli
```

| | |
| --- | --- |
| `rulealize restore <rule set>` | fetch what the document's `requires` names, into a folder |
| `rulealize plugins` | what a folder of vocabularies provides, or why it provides nothing |
| `rulealize check <rule set>` | does this document compile against that folder |

All three take `--plugins <folder>`, and it defaults to `plugin`.

Requires `net10.0` and the .NET SDK, which a `dotnet tool` implies.

## The folder is the seam

Every command works against a folder of assemblies, because that is the arrangement a
deployed application has: they are swept, instantiated and registered, and nothing else
about a vocabulary is consulted. Two things follow, and both are the point.

**A vocabulary nobody publishes joins in by being copied there.** `restore` fills the folder
from nuget.org; a plugin still being written fills it with `dotnet build` and a copy. From
that moment the two are indistinguishable, which is what makes the folder worth being the
seam.

**What goes wrong here goes wrong in production too.** A class left internal will not load
either place. The difference is that here something is willing to say so.

## `rulealize plugins`

```
plugin
  13 assemblies, 13 vocabularies

  My.Vocabulary 1.0.0  (my)
      my.length                    expression

  Rulealize.Plugin.Binding 1.0.0  (bind)  @
      bind.let                     expression
      bind.local                   expression
  …
69 operations in total.
```

The operations are read back off the loaded runtime — `RuleRuntime.Operations` — and not
off anybody's source. Nothing declares what a vocabulary provides: the operations exist only
as the calls it made while registering, so this list is the sole account of them, and it is
the one a rule set will be compiled against. **An operation that was written but never
registered is missing from it**, which is the cheapest way there is to find a forgotten
`AddExpression`.

When a folder holds assemblies and yields no vocabulary, the runtime does not say why — a
sweep is speculative, since pointing one at an application's own output must not be an
error, so a type it cannot use is passed over in silence. That is right for the runtime and
useless here, where somebody asked on purpose. So this asks again, itself:

```
notpublic
  1 assembly, 0 vocabularies

Nothing loaded, and the runtime does not say why: a folder sweep passes
over what it cannot use. What is in there:

  MyVocabulary.Vocabulary
      is not public. A sweep only takes public types.
```

The conditions are public, not nested, not abstract, and a parameterless constructor.

## `rulealize check`

The same compile `restore` ends with, on its own and without the network.

```
'probe.json' compiles against 'plugin' (13 vocabularies, 69 operations).
```

```
'typo.json' does not compile against 'plugin':
  /inputs/measure/effects[0]/value: 'my.lenght' is not an operation any loaded plugin
  provides. Check the rule set's 'requires'.
```

Restoring is something a document needs once. This is the question asked after every edit,
and a loop that reached nuget.org to answer it is a loop nobody would run.

## `rulealize restore`

```sh
rulealize restore reversi.json
```

```
  Rulealize.Plugin.Arithmetic 1.0.0
  Rulealize.Plugin.Binding 1.0.0
  …
10 plugins -> plugin
'reversi.json' compiles against it.
```

## Why

A rule set already says what it needs. `requires` names every vocabulary the document draws
on and which versions of each will do, in a form the runtime reads to refuse a document it
cannot run — so transcribing that list into `dotnet add package` lines by hand is copying a
dependency list that already exists, in a place where it can drift from the original.

## What decides what

**Rulealize decides which versions.** `PluginRequirement.ReadFrom` reads the constraints and
`PluginResolution.Resolve` chooses; both are pure, and both are the code `CreateContext`
checks a loaded plugin against. A second reading of `^1.0` living in this tool would
eventually differ from the runtime's, and it would show up as a folder this assembled and the
runtime refused — a fault with no symptom until the moment it is too late to be useful.

Two rules come with that, and neither is this tool's to change:

| | |
| --- | --- |
| the lowest satisfying version wins | so that the same document restores to the same folder after three more releases. Moving to a newer one means changing what the document asks for, and restoring a document should not rewrite it |
| constraints on one plugin are met together | a folder cannot hold two versions of one assembly |

**nuget.org supplies the packages**, and this does not talk to
[the registry](https://github.com/reny-develop/Rulealize.Registry) at all. Restoring needs
the published versions of a plugin the document already named, and the feed answers that; the
registry's catalogue is for finding a plugin you could not already name.

**The SDK does the fetching**, with the versions pinned exactly so that delegating decides
nothing. NuGet then gets target frameworks and a plugin's own dependencies right — which this
could only approximate — and what lands in the folder is what `dotnet publish` would have put
there, which is the arrangement a deployed application actually has.

## What it will not do

**Fetch a vocabulary nobody publishes.** A rule set may name one, and
[it is a supported arrangement](https://github.com/reny-develop/Rulealize/blob/main/doc/plugin.md#a-vocabulary-that-is-not-distributed):
implement `IRulealizePlugin` in your own assembly and hand an instance to `AddPlugin`. Such a
document is refused with the missing vocabulary named, and with a count of what would have
resolved, because that half is the useful part of the answer.

**Rewrite your document.** Restoring means fetching what it asks for. Asking for something
else is an edit, and an edit is yours.

## The one assumption

A plugin identifier and its NuGet package identifier are the same string. That is a
convention rather than something the runtime enforces — `requires` names a
`PluginManifest.Id`, and where the package with that name lives is nobody's business but the
publisher's — and it is the only thing this command assumes about where a plugin comes from.

## Verification

The folder is only right if it runs the document, so the last thing `restore` does is load
the folder and compile the rule set against it, the way an application will. It costs one
compile and turns *the packages downloaded* into *this rule set loads against what is now on
disk*.

If that fails, the folder is left where it was written and the compiler's own message is
printed. A document that does not compile is still a document you may want the plugins for.

## License

Apache-2.0.
