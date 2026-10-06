# Anti-Patterns

Format: **mistake → why it is wrong → fix**.

## Outdated / Non-existent API

- **Calling `Input.getArgumentValue "name"`** → the README's main example uses it, but no such function exists; it will not compile → use `input |> Input.Argument.value "name"`.

- **Calling `Input.Argument.tryGetAsInt`, `Input.Option.tryGetAsInt`, `Input.Option.tryValueAsInt` or `Input.tryAsInt`** → names from the README / `ARGUMENTS.md` / `OPTIONS.md` tables and snippets; none exist → use `Input.Argument.tryAsInt "name"` / `Input.Option.tryAsInt "name"`.

- **Writing `optionValue |> OptionValue.value` (or `ArgumentValue.value` with one argument)** → as in the README's `Interact` example and `OPTIONS.md`; both functions take the input name first (used in the exception message), so this is a partial application, not a `string` → `OptionValue.value "message" optionValue`, or `Input.Option.value "message" input`.

- **Passing `None` as the default of `Option.required`** → `OPTIONS.md` shows `Option.required "bar" (Some "b") "" None`, but the default is a plain `string` → pass a string (`""` if there is no meaningful default). Only `Option.optional` takes `string option`; the array constructors take `string list option`.

- **Expecting `Input.Argument.get` / `Input.Option.get` to return an `option`** → `ARGUMENTS.md` / `OPTIONS.md` annotate them as `... option`; they return the bare value and throw when it is missing → use `tryGet` for an `option`.

- **Qualifying style types as `Feather.ConsoleApplication.Style` / `Feather.ConsoleApplication.CustomTag`** → the README builder table names them so, but they live in `Feather.ConsoleStyle` → `open Feather.ConsoleStyle` and pass `Style` / `CustomTag` values to `withStyle` / `withCustomTags`.

- **Relying on the comments in `Input.Option.set` / `setList` that point to `Input.setOptionValue` / `Input.setOptionListValue` (or `Input.setArgumentValue` / `Input.setArgumentListValue`)** → those functions do not exist → use `Input.Option.set` / `setList` and `Input.Argument.set` / `setList`.

- **Expecting `--help` alone to print an overall application help** → the README says so, but an options-only argv runs the default command, so `--help` prints help for the default command (`list` unless `defaultCommand` is set) → run `list` for the command overview, `<command> --help` or `help <command>` for one command.

- **Assuming only two built-in commands** → the README says "two default commands"; there are three (`list`, `help`, `about`), and `exit` is also reserved → do not register commands with these names; it fails with `CommandNameError.Reserved`.

## Throwing Accessors

- **Reading an undeclared argument or option** → every `Input.Argument.*` / `Input.Option.*` accessor, active patterns included, throws `The "--x" option does not exists.` (or the argument equivalent) for a name the running command did not declare; a typo becomes a `ConsoleApplicationException` at run time → declare the input, keep names in one place (e.g. `[<Literal>]` constants), and cover each command with a test.

- **Using `Input.Option.asString` / `asList` / `asInt` / `value` / `get` on an option that may be absent** → they throw when the option was not passed and has no default (only `required` options always have one); the README's "returns `string option`" refers to the value, not to presence → use `Input.Option.has`, `tryGet`, or the `OptionalValue` / `ListValue` / `IsSet` active patterns. See `examples.md` → "Arguments and options".

- **Matching `Input.Option.Value "name" v` on an `Option.optional` passed without a value** → `Value` calls `OptionValue.value`, which throws for `ValueOptional None` → match `Input.Option.OptionalValue "name" v` first, then `Input.Option.Has "name" _` for the bare flag.

- **Using `Input.Argument.asInt` / `Input.Option.asInt` on user input** → returns `int option` but throws `FormatException` for non-numeric text → use `tryAsInt` and report the invalid value.

- **Calling `Input.Option.set` on a `noValue` option, or with an empty value for a `required` one** → throws → only set options that accept a value; for flags, decide in `Execute` with `Input.Option.has`.

## Life-cycle Misunderstandings

- **Checking argument defaults in `Interact`** → optional-argument defaults and the "missing required argument" check are applied after `Interact`, so a not-passed argument is absent there even if it has a default → in `Interact`, treat absence as "ask"; read defaults in `Execute`.

- **Validating required arguments yourself before `Interact` runs** → the library already reports `NotEnoughArguments` after `Interact`, and asking in `Interact` is how a value can still be supplied → fill missing values in `Interact`; leave the final check to the library.

- **Assuming `Interact` always runs** → `-n` / `--no-interaction` skips it entirely → `Execute` must cope with optional inputs that were never asked for.

## Definition Mistakes

- **Placing options or global flags before the command name (`tool -n user:create`)** → the first argv token is parsed as the command name, so this fails with `CommandNameError (NameError (StartsWith ("-n", "-")))`; only an options-only argv falls back to the default command → put the command name first: `tool user:create -n`.

- **Registering a command named `list`, `help`, `about`, `exit` or `command`, or an option named `help`, `quiet`, `version`, `verbose`, `no-interaction`, `no-progress`, `no-ansi`** → rejected as reserved when the application runs → choose another name.

- **Giving an option a shortcut `h`, `q`, `V` or `n`** → `OptionShortcutError.Reserved` → pick another letter. `v` is not rejected, but `-v` then also raises verbosity, so avoid it too.

- **Multi-letter or dashed shortcuts (`Some "o|oo"`, `Some "-o"`)** → `MoreThanSingleLetter` / `Contains` errors → pass a single letter without `-`.

- **A required argument after an optional one, or any argument after an array argument** → `RequiredArgumentAfterOptional` / `ArgumentAfterArrayArgument` → order arguments required → optional → one trailing array.

- **Option names with `=` or spaces, names starting with `-`, or command names with spaces, `::`, a leading `-`/`:` or a trailing `:`** → `NameError` cases → use kebab-case option names and `:`-namespaced command names.

- **Treating a successfully built `consoleApplication { ... }` as valid** → definition errors are carried inside the value and only surface when it is run → run every command (e.g. with `--help`) in a test so definition errors fail the build.

## Error Handling

- **Throwing from `Execute` to signal a domain failure** → it is caught, but becomes `ConsoleApplicationException` and prints only the exception message (full dump only with `-v`) → return `Error (ConsoleApplicationError.CommandError (CommandError.Message "..."))` from `ExecuteResult` / `ExecuteAsyncResult`.

- **Returning `ExitCode.Error` without output** → `run` prints nothing for it, so the user sees a silent failure → print a reason with `output.Error`, or return an `Error` result so `run` prints it.

- **Printing errors yourself and then calling `exit`** → bypasses the library's error rendering and the usage line it prints for the failing command → return the error and let `run` handle output and the exit code.

- **Expecting `-q` to suppress errors you print with `printfn`** → verbosity only applies to `output` → write everything through `output`.
