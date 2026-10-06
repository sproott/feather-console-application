# Examples

All example code for this skill lives here. Examples are ordered by increasing complexity and each is self-contained. Neutral placeholder names are used throughout.

## Basic command

```fsharp
open Feather.ConsoleApplication

[<EntryPoint>]
let main argv =
    consoleApplication {
        name "Greeter"
        version "1.0.0"
        info ApplicationInfo.NameAndVersion

        command "greet" {
            Description = "Greets a user."
            Help = None
            Arguments = [
                Argument.required "name" "Who to greet."
            ]
            Options = [
                Option.noValue "yell" (Some "y") "Greet in upper case."
            ]
            Initialize = None
            Interact = None
            Execute = Execute <| fun (input, output) ->
                let greeting = input |> Input.Argument.value "name" |> sprintf "Hello, %s."

                match input with
                | Input.Option.Has "yell" _ -> greeting.ToUpper()
                | _ -> greeting
                |> output.Message

                ExitCode.Success
        }
    }
    |> run argv
```

`dotnet greeter.dll greet Ann -y` (or the prefix `g Ann -y`):

```
Greeter <1.0.0>
===============

HELLO, ANN.
```

`dotnet greeter.dll greet` prints `Not enough arguments (missing: "name").` plus the usage line and exits with `1`. With no arguments the default `list` command runs.

## Arguments and options

Every input variant and the accessor that is safe for it.

```fsharp
open Feather.ConsoleApplication

let importCommand: CommandDefinition = {
    Description = "Imports files into a target."
    Help = None
    Arguments = [
        Argument.required "target" "Target name."
        Argument.optional "mode" "Import mode." (Some "append")
        Argument.optionalArray "files" "Files to import." None
    ]
    Options = [
        Option.noValue "dry-run" (Some "d") "Do not write anything."
        Option.required "batch-size" (Some "b") "Items per batch." "100"
        Option.optional "report" (Some "r") "Write a report, optionally to a file." None
        Option.optionalArray "tag" (Some "t") "Tags to attach." None
    ]
    Initialize = None
    Interact = None
    Execute = Execute <| fun (input, output) ->
        // declared arguments always have an entry after parsing
        let target = input |> Input.Argument.value "target"
        let mode = input |> Input.Argument.asString "mode" |> Option.defaultValue "append"
        let files = input |> Input.Argument.asList "files"

        // `required` options always carry their default
        let batchSize = input |> Input.Option.tryAsInt "batch-size" |> Option.defaultValue 100

        // options without a default may be absent: use `has` or active patterns
        let dryRun = input |> Input.Option.has "dry-run"

        let tags =
            match input with
            | Input.Option.ListValue "tag" tags -> tags
            | _ -> []

        let report =
            match input with
            | Input.Option.OptionalValue "report" file -> Some (Some file)  // --report out.txt
            | Input.Option.Has "report" _ -> Some None                      // --report
            | _ -> None                                                     // not passed

        output.Message $"{target} {mode} {files} dry-run={dryRun} batch={batchSize} tags={tags} report={report}"
        ExitCode.Success
}
```

`import db -d -t a --tag=b -- replace f1 f2` gives `target = "db"`, `mode = "replace"`, `files = ["f1"; "f2"]`, `dryRun = true`, `batchSize = 100`, `tags = ["a"; "b"]`.

## Realistic

Application metadata, a namespaced default command with generated help, and a domain error mapped through `ExecuteResult`.

