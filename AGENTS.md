# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Build system uses FAKE 6. `./build.sh` handles tool restore and Paket restore before running.

```bash
# Build
./build.sh -t Build

# Run tests
./build.sh -t Tests

# Lint (FSharpLint)
./build.sh -t Lint

# Pack NuGet package
./build.sh -t Release

# Publish to NuGet (requires NUGET_API_KEY env var)
./build.sh -t Publish

# Watch mode
./build.sh -t Watch
```

Run a single test by name with Expecto's `--filter` flag:
```bash
dotnet test tests/tests.fsproj -- --filter "test name substring"
```

## Architecture

This is an F# library (`Feather.ConsoleApplication`) for building CLI applications. Ships as a NuGet package targeting .NET 10.

### Layers (bottom-up)

**Domain types & validation** (`src/Types.fs`, `src/Arguments.fs`, `src/Options.fs`)
- Private wrapper types (`Name`, `CommandName`, `OptionName`, etc.) prevent invalid values at the type level
- All construction goes through validated factory functions returning `Result`
- `ArgumentValueDefinition` (Required/Optional/Array/RequiredArray) and `OptionValueDefinition` (5 variants) define command signatures

**Parsing & input** (`src/Input.fs`)
- `Input` record holds parsed arguments + options alongside their definitions
- Active patterns (`Input.Argument.Has`, `Input.Argument.Value`, `Input.Option.Has`, etc.) are the intended API for command handlers to access input — prefer these over raw record access

**Command definition & dispatch** (`src/Command.fs`)
- `CommandDefinition` (raw, before validation) → `Command` (validated, private)
- Three lifecycle hooks per command: `Initialize` (pre-parse setup), `Interact` (interactive prompting), `Execute` (main handler)
- `Execute` DU supports sync/async × result/unit variants

**Shell completion** (`src/Completion/`, namespace `Feather.ConsoleApplication.Completion`)
- One module per file, compiled in this order: `Shell` (supported shells) → `Words` (normalizes the words the shell passed) → `Definitions` (option/command lookups) → `Suggestions` (`resolve`) → `ShellScript` (script generation) → `Setup` (`validateConfig`, `wire`)
- `Setup.wire` adds the user-facing script command plus the hidden `__completion` resolver command the generated script calls back into

**Fluent builder** (`src/Builder.fs`)
- `consoleApplication { ... }` computation expression builds the app
- Custom CE operations: `command`, `name`, `version`, `title`, `info`, `showOptions`, `defaultCommand`, `withStyle`, `withCustomTags`

**Runtime dispatcher** (`src/ConsoleApplication.fs`)
- `runAsyncResult` is the core entrypoint; handles built-in flags (--help, --version, --quiet, --verbose, --no-ansi), resolves command, runs Initialize → Interact → Execute lifecycle
- Error operators `<!!*>`, `<*!!*>`, `<!!!>` attach command context to errors for richer messages
- `runResult`, `run`, `runInteractively` are convenience wrappers around `runAsyncResult`

**Rendering** (`src/Render.fs`)
- Help and error rendering only; output styling delegated to `Feather.ConsoleStyle`
- `{{command.name}}` and `{{command.full_name}}` placeholders supported in help text

### Key dependencies

- **Feather.ErrorHandling** — `Result`/`AsyncResult` operators used throughout
- **Feather.ConsoleStyle** — `Output`/`ConsoleStyle` type for styled terminal output (aliased as `Output` in this lib)
- **ShellProgressBar** — wrapped by `src/Progress.fs`
- **Expecto** — test framework

### Testing pattern

Tests use `BufferOutput` to capture console output and assert on exact formatted strings. See `tests/DefaultCommandsTests.fs` for examples. Test fixtures (reusable command definitions) live in `tests/Fixtures/Commands.fs`.
