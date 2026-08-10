# Todo: completion-majority-model

Plan: `docs/tasks/completion-majority-model/plan.md`

## Phase 1: Input model

- [x] **Task 1 — `Completion/Words.fs`** (S, no deps)
  - [x] Rejoin bash's `--opt`, `=`, `value` split into one word, index following it
  - [x] Unescape backslashes in the current word (`my\ o` → `my o`)
  - [x] Index past the end yields an empty current word
  - [x] Words after the index dropped
  - [x] Tests: `--filter-test-list "Words"`

- [x] **Task 2 — `CompletionInput` reshaped, line parsing deleted** (M, needs 1)
  - [x] `CompletionInput = { Words; Index }`
  - [x] `Suggestions.resolve` builds context from `Words`; `forShell` removed
  - [x] `Completion/Bash.fs` deleted, tokenizer deleted, `splitAssignment` moved to `Words`
  - [x] `--opt1=va` returns bare `val1`/`val2`
  - [x] Clusters, command names, option names, positional indexing unchanged
  - [x] Tests green + fsharplint 0 warnings

- [x] **Task 3 — Resolver command `<index> -- <words>...`** (S, needs 2)
  - [x] Index argument + words array argument, shell argument dropped
  - [x] Non-numeric index → `ExitCode.Error`, no output
  - [x] Manual: `myapp __completion 2 -- myapp test pl` → `plain`

- [x] **Task 4 — Shell scripts for the word model** (M, needs 3)
  - [x] bash: `COMP_WORDS` + `words[COMP_CWORD]="$2"` (guarded by `BASH_VERSINFO`), `printf %q`, `compgen`
  - [x] zsh: `${words[@]}` + `CURRENT-1`, `compadd -P` for `-*=`
  - [x] fish: `commandline --tokenize --cut-at-cursor`, `string escape`
  - [x] No bash-completion package dependency
  - [x] `bash -n`, `zsh -n` and `fish --no-execute` pass

### Checkpoint: Input model

- [x] Build clean, lint clean, all tests green
- [x] pty matrix (bash, zsh, fish) with the scratchpad demo app:
  - [x] `test pl` → `plain`
  - [x] `test "my v` → `my value`
  - [x] `test my\ o` → `my other value`
  - [x] `test --opt1=va` → `val` (ambiguous common prefix)
  - [x] `test -fov` → `-foval`
  - [x] `test ` (trailing space) → argument values
- [x] Regressions versus current behaviour written down — none found; every matrix case behaves as
      before or better. Two script-side facts the pty runs settled: bash escapes suggestions with
      `printf %q` only when the word at the cursor is unquoted (readline inserts into an open quote
      itself), and both zsh and fish must hand over the word at the cursor unquoted by the shell
      (`$PREFIX`, `commandline --current-token --tokenize`) — an open quote is not stripped by `(Q)`.

## Phase 2: End of options

- [x] **Task 5 — `--` stops option suggestions** (S, needs 2)
  - [x] `test -- <TAB>` → positional values, no option names
  - [x] `test -- --<TAB>` → no option names
  - [x] `--` not counted as a positional value
  - [x] Tests: `--filter-test-list "end of options"`

## Phase 3: Descriptions

- [x] **Task 6 — `CompletionSuggestion` carries a description** (M, needs 2)
  - [x] Record with value + optional description, companion `value`/`create`/`describe`
  - [x] Command names carry `Command.description`
  - [x] Option names carry the option description
  - [x] Wire format `value<TAB>description`, no tab when absent

- [x] **Task 7 — `Suggest.describedValues`** (S, needs 6)
  - [x] Described values API, existing `Suggest.values` unchanged
  - [x] `map`/`filter` keep descriptions

- [x] **Task 8 — Scripts render descriptions** (S, needs 4 and 6)
  - [x] bash strips at the tab
  - [x] zsh `_describe` with `value:description`
  - [x] fish passes the tab through

### Checkpoint: Descriptions

- [x] pty: zsh and fish list descriptions, bash inserts values with no description text

## Phase 4: Documentation

- [x] **Task 9 — README + CHANGELOG** (S, needs 4, 5, 8)
  - [x] README describes the word+index protocol
  - [x] Limitations match behaviour verified in bash, zsh and fish
  - [x] CHANGELOG entries for the API changes — no `[**BC**]` marker: every changed type
        (`CompletionInput`, `CompletionSuggestion`, `Suggest` cases, resolver arguments) lives in the
        Unreleased section, so nothing released breaks and a BC marker would claim otherwise

### Checkpoint: Complete

- [x] All acceptance criteria met
- [x] Ready for review