```fsharp
open Feather.ConsoleApplication

type ExportError =
    | UnknownFormat of string

let export format =
    match format with
    | "csv" | "json" -> Ok ()
    | other -> Error (UnknownFormat other)

let exportCommand: CommandDefinition = {
    Description = "Exports data in the given format."
    Help =
        Help.lines [
            "The <c:green>{{command.name}}</c> command exports data:"
            "        <c:green>dotnet {{command.full_name}} --format json</c>"
        ]
    Arguments = []
    Options = [
        Option.required "format" (Some "f") "Output format (csv, json)." "csv"
    ]
    Initialize = None
    Interact = None
    Execute = ExecuteResult <| fun (input, output) ->
        let format = input |> Input.Option.value "format"

        format
        |> export
        |> Result.map (fun () ->
            output.Success $"Exported as {format}."
            ExitCode.Success
        )
        |> Result.mapError (function
            | UnknownFormat format ->
                ConsoleApplicationError.CommandError (CommandError.Message $"Unknown format \"{format}\".")
        )
}

[<EntryPoint>]
let main argv =
    consoleApplication {
        title "Data Tool"
        name "data-tool"
        version "2.1.0"
        description "Exports and imports data."
        info ApplicationInfo.NameAndVersion
        git "https://example.com/data-tool.git" "main" "abc1234"
        meta ("Owner", "team-a")
        showOptions Complete
        defaultCommand "data:export"

        command "data:export" exportCommand
    }
    |> run argv
```

- `data-tool` (no args) runs `data:export` with `format = "csv"`.
- `data-tool d:e -f xml` prints `Unknown format "xml".` and exits with `1`.
- `data-tool data:export --help` prints description, the full usage line (`showOptions Complete`), options with `[default: "csv"]`, and the help text with placeholders replaced.
- `data-tool about` shows the name, version, description, `Owner`, git values, environment, and library version.

## Interaction

`Initialize` runs first; `Interact` asks for missing values and is skipped by `-n`. Missing required arguments are only reported after `Interact`.

```fsharp
open Feather.ConsoleApplication

let createUserCommand: CommandDefinition = {
    Description = "Creates a user."
    Help = None
    Arguments = [
        Argument.required "username" "Login name."
    ]
    Options = [
        Option.optional "email" (Some "e") "Contact e-mail." None
    ]
    Initialize = Some (fun (input, output) ->
        output.Section "Create user"
        (input, output)
    )
    Interact = Some (fun ({ Input = input; Ask = ask }, output) ->
        let input =
            match input with
            | Input.Argument.Has "username" _ -> input
            | _ -> ask "Username:" |> Input.Argument.set input "username"

        let input =
            match input with
            | Input.Option.IsSet "email" _ -> input
            | _ -> ask "E-mail:" |> Input.Option.set input "email"

        (input, output)
    )
    Execute = Execute <| fun (input, output) ->
        let username = input |> Input.Argument.value "username"

        let email =
            match input with
            | Input.Option.OptionalValue "email" email -> email
            | _ -> "-"

        output.Message $"{username} <{email}>"
        ExitCode.Success
}
```

- `user:create` asks for both values.
- `user:create bob -n` skips the questions and prints `bob <->`.
- `user:create -n` fails with `Not enough arguments (missing: "username").`

## Async command

`ExecuteAsyncResult` with `asyncResult {}` from Feather.ErrorHandling.

```fsharp
open Feather.ConsoleApplication
open Feather.ErrorHandling

let checkStatus (url: string): Async<Result<int, string>> =
    async { return if url.StartsWith "https" then Ok 200 else Error "Only https is supported." }

let checkCommand: CommandDefinition = {
    Description = "Checks an URL."
    Help = None
    Arguments = [ Argument.required "url" "URL to check." ]
    Options = []
    Initialize = None
    Interact = None
    Execute = ExecuteAsyncResult <| fun (input, output) ->
        asyncResult {
            let url = input |> Input.Argument.value "url"

            let! status =
                checkStatus url
                |> AsyncResult.mapError (CommandError.Message >> ConsoleApplicationError.CommandError)

            output.Message $"{url} -> {status}"
            return ExitCode.Success
        }
}
```

## Progress bar

The library wires ConsoleStyle progress bars to `--no-progress` and debug verbosity.

```fsharp
open Feather.ConsoleApplication

let processCommand: CommandDefinition = {
    Description = "Processes items."
    Help = None
    Arguments = [ Argument.requiredArray "items" "Items to process." ]
    Options = []
    Initialize = None
    Interact = None
    Execute = Execute <| fun (input, output) ->
        let items = input |> Input.Argument.asList "items"
        let progress = output.ProgressStart "Processing" items.Length

        items
        |> List.iter (fun _item ->
            // process the item
            output.ProgressAdvance progress
        )

        output.ProgressFinish progress
        ExitCode.Success
}
```

