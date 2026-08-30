# Rulealize.Cli

Works with [Rulealize](https://github.com/reny-develop/Rulealize) rule sets and the
vocabularies they draw on, from a command line.

```sh
dotnet tool install -g Rulealize.Cli
```

| | |
| --- | --- |
| `rulealize restore <rule set>` | fetch what `uses` and `requires` name, through the whole graph, into folders |
| `rulealize plugins` | what a folder of vocabularies provides, or why it provides nothing |
| `rulealize check <rule set>` | does this document compile against that folder |
| `rulealize moves <rule set>` | what is legal from a position |
| `rulealize apply <rule set> <input>` | apply one, and write the state it reached |
| `rulealize play <rule set>` | walk it, holding the position in memory |

```
--plugins <folder>   where the vocabularies are        (default 'plugin')
--rulesets <folder>  where the documents it holds are, and where restore puts the ones it
                     fetches                          (default the rule set's own folder)
--state <file>       the position to start from        (default the rule set's own)
--limit <n>          candidates GetValidInputs may try (default 10000)
--outcomes <n>       outcomes GetOutcomes may return   (default 64)
--outcome <file>     what was drawn, for an input that resolves something nobody chose
--write              amend --state in place instead of writing to standard output
--json               moves, as the runtime writes them
```

Requires `net10.0` and the .NET SDK, which a `dotnet tool` implies.

## Trying a rule set

```
$ rulealize restore reversi.json
10 plugins -> plugin
'reversi.json' compiles against it.

$ rulealize play reversi.json
reversi@1.0.0 from the initial state
Choose by number. 'state' prints the position, 'q' stops.

    1. place(at: e6)
    2. place(at: f5)
    3. place(at: c4)
    4. place(at: d3)
> 4

    1. place(at: c5)
    2. place(at: c3)
    3. place(at: e3)
>
```

Two commands, no host program, and no file written but the plugin folder.

## Where the state lives

**Nowhere, until you say.** `ApplyToState` is a function of the state it was handed, and a
`RuleContext` holds a rule set and no position at all, so every command here has to be told
where a position comes from and told what to do with the one it produced. There is no hidden
current game.

| | |
| --- | --- |
| told nothing | the rule set's own `state.initial`, which is the document's opening position and not this tool's invention. The line above the answer says which was used |
| `--state <file>` | that document |
| the new state | standard output, so that redirecting gives a state document and nothing else |
| `--write` | amend `--state` in place instead, which is one file name for a sequence of moves rather than one per move |
| `play` | memory, and then gone |

```sh
rulealize apply reversi.json "place(at: d3)" > s1.json
rulealize moves reversi.json --state s1.json
```

## Naming an input

An input is named the way `moves` prints it, and the round trip is the runtime's own: the
legal inputs are enumerated and the one the text names is asked for its document, so **what
can be named is exactly what was offered**. Nothing is built from the text — an input the
rule set does not offer cannot be written into existence here.

The arguments are matched by name, so they may be written in any order and spaced however
you like. `place(at: d3)`, `place(at:d3)` and, where an input takes two,
`assign(shift: mon-am, who: ann)` and `assign(who: ann, shift: mon-am)` are one move named
four ways rather than four names, only one of which happens to be the runtime's.

```
$ rulealize apply reversi.json "place(at: a1)"
'place(at: a1)' does not name an input the initial state offers. 'place' is, but not with
those arguments.
'place' is offered as:
  place(at: e6)
  place(at: f5)
  place(at: c4)
  place(at: d3)
```

Which leaves the one thing this cannot express — an input the rule set ought to refuse, and
checking that it does — to `--input <file>` and a written document.

## When nobody chooses the next state

Not every rule set settles where it lands from the input alone. A card comes off a deck and
the input has more than one state it could arrive at, so `apply` will not pick one — doing
that would be this tool inventing an answer nobody enumerated, and it is the one thing the
runtime is built not to do. It says what could have happened instead:

```
$ rulealize apply blackjack.json "dealSeat"
'dealSeat' resolves something nobody chose, so applying it takes an outcome as well.

What could happen, most likely first:
  A (0.077)
  2 (0.077)
  …

Write the one that happened as a rulealize/outcome/v1 document and pass it
with --outcome <file>. 'play' asks rather than stopping.
```

```jsonc
// what.json
{ "$schema": "rulealize/outcome/v1", "ruleSet": "blackjack@1.0.0",
  "input": "dealSeat", "draws": ["9"] }
```

```sh
rulealize apply blackjack.json "dealSeat" --outcome what.json > s1.json
```

An input and an outcome settle a transition exactly, so that pair reproduces `s1.json`
however long afterwards — which is what makes a redirected sequence of them a record rather
than a re-run.

`play` asks, because it has somebody there to ask:

```
    1. dealSeat
>1

  Nobody chooses what happens next. Which did?
    1. A (0.077)
    2. 2 (0.077)
    …
>9
```

**Nothing here rolls anything.** The alternatives come from `GetOutcomes`, and choosing one
is the caller's — a file in `apply`, a person in `play`, a random number generator in a host
that wants one. That is the same arrangement either way, which is why neither command has a
`--seed`.

**A rule set with no chance in it notices none of this.** An input that draws nothing has
exactly one outcome, of probability one, so `apply` needs no `--outcome` and `play` never
asks. Both commands call `GetOutcomes` regardless; there is no branch here for chance,
because there is none in the runtime either.

## `moves`

The moves go to standard output, one per line, and everything about them to standard error.
A count is not a move.

```
$ rulealize moves reversi.json
reversi@1.0.0 from the initial state (ongoing)        <- standard error
place(at: e6)
place(at: f5)
place(at: c4)
place(at: d3)
4 legal inputs, 65 candidates evaluated               <- standard error
```

`GetValidInputs` stops at a limit rather than searching for as long as it takes, so a short
list can mean the position or it can mean the limit. **It always says which**, because the
worst failure this command has is a truncated answer that looks complete.

```
truncated at 2 candidates -- pass --limit to raise it
```

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

## A rule set that holds other rule sets

`uses` names the rule sets a document holds, by identifier, and the runtime is handed their
documents. **The folder the document is in is where those are looked for**, and
`--rulesets <folder>` says where they are when they are kept somewhere else.

A component may be one you wrote, sitting beside the document that holds it, or one somebody
published. Both are files in that folder by the time anything reads it, and `restore` is what
puts the second kind there — see [holding one you did not
write](#holding-a-rule-set-you-did-not-write).

```
$ rulealize check process.json
'process.json' compiles against 'plugin' (5 vocabularies, 33 operations).
holding 1 rule set:
  counter@1.0.0 ('counter.json')
```

Which document answers to an identifier is read out of the document and not off its file
name. `id` is what the document declares and what `uses` names; a file's name is nobody's
business but the author's. Files in the folder that are not rule sets — the states and the
outcomes written beside them — carry no `id` and are passed over, the way a plugin sweep
passes over an assembly it cannot use.

**An identifier has one document in a folder.** Two of them declaring it is a refusal naming
both, because taking whichever the file system listed first would settle something no
document said, and settle it quietly.

**No version is chosen while reading a folder.** With one document per identifier there is
nothing to choose between. Choosing happens where a version could be had, which is `restore`,
and whether what is here satisfies what asked for it is Rulealize's reading either way:

```
'process.json' does not compile against 'plugin':
  /uses[0]/version: this rule set needs counter ^1.0 as c, but 2.0.0 was supplied.
```

## Holding a rule set you did not write

A published rule set is a package whose `ruleset` folder holds one document, and **its
identifier is the identifier of that package**. So `uses` names a package, and nothing has to
be looked up anywhere to find it:

```json
"uses": [
  { "ruleSet": "Rulealize.RuleSet.Request", "version": "^1.0", "as": "req" }
]
```

`restore` fetches what that names, through the whole graph, into the folder the document
resolves its components from:

```
$ rulealize restore roster.json
  Rulealize.RuleSet.Request@1.0.0 -> Rulealize.RuleSet.Request.json
1 rule set -> .
holding 1 rule set:
  Rulealize.RuleSet.Request@1.0.0 ('Rulealize.RuleSet.Request.json')
  ...
7 plugins -> plugin
'roster.json' compiles against it.
```

**`as` is not optional for one of these.** An alias defaults to the identifier and may not
contain a `.`, so an entry naming a package-shaped identifier without `as` is refused, with a
message about a key you did not write.

**Which version is Rulealize's answer, not this tool's.** `RuleSetRequirement.Choose` takes
the *lowest* published version satisfying every constraint that named it — the same rule a
`requires` is resolved by, and for the same reason: the document resolves to the same folder
next year, when three more versions have shipped. Moving to a newer one means changing what
the document asks for, and restoring it is not the moment to do that.

**What is fetched is checked as it arrives**, against both halves of what was asked:

```
'roster.json' holds Rulealize.RuleSet.Request ^2.0 as req,
and 'Rulealize.RuleSet.Request.json' is Rulealize.RuleSet.Request@1.0.0.
```

**A document already in the folder is never written over.** Yours and one fetched earlier
look the same from here, and restoring is not the moment to decide which. If one that
`restore` put there is now the wrong version, delete it and run again.

**A rule set nobody publishes is not a mistake.** A component still being written is a file
you put in that folder, and from that moment it is indistinguishable from a fetched one —
which is the same thing the plugin folder is for, one kind of dependency over.

**`restore` fetches what the whole graph requires.** `requires` is the whole of what one
document may draw on, so a folder assembled from the holder's alone is one its components do
not compile against:

```
$ rulealize restore process.json
holding 1 rule set:
  counter@1.0.0 ('counter.json')
  Rulealize.Plugin.Arithmetic 1.0.0
  ...
5 plugins -> plugin
'process.json' compiles against it.
```

Nothing else here changes. A held rule set's inputs are offered as `alias.input`, so `moves`
prints them, `apply` takes them and `play` walks them like anything else; a composite's case
is still one state document; and a document that holds nothing reads no folder at all.

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
[it is a supported arrangement](https://github.com/reny-develop/Rulealize/blob/main/doc/plugin.md#a-vocabulary-that-is-not-distributed).
There is nothing on the feed to fetch, so **put the assembly in the folder yourself** and
restoring credits it and fetches the rest:

```
  My.Vocabulary (already in plugin)
  Rulealize.Plugin.State 1.0.0
  Rulealize.Plugin.TypeSchema 1.1.0
2 plugins -> plugin, 1 already there
'probe.json' compiles against it.
```

Whether the version there answers the constraint is Rulealize's reading — the same
`PluginRequirement.IsSatisfiedBy` the runtime checks a loaded plugin against — for the
reason no version choice is made here either.

A requirement neither the feed nor the folder can meet is still a refusal, naming what is
missing and how many would have resolved.

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
