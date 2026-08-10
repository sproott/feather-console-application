# Plan: completion-majority-model

Specs touched:

- none — this repo has no `docs/specs/`. The durable contract for completion is the
  "Shell Completion" section of `README.md`, updated by Task 9.

## Overview

Switch shell completion from the current design (raw line + cursor parsed in-process, suggestions
rendered per shell in F#) to the design used by optparse-applicative, clap and cobra: the shell
tokenizes, the program receives a **word array plus the index of the word being completed**, and each
generated script does its own filtering, prefix trimming and escaping. On top of that, add the two
features every one of those libraries has and we lack: `--` end-of-options awareness during
completion, and descriptions attached to suggestions.

## Facts established before planning (measured, not assumed)

Bash completion probe against the demo app (`COMP_WORDS` / `$2` dumped from a real pty):

| typed | `COMP_WORDS` | `COMP_CWORD` | `$2` (cur) |
|---|---|---|---|
| `myapp test "my v` | `<myapp><test><"my v>` | 2 | `my v` |
| `myapp test my\ o` | `<myapp><test><my\ o>` | 2 | `my\ o` |
| `myapp test --opt1=va` | `<myapp><test><--opt1><=><va>` | 4 | `va` |
| `myapp test -fov` | `<myapp><test><-fov>` | 2 | `-fov` |
| `myapp test ` (trailing space) | `<myapp><test><>` | 2 | `` |

Consequences that shape the tasks:

- Bash keeps the quote characters in `COMP_WORDS` but hands the **unquoted** current word in `$2`
  — so the script must override `words[COMP_CWORD]="$2"` (this is what clap does,
  `clap_complete/src/env/shells.rs:40-43`, guarded by `BASH_VERSINFO >= 4`).
- Bash **splits the word at `=`** into three entries; zsh and fish do not. Word normalization has to
  rejoin them, or the resolver sees `--opt1`, `=`, `va` as three words.
- A backslash-escaped word stays escaped in `$2` (`my\ o`) — zsh's `$PREFIX` behaves the same way.
  One shared unescape step covers both.
- Readline still inserts `COMPREPLY` entries literally, so values containing spaces must be escaped
  by the script (`printf %q`, cobra's approach) or completion of such values breaks.

Repo facts:

- The runtime argument parser **already** honours `--` (`src/Input.fs:44`), so `__completion <index> -- <words>...`
  is safe and no parser change is needed. Only the completion resolution is unaware of `--`.
- Array arguments exist (`Argument.requiredArray`, `src/Arguments.fs:86`).
- `Completion.fs` has been split into `src/Completion/{Shell,CommandLine,Definitions,Bash,Suggestions,ShellScript,Setup}.fs`;
  the plan targets that layout.
- Descriptions already exist on every definition (`Command.description`, option/argument `Description`),
  so name suggestions can carry them without any new user-facing input.

## Architecture Decisions

1. **Input becomes `words` + `index`.** `CompletionInput` changes from `{ Line; CursorPosition }` to
   `{ Words: string list; Index: int }`. `CompletionContext` (what `Suggest` callbacks see) keeps its
   current shape — `CurrentWord` / `PrecedingWords` — so user-written suggestion callbacks are unaffected.
2. **No shell-specific rendering in F#.** `Completion/Bash.fs` and the quote state machine in
   `Completion/CommandLine.fs` are deleted. What replaces them is `Completion/Words.fs`: normalization of
   the incoming word array (rejoin bash's `=` split, unescape backslashes, slice at the index).
3. **Suggestions are relative to the shell's replacement unit.** After `--opt=`, the resolver returns bare
   values (`val1`), not whole words — bash replaces only the post-`=` text, and the zsh/fish scripts add the
   prefix back (`compadd -P`, cobra `zsh_completions.go:136`). Everywhere else a suggestion is a whole word,
   including short-option clusters (`-foval1`), which have no break character inside them.
4. **Escaping moves into the generated scripts.** bash: `printf %q` before and after `compgen`
   (cobra `bash_completionsV2.go:286-293`). zsh: `_describe`/`compadd` quote on insertion. fish: `string escape`.
5. **No bash-completion package dependency.** cobra requires `_get_comp_words_by_ref`; we stay
   self-contained like clap and optparse-applicative, using `COMP_WORDS` + `$2`.
6. **Descriptions are additive.** `CompletionSuggestion` gains an optional description;
   `Suggest.values` and existing callbacks keep returning `string list`; a parallel `Suggest.describedValues`
   is added for callbacks that want descriptions. Wire format is `value<TAB>description`.
7. **`--` handling is a completion-time rule**, not a parser change: after a `--` word, only positional
   values are suggested, option names never are, and `--` itself is not counted as a positional value.

## Task List

### Phase 1: Input model

- [x] Task 1: `Completion/Words.fs` — normalize the incoming word array
- [x] Task 2: `CompletionInput` reshaped; resolver consumes words; line parsing and bash rendering deleted
- [x] Task 3: Resolver command takes `<index> -- <words>...`
- [x] Task 4: Regenerate the three shell scripts for the word model

### Checkpoint: Input model

- [x] `dotnet build` clean, `fsharplint` clean, full test suite green
- [x] pty verification with the scratchpad demo app, in bash, zsh and fish, for: plain word, quoted word
      with a space, backslash-escaped word, `--opt1=va`, `-fov`, trailing space
- [x] Any case that regresses versus today's behaviour is written down before moving on

### Phase 2: End of options

- [x] Task 5: `--` stops option suggestions and is skipped by positional counting

### Phase 3: Descriptions

- [x] Task 6: `CompletionSuggestion` carries an optional description; built-in name suggestions fill it
- [x] Task 7: `Suggest.describedValues` for user callbacks
- [x] Task 8: Scripts render descriptions (zsh `_describe`, fish tab column, bash strips them)

### Checkpoint: Descriptions

- [x] pty check: zsh and fish show descriptions, bash completes unchanged with descriptions stripped

### Phase 4: Documentation

- [x] Task 9: README completion section + CHANGELOG rewritten for the new model

### Checkpoint: Complete

- [x] All acceptance criteria met, docs match observed behaviour, ready for review

---

## Task 1: `Completion/Words.fs` — normalize the incoming word array

**Description:** New module turning the raw word array the shell passed into the words the resolver
reasons about: rejoin bash's `option`, `=`, `value` split into one `option=value` word, drop the
backslash escapes a shell leaves in the current word, and expose the current word plus the preceding
ones for a given index. Pure, no I/O, fully unit-testable.

**Acceptance criteria:**

- [ ] `--opt1`, `=`, `va` with the index on `va` normalizes to one word `--opt1=va` with the index on it
- [ ] `my\ o` normalizes to `my o`; `\\` normalizes to `\`
- [ ] An index past the end of the array yields an empty current word (trailing-space case)
- [ ] Words after the index are dropped — completion never looks past the cursor

**Verification:**

- [ ] `dotnet run --project tests/tests.fsproj -- --filter-test-list "Words"`

**Dependencies:** None

**Files likely touched:** `src/Completion/Words.fs`, `console-application.fsproj`, `tests/CompletionTests.fs`

**Estimated scope:** S

## Task 2: `CompletionInput` reshaped; line parsing and bash rendering deleted

**Description:** `CompletionInput` becomes `{ Words: string list; Index: int }`. `Suggestions.resolve`
builds its `CompletionContext` from `Words.normalize` instead of `CommandLine.parse`, and
`Suggestions.forShell` disappears — there is one resolve path and its output is shell-agnostic.
`Completion/Bash.fs` and the tokenizer half of `Completion/CommandLine.fs` are deleted;
`splitAssignment` moves to `Words`. The assignment case returns bare values again (decision 3);
cluster and whole-word cases are unchanged.

**Acceptance criteria:**

- [ ] `Completion/Bash.fs` is gone and the quote state machine no longer exists in the codebase
- [ ] Existing resolution behaviour is preserved for: command names, long option names, short-option
      clusters (`-f` → `-fl`/`-fo`, `-fov` → `-foval1`), option values, positional values and their indexing
- [ ] `--opt1=va` yields `val1`/`val2`, not `--opt1=val1`
- [ ] Suggestion callbacks still receive `CurrentWord`/`PrecedingWords` with the same meaning

**Verification:**

- [ ] `dotnet run --project tests/tests.fsproj -- --filter-test-list "Completion"`
- [ ] `dotnet fsharplint lint console-application.fsproj` reports 0 warnings

**Dependencies:** Task 1

**Files likely touched:** `src/Types.fs`, `src/Completion/CommandLine.fs` (deleted), `src/Completion/Bash.fs` (deleted),
`src/Completion/Suggestions.fs`, `console-application.fsproj`, `tests/CompletionTests.fs`

**Estimated scope:** M

## Task 3: Resolver command takes `<index> -- <words>...`

**Description:** The hidden `__completion` command's arguments become an index and an array of words.
The shell name argument is dropped — output no longer depends on the shell. Malformed calls stay silent
(non-numeric index, missing words) as they do today.

**Acceptance criteria:**

- [ ] `myapp __completion 2 -- myapp test pl` prints `plain`
- [ ] A non-numeric index exits with `ExitCode.Error` and prints nothing
- [ ] Words starting with `-` reach the resolver untouched (the `--` separator already handles this)

**Verification:**

- [ ] `dotnet run --project tests/tests.fsproj -- --filter-test-list "completion"`
- [ ] Manual: build the scratchpad demo app and run the three invocations above

**Dependencies:** Task 2

**Files likely touched:** `src/Completion/Setup.fs`, `tests/CompletionTests.fs`

**Estimated scope:** S

## Task 4: Regenerate the three shell scripts for the word model

**Description:** Rewrite the bash, zsh and fish scripts. bash: copy `COMP_WORDS`, override
`words[COMP_CWORD]="$2"` when `BASH_VERSINFO[0] >= 4`, call the resolver with `COMP_CWORD` and the words,
escape results with `printf %q`, filter with `compgen -- "$cur"`, re-escape the single-match case.
zsh: pass `${words[@]}` and `CURRENT-1`, add `compadd -P` for the `-*=` prefix case. fish:
`commandline --tokenize --cut-at-cursor`, `string escape` on the current token.

**Acceptance criteria:**

- [ ] Each script passes its shell's syntax check (`bash -n`, `zsh -n`, `fish --no-execute`)
- [ ] No script depends on the bash-completion package
- [ ] Generated bash script contains no `COMP_LINE`/`COMP_POINT` reference

**Verification:**

- [ ] `dotnet run --project tests/tests.fsproj -- --filter-test-list "completion script"`
- [ ] pty run of the checkpoint matrix below

**Dependencies:** Task 3

**Files likely touched:** `src/Completion/ShellScript.fs`, `tests/CompletionTests.fs`

**Estimated scope:** M

## Task 5: `--` stops option suggestions

**Description:** Completion honours the end-of-options separator: once a `--` word precedes the cursor,
option names are never suggested and the current word is always treated as a positional value. `--`
itself is not counted as a positional value when computing the argument index.

**Acceptance criteria:**

- [ ] `myapp test -- <TAB>` suggests the first positional argument's values, not option names
- [ ] `myapp test -- --<TAB>` suggests nothing (or positional values matching `--`), never option names
- [ ] `myapp test -- alpha <TAB>` suggests the second positional argument's values

**Verification:**

- [ ] `dotnet run --project tests/tests.fsproj -- --filter-test-list "end of options"`

**Dependencies:** Task 2

**Files likely touched:** `src/Completion/Suggestions.fs`, `tests/CompletionTests.fs`

**Estimated scope:** S

## Task 6: `CompletionSuggestion` carries a description

**Description:** `CompletionSuggestion` becomes a record of a value and an optional description, with its
companion module keeping `value` and gaining `create`/`describe`. Built-in sources fill it: command names
from `Command.description`, long and short option names from the option's description. The resolver emits
`value<TAB>description` when a description is present, bare `value` otherwise.

**Acceptance criteria:**

- [ ] Command name suggestions carry the command description
- [ ] Option name suggestions carry the option description
- [ ] A suggestion without a description emits no tab character
- [ ] Values from `Suggest` callbacks are unaffected (no description)

**Verification:**

- [ ] `dotnet run --project tests/tests.fsproj -- --filter-test-list "Completion"`

**Dependencies:** Task 2

**Files likely touched:** `src/Types.fs`, `src/Completion/Definitions.fs`, `src/Completion/Suggestions.fs`,
`src/Completion/Setup.fs`, `tests/CompletionTests.fs`

**Estimated scope:** M

## Task 7: `Suggest.describedValues` for user callbacks

**Description:** Additive API letting a suggestion callback return values with descriptions, without
changing the existing `Suggest.values` / `Suggest.sync` / `Suggest.async` signatures. The combinators
(`map`, `filter`, and the async variants) keep working on the value, leaving descriptions untouched.

**Acceptance criteria:**

- [ ] `Suggest.describedValues [ "dev", "Development"; "prod", "Production" ]` produces described suggestions
- [ ] `Suggest.map`/`Suggest.filter` applied to described values keep the descriptions
- [ ] Existing `Suggest.values` code compiles unchanged

**Verification:**

- [ ] `dotnet run --project tests/tests.fsproj -- --filter-test-list "Suggest"`

**Dependencies:** Task 6

**Files likely touched:** `src/Types.fs`, `tests/CompletionTests.fs`

**Estimated scope:** S

## Task 8: Scripts render descriptions

**Description:** bash strips everything after the tab (readline cannot show descriptions), zsh feeds
`value:description` pairs to `_describe`, fish passes the tab through — it renders the second column natively.

**Acceptance criteria:**

- [ ] bash completes the value only, with no tab or description text inserted
- [ ] zsh and fish show the description next to each candidate
- [ ] A suggestion with no description renders as before in all three shells

**Verification:**

- [ ] `dotnet run --project tests/tests.fsproj -- --filter-test-list "completion script"`
- [ ] pty run: bash inserts values cleanly, zsh and fish list descriptions

**Dependencies:** Tasks 4, 6

**Files likely touched:** `src/Completion/ShellScript.fs`, `tests/CompletionTests.fs`

**Estimated scope:** S

## Task 9: Documentation

**Description:** Rewrite the README "How It Works" paragraphs for the word model, document descriptions and
`--` handling, and restate the limitations to match what the pty runs actually showed. CHANGELOG gets the
breaking-change entries (`CompletionInput`, `CompletionSuggestion`, resolver command signature).

**Acceptance criteria:**

- [ ] README describes the word+index protocol and no longer claims in-process line parsing
- [ ] Limitations list matches behaviour verified in all three shells
- [ ] CHANGELOG marks the public API changes as breaking

**Verification:**

- [ ] Manual read-through against the checkpoint results

**Dependencies:** Tasks 4, 5, 8

**Files likely touched:** `README.md`, `CHANGELOG.md`

**Estimated scope:** S

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Public API break (`CompletionInput`, `CompletionSuggestion`, resolver arguments) | High | Ship as a major version; CHANGELOG `[**BC**]` entries; completion is unreleased anyway (Unreleased section) |
| Quoted-word completion regresses in bash | Medium | The probe shows `$2` is already unquoted; the `words[COMP_CWORD]="$2"` override covers it. Verified in the phase-1 checkpoint, not assumed |
| Values with spaces stop inserting correctly | Medium | `printf %q` in the bash script, verified in a pty; zsh/fish quote themselves |
| bash 3.2 (macOS) has no `$2` override guard | Low | Guard with `BASH_VERSINFO`, as clap does; degraded but functional on bash 3 |
| fish behaviour differs from the other two (its own escaping and token rules) | Low | fish 4.8.1 is installed, so the pty matrix covers it at every checkpoint |
| Descriptions break existing suggestion consumers | Low | Additive API; tab only emitted when a description exists |

## Resolved Decisions

- **Public API breaks outright.** No line-based compatibility shim — `CompletionInput` and
  `CompletionSuggestion` change shape, the quote state machine is deleted rather than kept as a second
  path, and the CHANGELOG carries `[**BC**]` entries. Completion is unreleased, so nothing shipped breaks.
- **bash strips descriptions at the tab.** No padded display column; zsh and fish show descriptions.
- **fish is verified like the others.** fish 4.8.1 is installed, so every checkpoint pty matrix covers
  bash, zsh and fish.