## Custom output and tags

```fsharp
open Feather.ConsoleApplication
open Feather.ConsoleStyle

let highlight = CustomTag.createAndParseMarkup (TagName "hl") "<c:black|bg:cyan>"

let runCommand: CommandDefinition = {
    Description = "Runs the job."
    Help = None
    Arguments = []
    Options = []
    Initialize = None
    Interact = None
    Execute = Execute <| fun (_, output) ->
        output.Title("Job <hl>%s</hl> is running", "nightly")
        ExitCode.Success
}

[<EntryPoint>]
let main argv =
    consoleApplication {
        name "Jobs"
        withCustomTags [ highlight ]
        defaultCommand "run"
        command "run" runCommand
    }
    |> run argv
```

`withCustomTags` also accepts a plain `CustomTag list`; `useOutput` / `updateOutput` replace or adjust the whole `ConsoleStyle`.

## Testing a command

Mirrors the library's own tests (Expecto): buffer output, fixed answers, `runResult`, and the captured `Input`.

```fsharp
open Expecto
open Feather.ConsoleApplication
open Feather.ConsoleStyle

let createUser onExecute: CommandDefinition = {
    Description = "Creates a user."
    Help = None
    Arguments = [ Argument.required "username" "Login name." ]
    Options = []
    Initialize = None
    Interact = Some (fun ({ Input = input; Ask = ask }, output) ->
        match input with
        | Input.Argument.Has "username" _ -> (input, output)
        | _ -> (ask "Username:" |> Input.Argument.set input "username", output)
    )
    Execute = Execute <| fun (input, output) ->
        onExecute input
        output.Message "created"
        ExitCode.Success
}

let runCaptured argv =
    use buffer = new Output.BufferOutput(Verbosity.Normal)
    let console = ConsoleStyle(buffer)
    let mutable captured: Input option = None

    let result =
        consoleApplication {
            useOutput console
            useAsk (fun _question -> "answer")
            command "user:create" (createUser (fun input -> captured <- Some input))
        }
        |> runResult argv

    result, captured, buffer.Fetch() |> console.RemoveMarkup

[<Tests>]
let userCreateTests =
    testList "user:create" [
        testCase "should ask for a missing username" <| fun _ ->
            let result, captured, _ = runCaptured [| "user:create" |]

            Expect.equal result (Ok ExitCode.Success) "command should succeed"
            Expect.equal
                (captured |> Option.map (fun input -> input.Arguments.["username"]))
                (Some (ArgumentValue.Required "answer"))
                "username should come from the interaction"

        testCase "should fail on a missing username when interaction is disabled" <| fun _ ->
            let result, _, _ = runCaptured [| "user:create"; "-n" |]

            let expected =
                ArgumentsError.NotEnoughArguments "username"
                |> InputError.ArgumentsError
                |> ArgsError.InputError
                |> ConsoleApplicationError.ArgsError
                |> Error

            Expect.equal result expected "missing argument should be reported"

        testCase "should report an unknown command" <| fun _ ->
            let result, _, _ = runCaptured [| "user:remove" |]

            Expect.equal result (Error (ConsoleApplicationError.ArgsError (CommandNotFound.create "user:remove"))) "command should not be found"
    ]
```

## Interactive mode

```fsharp
open Feather.ConsoleApplication

let pingCommand: CommandDefinition = {
    Description = "Answers with pong."
    Help = None
    Arguments = []
    Options = []
    Initialize = None
    Interact = None
    Execute = Execute <| fun (_, output) ->
        output.Message "pong"
        ExitCode.Success
}

[<EntryPoint>]
let main argv =
    consoleApplication {
        name "Shell"
        command "ping" pingCommand
    }
    |> runInteractively argv
```

Each loop prints the available commands plus `exit`, asks `Command:`, and runs the answer as the command name. Typing `exit` returns `0`; a failing command ends the loop with its exit code.
