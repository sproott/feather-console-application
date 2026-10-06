# Preferred Patterns

## Core Principles

- One `consoleApplication { ... }` per program, piped into `run argv` from `[<EntryPoint>]`. Keep each `CommandDefinition` as a named value (or a function of its dependencies) and register it with `command "name" definition`.
- Declare every input the command reads. All `Input.Argument.*` / `Input.Option.*` accessors throw when the name is not declared on the running command.
- Model failures as values: prefer `ExecuteResult` / `ExecuteAsyncResult` returning `ConsoleApplicationError.CommandError (CommandError.Message ...)` over throwing. Thrown exceptions are still caught and become `ConsoleApplicationException`, exit code `1`.
- Let the library render usage, help and errors. Do not print your own usage text or parse `argv` before handing it over.

## Definition Rules

Validated when the application runs; `run` prints the error and returns `1`, `runResult` returns `Error`.

- **Command name** (`command`, `defaultCommand`) — characters `[\w.\-: ]` only (`CommandNameError.Invalid` otherwise); must not start with `:` or `-`, contain a space or `::`, or end with `:`. `command`, `list`, `help`, `about`, `exit` are reserved (`CommandNameError.Reserved`). Use `:` for namespaces (`db:migrate`).
- **Application name** (`name`) — must not be empty or whitespace. Default name is `Console Application`.
- **Argument name** — not empty, must not start with `-` or contain a space; `command` is reserved. Within a command: names unique, an array argument only last, no required argument after an optional one.
- **Option name** — not empty, must not start with `-` or contain `=` or a space; `help`, `quiet`, `version`, `verbose`, `no-interaction`, `no-progress`, `no-ansi` are reserved. Names and shortcuts unique within a command.
- **Option shortcut** — `Some "x"`: exactly one character, no `-`; `h`, `q`, `V`, `n` are reserved (`OptionShortcutError.Reserved`).
- Argument and option errors of one command are accumulated (`CommandDefinitionError.ArgumentDefinitionError of ArgumentDefinitionError list`, likewise for options); builder-level errors stop at the first one.

## Recommended API Usage

### Builder operations

`name`, `version`, `title`, `description`, `meta ("Key", "value")` or `meta [ ... ]`, `git repository branch commit` (three strings) or `git (repoOption, branchOption, commitOption)`, `gitRepository` / `gitBranch` / `gitCommit`, `info ApplicationInfo.X`, `showOptions Complete`, `defaultCommand "name"`, `command "name" definition`, `useOutput`, `useAsk`, `updateOutput`, `withStyle`, `withCustomTags` (a `CustomTag list`, or a `Result<CustomTag, string> list` whose errors become `CommandDefinitionError.InvalidCustomTags`). `description`, `meta` and git values are shown by `about`. See `examples.md` → "Realistic".

### Arguments

| Constructor | Signature after `name description` | Value after parsing |
| --- | --- | --- |
| `Argument.required` | — | `ArgumentValue.Required value`; missing → `NotEnoughArguments` |
| `Argument.optional` | `default: string option` | `ArgumentValue.Optional (Some arg \| default)` |
| `Argument.requiredArray` | — | `ArgumentValue.RequiredArray` (one or more) |
| `Argument.optionalArray` | `default: string list option` | `ArgumentValue.Array` (default or `[]`) |

After parsing every declared argument has an entry, so `Input.Argument.value` / `asString` / `asList` are safe for declared names.

### Options

| Constructor | Signature after `name shortcut description` | Present in `Input.Options` |
| --- | --- | --- |
| `Option.noValue` | — | only when passed (`ValueNone`) |
| `Option.required` | `default: string` | always (passed value or default); passed without a value → `RequiredValueNotSet` |
| `Option.optional` | `default: string option` | when passed, or when default is `Some` |
| `Option.optionalArray` | `default: string list option` | when passed, or when default is `Some` |
| `Option.requiredArray` | `default: string list option` | when passed, or when default is `Some` |

Values are given as `--name value`, `--name=value`, `-x value` or `-xvalue`; shortcuts bundle (`-fc`). Repeat array options (`-t a --tag=b`). A value that looks like an option or shortcut (including `--`) is not consumed as a value. Everything after `--` is positional.

### Reading input

- Flag: `input |> Input.Option.has "dry-run"`.
- Option that may be absent: active patterns — `Input.Option.OptionalValue "name" value`, `Input.Option.ListValue "name" values`, `Input.Option.IsSet "name" optionValue` — or `Input.Option.tryGet "name"` (`OptionValue option`).
- Option that always has a value (`Option.required`, or a `Some` default): `Input.Option.value "name"`, `Input.Option.tryAsInt "name"` (`int option`, `None` when not an int).
- Arguments: `Input.Argument.value "name"` (single value), `Input.Argument.asString` (`string option`), `Input.Argument.asList` (`string list`), `Input.Argument.tryAsInt`.
- Unwrapping a raw value takes the input name first: `OptionValue.value "name" optionValue`, `ArgumentValue.value "name" argumentValue`.

