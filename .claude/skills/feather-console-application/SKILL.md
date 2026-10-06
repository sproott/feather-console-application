---
name: feather-console-application
description: Use whenever generating or reviewing F# code that builds a command-line application with Feather.ConsoleApplication — the `consoleApplication { ... }` computation expression (`name`, `version`, `info`, `command`, `defaultCommand`, `useOutput`, `useAsk`, `withCustomTags`) executed via `run`, `runResult`, `runAsyncResult` or `runInteractively`; `CommandDefinition` records with `Initialize` / `Interact` / `Execute` (`Execute`, `ExecuteResult`, `ExecuteAsync`, `ExecuteAsyncResult`); declaring inputs with `Argument.required` / `optional` / `requiredArray` / `optionalArray` and `Option.noValue` / `required` / `optional` / `optionalArray` / `requiredArray`; or reading them through `Input.Argument.*` / `Input.Option.*` active patterns and getters. Trigger also on `ExitCode`, `ConsoleApplicationError`, `ArgumentValue`, `OptionValue`, `ApplicationInfo`, `Help.lines`, `--no-interaction`, namespaced command names like `db:migrate`, or testing a CLI with a ConsoleStyle `BufferOutput`.
---

# Feather-Console-Application

Library: [FeatherTools/console-application](https://github.com/FeatherTools/console-application)
NuGet: `Feather.ConsoleApplication`

## Purpose

`Feather.ConsoleApplication` is an F# framework for multi-command CLI applications, modelled on Symfony Console. A `consoleApplication { ... }` computation expression registers application metadata and named commands; each command is a `CommandDefinition` record declaring its arguments, options, and `Initialize` → `Interact` → `Execute` life-cycle. The library parses `argv` (long/short options, bundled shortcuts, `--` separator, abbreviated command names), validates definitions, adds built-in `list`/`help`/`about` commands and global options, and renders help and errors through `Feather.ConsoleStyle`.

## When to Use

- Building a `dotnet` console tool with one or more named (optionally namespaced) commands.
- Declaring typed positional arguments and `--long` / `-s` options with defaults, arrays, and generated help.
- Asking interactively for missing input, skippable with `--no-interaction`.
- Running commands that return `Result` or `Async` and mapping failures to exit codes.
- Testing a CLI end-to-end by capturing output into a buffer and asserting on the parsed `Input`.

## When NOT to Use

- Parsing arguments into a typed record via reflection or attributes (Argu-style) — values here are strings read by name.
- Single-command tools that take positional arguments directly (`tool file.txt`) — the first argv token is always resolved as a command name; only an empty or options-only argv falls back to the default command.
- Long-running hosts or services — the library runs one command and returns an exit code.

## Main Concepts

- **`consoleApplication`** — builder CE (in the auto-opened `MFConsoleApplication` module); produces a `ConsoleApplication`. Definition errors (invalid names, invalid argument/option sets) surface when it is run, not when it is built.
- **`run` / `runResult` / `runAsyncResult` / `runInteractively`** — execute with `argv`: `run` prints errors and returns an `int` exit code; `runResult` returns `Result<ExitCode, ConsoleApplicationError>`; `runAsyncResult` returns the error paired with the current command; `runInteractively` loops prompting for commands until `exit`.
- **`CommandDefinition`** — record with all of `Description`, `Help: string option`, `Arguments`, `Options`, `Initialize: Initialize option`, `Interact: Interact option`, `Execute: Execute`.
- **`Execute`** — union: `Execute of (IO -> ExitCode)`, `ExecuteResult of (IO -> Result<ExitCode, ConsoleApplicationError>)`, `ExecuteAsync of (IO -> Async<ExitCode>)`, `ExecuteAsyncResult of (IO -> Async<Result<ExitCode, ConsoleApplicationError>>)`.
- **`IO`** — `Input * Output`; `Output` is `Feather.ConsoleStyle.ConsoleStyle`. `Initialize = IO -> IO`; `Interact = InteractiveInput * Output -> IO`, where `InteractiveInput = { Input: Input; Ask: string -> string }`.
- **`Argument.*` / `Option.*`** — definition constructors returning `Result<RawArgumentDefinition, ArgumentDefinitionError>` / `Result<RawOptionDefinition, OptionDefinitionError>`; placed directly into the `Arguments` / `Options` lists and validated when the command is registered.
- **`Input`** — record with `Arguments: Map<string, ArgumentValue>` and `Options: Map<string, OptionValue>`; read through `Input.Argument.*` and `Input.Option.*` (active patterns `IsDefined`, `Has`, `IsSet`, `Value`, `OptionalValue`, `ListValue`; functions `tryGet`, `get`, `value`, `asString`, `asInt`, `tryAsInt`, `asList`, `isValueSet`, `set`, `setList`; `Input.Option.has`).
- **`ArgumentValue`** — `Required` | `Optional` | `Array` | `RequiredArray of NotEmptyList<_>`; **`OptionValue`** — `ValueNone` | `ValueRequired` | `ValueOptional` | `ValueIsArray` | `ValueRequiredArray`. Both modules expose `value`, `stringValue`, `intValue`, `tryIntValue`, `listValue`.
- **`ExitCode`** — `Success` | `Error`; `ExitCode.code` maps to `0` / `1`.
- **`ConsoleApplicationError`** — `ArgsError` | `CommandNameError` | `ApplicationNameError` | `CommandDefinitionError` | `ConsoleApplicationException of exn` | `CommandError of CommandError`; `CommandError` is `Exception of exn` | `Message of string` | `Errors of CommandError list`.
- **`ApplicationInfo`** — header printed before each command: `Hidden` (default), `MainTitle`, `NameAndVersion`, `OnlyNameAndVersion`, `Interactive`, `All`. **`OptionDecorationLevel`** — `Minimal` (default) | `Complete`, for usage lines in help.
- **Built-ins** — commands `list` (default command), `help`, `about`; global options `--help/-h`, `--quiet/-q`, `--version/-V`, `--no-interaction/-n`, `--no-progress`, `--no-ansi`, `--verbose/-v|vv|vvv`.

## Related Libraries

- **Feather.ConsoleStyle** — supplies `Output` (`ConsoleStyle`), `<c:color>` markup, `Ask`, progress bars, `Style` / `CustomTag` for `withStyle` / `withCustomTags`, and `Output.BufferOutput` for tests.
- **Feather.ErrorHandling** — supplies the `result {}` / `asyncResult {}` CEs and `AsyncResult` used with `ExecuteResult` / `ExecuteAsyncResult` and `runAsyncResult`.

## Keywords for Search

Feather.ConsoleApplication, console-application, CLI, command line, consoleApplication, computation expression, MFConsoleApplication, run, runResult, runAsyncResult, runInteractively, CommandDefinition, Initialize, Interact, Execute, ExecuteResult, ExecuteAsync, ExecuteAsyncResult, IO, InteractiveInput, Ask, Input, Input.Argument, Input.Option, Argument.required, Argument.optional, Argument.requiredArray, Argument.optionalArray, Option.noValue, Option.required, Option.optional, Option.optionalArray, Option.requiredArray, ArgumentValue, OptionValue, NotEmptyList, ExitCode, ConsoleApplicationError, CommandError, ArgsError, CommandDefinitionError, ApplicationInfo, OptionDecorationLevel, showOptions, defaultCommand, useOutput, useAsk, withCustomTags, Help.lines, list, help, about, --no-interaction, --no-ansi, --no-progress, shortcut, namespace, Symfony Console, ConsoleStyle, BufferOutput, F#

## Reference Files

- For definition rules, life-cycle order, input access, error handling, and testing guidance, read `references/preferred-patterns.md`.
- For known pitfalls, the outdated README/docs API, and throwing accessors, read `references/anti-patterns.md`.
- For worked, self-contained code examples ordered by increasing complexity, read `references/examples.md`.