See `examples.md` → "Arguments and options".

### Help text

`Help = Help.lines [ ... ]` joins lines with blank lines and returns `Some`. `{{command.name}}` and `{{command.full_name}}` (relative dll path + command name) are replaced; ConsoleStyle markup such as `<c:green>...</c>` is allowed.

## Life-cycle

Per run: header by `info` → parse argv (option defaults applied) → `Initialize` → `Interact` (skipped with `-n` / `--no-interaction`) → missing arguments checked and argument defaults applied → `Execute`.

- In `Interact`, a not-passed required or optional argument is still absent (`Input.Argument.Has` does not match); option defaults are already present.
- Fill missing values with `Input.Argument.set input "name" value` / `Input.Option.set input "name" value` (input first, then name, then value); `setList` for arrays. `set` on a `noValue` option, or an empty value for a required one, throws.
- Return the updated `(input, output)` tuple from `Initialize` / `Interact`.

See `examples.md` → "Interaction".

## Error Handling

- Command failures: return `Error (ConsoleApplicationError.CommandError (CommandError.Message "..."))`; use `CommandError.Errors [ ... ]` for several, `CommandError.Exception exn` to wrap one. `run` prints each message via `output.Error`; with `-v` exceptions are printed in full.
- Map domain errors at the `Execute` boundary with `Result.mapError` / `AsyncResult.mapError`.
- `ExecuteAsync` / `ExecuteAsyncResult` exceptions are caught as `ConsoleApplicationException`, like synchronous ones.
- Error formatting functions are internal; with `runResult` / `runAsyncResult`, match on the cases yourself or use `run` for the printed form.

## Composition

- Group commands by namespace (`user:create`, `user:delete`); users can type the shortest unambiguous prefix per segment (`u:c`). Ambiguity yields `ArgsError.AmbigousCommandFound`.
- Set `defaultCommand` for the command to run when argv is empty or holds only options; the default is `list`.
- Share dependencies by making definitions functions: `let importCommand (repository: Repository): CommandDefinition = { ... }`.
- For a REPL-like tool, use `runInteractively argv`: it lists commands, asks `Command:`, runs the answer as one argv token (the command name) followed by the original `argv`, and stops on `exit` or the first non-zero exit code.

## Integration with Other Libraries

- `output` is a `Feather.ConsoleStyle.ConsoleStyle`: use `output.Message`, `Title`, `Section`, `Success`, `Error`, `Table`, `Ask`, and the progress API (`output.ProgressStart name total`, `ProgressAdvance`, `ProgressFinish`). `--no-progress` and `-vvv` disable progress bars. See `examples.md` → "Progress bar".
- `-q` sets `Verbosity.Quiet`, `-v` / `-vv` / `-vvv` set `Verbose` / `VeryVerbose` / `Debug`; check with `output.IsVerbose()` / `output.IsDebug()`. `--no-ansi` strips markup.
- `asyncResult {}` from Feather.ErrorHandling composes naturally inside `ExecuteAsyncResult`. See `examples.md` → "Async command".

## Naming Conventions

- Command names: lower-case, `:`-namespaced, `-` inside segments (`cache:clear-all`).
- Argument names: camelCase or snake_case (`command_name` in built-ins); option names: kebab-case (`dry-run`, `batch-size`).
- Shortcuts are case-sensitive single letters (`-o` and `-O` differ).

## Testing Recommendations

- Inject a `Feather.ConsoleStyle.ConsoleStyle(new Output.BufferOutput(Verbosity.Normal))` with `useOutput`, and a fixed `useAsk (fun _ -> "answer")`; run with `runResult` and assert on the `Result` and on `buffer.Fetch() |> console.RemoveMarkup`. See `examples.md` → "Testing a command".
- Capture the parsed `Input` from `Execute` into a `mutable` and assert on `input.Arguments` / `input.Options` maps; `input.Arguments` also contains a `"command"` entry with the resolved command name.
- Assert definition errors by expected value, e.g. `Error (ConsoleApplicationError.CommandDefinitionError (CommandDefinitionError.OptionDefinitionError [ ... ]))`. `CommandNotFound.create` and `AmbigousCommandFound.create` build the matching `ArgsError` values.
- Drive cases through a table of `(argv, expected)` records, as the library's own tests do.
