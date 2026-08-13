module Feather.ConsoleApplication.Tests.Args

open Expecto
open Feather.ConsoleApplication
open Feather.ConsoleApplication.Tests.Commands

type Expected =
    | Input of Input
    | ExpectedError of ConsoleApplicationError

type Args = {
    Argv: string []
    Command: string
    Expected: Expected
    Description: string
}

let expect options arguments =
    Input {
        Options = options |> Map.ofList
        Arguments = arguments |> Map.ofList
        ArgumentDefinitions = []
        OptionDefinitions = []
    }

let provideArgs = seq {
    let messageFromInteraction = "message", OptionValue.ValueOptional (Some "from-interaction")

    yield {
        Description = "No arguments and empty options with interaction"
        Command = "one"
        Argv = [| "arg" |]
        Expected = expect [
            messageFromInteraction
            "opt1", OptionValue.ValueRequired "opt1-default-value"
        ] [
            "mandatoryArg", ArgumentValue.Required "arg"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Options by shortuct and by name without `=`"
        Command = "one"
        Argv = [|
            "-o"; "value1"
            "--opt2"; "value2"
            "arg"
        |]
        Expected = expect [
            messageFromInteraction
            "opt1", OptionValue.ValueRequired "value1"
            "opt2", OptionValue.ValueOptional (Some "value2")
        ] [
            "mandatoryArg", ArgumentValue.Required "arg"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Options by shortuct and by name overriding first one and with arguments at the beginning"
        Command = "one"
        Argv = [|
            "arg1"
            "--opt2"; "overriden_value"
            "-o"; "value1"
            "--opt2=value2"
        |]
        Expected = expect [
            "opt1", OptionValue.ValueRequired "value1"
            "opt2", OptionValue.ValueOptional (Some "value2")
            messageFromInteraction
        ] [
            "mandatoryArg", ArgumentValue.Required "arg1"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Options by shortuct and by name overriding first one and with arguments in the middle"
        Command = "one"
        Argv = [|
            "--opt2"; "overriden_value"
            "arg1"
            "-o"; "value1"
            "--opt2=value2"
        |]
        Expected = expect [
            "opt1", OptionValue.ValueRequired "value1"
            "opt2", OptionValue.ValueOptional (Some "value2")
            messageFromInteraction
        ] [
            "mandatoryArg", ArgumentValue.Required "arg1"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Options by shortuct and by name overriding first one and with arguments in the end"
        Command = "one"
        Argv = [|
            "--opt2"; "overriden_value"
            "-o"; "value1"
            "--opt2=value2"
            "arg1"
        |]
        Expected = expect [
            "opt1", OptionValue.ValueRequired "value1"
            "opt2", OptionValue.ValueOptional (Some "value2")
            messageFromInteraction
        ] [
            "mandatoryArg", ArgumentValue.Required "arg1"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Options and arguments between them"
        Command = "two"
        Argv = [|
            "first"
            "--opt1"; "overriden_value"
            "two"
            "-o"; "value1"
            "three"
            "--opt1=value2"
            "four"
            "five"
        |]
        Expected = expect [
            "opt1", OptionValue.ValueOptional (Some "value2")
        ] [
            "mandatoryArg", ArgumentValue.Required "first"
            "argumentList", ArgumentValue.Array [ "two"; "three"; "four"; "five" ]
        ]
    }
    yield {
        Description = "Argument value is -"
        Command = "one"
        Argv = [| "-" |]
        Expected = expect [
            messageFromInteraction
            "opt1", OptionValue.ValueRequired "opt1-default-value"
        ] [
            "mandatoryArg", ArgumentValue.Required "-"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Undefined option"
        Command = "one"
        Argv = [| "--undefined-option" |]
        Expected =
            OptionsError.UndefinedOption "--undefined-option"
            |> InputError.OptionsError
            |> ArgsError.InputError
            |> ConsoleApplicationError.ArgsError
            |> ExpectedError
    }
    yield {
        Description = "Options array and arguments between them"
        Command = "two"
        Argv = [|
            "one"
            "-i"; "one"
            "--item"; "two"
            "two"
            "-o"
            "--item=three"
            "three"
            "four"
        |]
        Expected = expect [
            "opt1", OptionValue.ValueOptional None
            "item", OptionValue.ValueIsArray [ "one"; "two"; "three" ]
        ] [
            "mandatoryArg", ArgumentValue.Required "one"
            "argumentList", ArgumentValue.Array [ "two"; "three"; "four" ]
        ]
    }
    yield {
        Description = "Options array and Required arguments between them"
        Command = "four"
        Argv = [|
            "one"
            "-i"; "one"
            "-i"; "two"
            "-o"; "value"
            "two"
            "-i"; "three"
            "three"
            "four"
        |]
        Expected = expect [
            "opt1", OptionValue.ValueOptional (Some "value")
            "item", OptionValue.ValueIsArray [ "one"; "two"; "three" ]
        ] [
            "mandatoryArg", ArgumentValue.Required "one"
            "mandatoryArgumentList", ArgumentValue.RequiredArray (NotEmptyList.ofListWithValues [ "two"; "three"; "four" ])
        ]
    }

    yield {
        Description = "Argument and empty options with interaction, with separator"
        Command = "one"
        Argv = [| "--"; "value" |]
        Expected = expect [
            messageFromInteraction
            "opt1", OptionValue.ValueRequired "opt1-default-value"
        ] [
            "mandatoryArg", ArgumentValue.Required "value"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Option value not consume separator -- as value"
        Command = "one"
        Argv = [| "--opt2"; "--"; "arg1" |]
        Expected = expect [
            messageFromInteraction
            "opt1", OptionValue.ValueRequired "opt1-default-value"
            "opt2", OptionValue.ValueOptional None
        ] [
            "mandatoryArg", ArgumentValue.Required "arg1"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Option value not consume shortcut as value"
        Command = "one"
        Argv = [|
            "--opt2"
            "-o"; "value1"
            "--"
            "arg1"
        |]
        Expected = expect [
            messageFromInteraction
            "opt1", OptionValue.ValueRequired "value1"
            "opt2", OptionValue.ValueOptional None
        ] [
            "mandatoryArg", ArgumentValue.Required "arg1"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Option as shortcut not consume shortcut as value"
        Command = "one"
        Argv = [|
            "-O"
            "-o"; "value1"
            "--"
            "arg1"
        |]
        Expected = expect [
            messageFromInteraction
            "opt1", OptionValue.ValueRequired "value1"
            "opt2", OptionValue.ValueOptional None
        ] [
            "mandatoryArg", ArgumentValue.Required "arg1"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Option 1 with `=`, Option 2 without a value, Option after separator used as argument, with interaction"
        Command = "one"
        Argv = [|
            "--opt2"
            "--opt1=x"
            "--"
            "--opt1=as-argument"
        |]
        Expected = expect [
            messageFromInteraction
            "opt1", OptionValue.ValueRequired "x"
            "opt2", OptionValue.ValueOptional None
        ] [
            "mandatoryArg", ArgumentValue.Required "--opt1=as-argument"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Options in different order and with empty value and argument after separator"
        Command = "one"
        Argv = [|
            "--opt2="
            "-o"; "value1"
            "--"
            "arg1"
        |]
        Expected = expect [
            messageFromInteraction
            "opt1", OptionValue.ValueRequired "value1"
            "opt2", OptionValue.ValueOptional (Some "")
        ] [
            "mandatoryArg", ArgumentValue.Required "arg1"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Argument value is --"
        Command = "one"
        Argv = [| "--"; "--" |]
        Expected = expect [
            messageFromInteraction
            "opt1", OptionValue.ValueRequired "opt1-default-value"
        ] [
            "mandatoryArg", ArgumentValue.Required "--"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "Valid options, args with Undefined option shortcut"
        Command = "one"
        Argv = [|
            "--opt2"
            "-U"
            "--"
            "foo"
        |]
        Expected =
            OptionsError.UndefinedOption "-U"
            |> InputError.OptionsError
            |> ArgsError.InputError
            |> ConsoleApplicationError.ArgsError
            |> ExpectedError
    }
    yield {
        Description = "Too many arguments"
        Command = "one"
        Argv = [| "--"; "too"; "many"; "arguments" |]
        Expected =
            ArgumentsError.TooManyArguments [ "mandatoryArg"; "optionalArg" ]
            |> InputError.ArgumentsError
            |> ArgsError.InputError
            |> ConsoleApplicationError.ArgsError
            |> ExpectedError
    }
    yield {
        Description = "Arguments as array"
        Command = "two"
        Argv = [| "--"; "--mandatory"; "too"; "many"; "arguments" |]
        Expected = expect [] [
            "mandatoryArg", ArgumentValue.Required "--mandatory"
            "argumentList", ArgumentValue.Array [ "too"; "many"; "arguments" ]
        ]
    }
    yield {
        Description = "Arguments as required array"
        Command = "three"
        Argv = [| "--"; "optional"; "too"; "many"; "arguments" |]
        Expected = expect [
            "opt1", OptionValue.ValueRequired "opt1-default-value"
            "item", OptionValue.ValueRequiredArray ["foo"]
        ] [
            "optionalArg", ArgumentValue.Optional (Some "optional")
            "argumentList", ArgumentValue.Array [ "too"; "many"; "arguments" ]
        ]
    }
    yield {
        Description = "No optional arguments"
        Command = "three"
        Argv = [| "--" |]
        Expected = expect [
            "opt1", OptionValue.ValueRequired "opt1-default-value"
            "item", OptionValue.ValueRequiredArray ["foo"]
        ] [
            "optionalArg", ArgumentValue.Optional None
            "argumentList", ArgumentValue.Array []
        ]
    }
    yield {
        Description = "Arguments as required array without values, because of optional argument"
        Command = "four"
        Argv = [| "--"; "optional" |]
        Expected =
            ArgumentsError.NotEnoughArguments "mandatoryArgumentList"
            |> InputError.ArgumentsError
            |> ArgsError.InputError
            |> ConsoleApplicationError.ArgsError
            |> ExpectedError
    }
    yield {
        Description = "Options array and Required arguments after separator"
        Command = "four"
        Argv = [|
            "-i"; "one"
            "-i"; "two"
            "-i"; "three"
            "--"; "one"; "two"; "-i"; "four"
        |]
        Expected = expect [
            "opt1", OptionValue.ValueOptional (Some "opt1-default-value")
            "item", OptionValue.ValueIsArray [ "one"; "two"; "three" ]
        ] [
            "mandatoryArg", ArgumentValue.Required "one"
            "mandatoryArgumentList", ArgumentValue.RequiredArray (NotEmptyList.ofListWithValues [ "two"; "-i"; "four" ])
        ]
    }
    yield {
        Description = "Arguments before and after -- separator"
        Command = "two"
        Argv = [| "1"; "2"; "3"; "-n"; "--"; "4"; "5" |]
        Expected = expect [
            "no-interaction", OptionValue.ValueNone
        ] [
            "mandatoryArg", ArgumentValue.Required "1"
            "argumentList", ArgumentValue.Array [ "2"; "3"; "4"; "5" ]
        ]
    }
    yield {
        Description = "Arguments before -- separator"
        Command = "two"
        Argv = [| "1"; "2"; "3"; "-n"; "--" |]
        Expected = expect [
            "no-interaction", OptionValue.ValueNone
        ] [
            "mandatoryArg", ArgumentValue.Required "1"
            "argumentList", ArgumentValue.Array [ "2"; "3" ]
        ]
    }
    yield {
        Description = "Arguments array before -- separator"
        Command = "four"
        Argv = [| "1"; "2"; "3"; "--" |]
        Expected = expect [
            "opt1", OptionValue.ValueOptional (Some "opt1-default-value")
            "item", OptionValue.ValueIsArray [ "foo"; "bar" ]
        ] [
            "mandatoryArg", ArgumentValue.Required "1"
            "mandatoryArgumentList", ArgumentValue.RequiredArray (NotEmptyList.ofListWithValues [ "2"; "3" ])
        ]
    }
    yield {
        Description = "Missing Arguments for array"
        Command = "four"
        Argv = [| "1"; "--" |]
        Expected =
            ArgumentsError.NotEnoughArguments "mandatoryArgumentList"
            |> InputError.ArgumentsError
            |> ArgsError.InputError
            |> ConsoleApplicationError.ArgsError
            |> ExpectedError
    }
    yield {
        Description = "Pass argument value in args."
        Command = "five"
        Argv = [| "--"; "arg" |]
        Expected = expect [] [
            "mandatoryArg", ArgumentValue.Required "arg"
        ]
    }
    yield {
        Description = "Ask for value in interaction and set missing required value."
        Command = "five"
        Argv = [| |]
        Expected = expect [] [
            "mandatoryArg", ArgumentValue.Required "answer"
        ]
    }
    yield {
        Description = "Ask for value in interaction and set missing required value with separator."
        Command = "five"
        Argv = [| "--" |]
        Expected = expect [] [
            "mandatoryArg", ArgumentValue.Required "answer"
        ]
    }
    yield {
        Description = "Skip interaction and fail on missing required value."
        Command = "five"
        Argv = [| "-n"; "--" |]
        Expected =
            ArgumentsError.NotEnoughArguments "mandatoryArg"
            |> InputError.ArgumentsError
            |> ArgsError.InputError
            |> ConsoleApplicationError.ArgsError
            |> ExpectedError
    }

    yield {
        Description = "Simple option value with `=`"
        Command = "six"
        Argv = [| "--bar=Hello" |]
        Expected =
            expect
                [ "bar", OptionValue.ValueRequired "Hello" ]
                [ "arg", ArgumentValue.Optional None ]
    }
    yield {
        Description = "Simple option value without `=`"
        Command = "six"
        Argv = [| "--bar"; "Hello" |]
        Expected =
            expect
                [ "bar", OptionValue.ValueRequired "Hello" ]
                [ "arg", ArgumentValue.Optional None ]
    }
    yield {
        Description = "Shortcut option value with `=`"
        Command = "six"
        Argv = [| "-b=Hello" |]
        Expected =
            expect
                [ "bar", OptionValue.ValueRequired "=Hello" ]
                [ "arg", ArgumentValue.Optional None ]
    }
    yield {
        Description = "Shortcut option value without `=`"
        Command = "six"
        Argv = [| "-b"; "Hello" |]
        Expected =
            expect
                [ "bar", OptionValue.ValueRequired "Hello" ]
                [ "arg", ArgumentValue.Optional None ]
    }
    yield {
        Description = "Shortcut option value without `=` and without any whitespace"
        Command = "six"
        Argv = [| "-bHello" |]
        Expected =
            expect
                [ "bar", OptionValue.ValueRequired "Hello" ]
                [ "arg", ArgumentValue.Optional None ]
    }
    yield {
        Description = "Multiple shortcut options with single `-`"
        Command = "six"
        Argv = [|
            "-fcWorld"
            "-b"; "Hello"
        |]
        Expected =
            expect
                [
                    "foo", OptionValue.ValueNone
                    "bar", OptionValue.ValueRequired "Hello"
                    "cat", OptionValue.ValueOptional (Some "World")
                ]
                [ "arg", ArgumentValue.Optional None ]
    }
    yield {
        Description = "Multiple shortcut options with single `-` - cat is eager and consume `f` shortuct"
        Command = "six"
        Argv = [|
            "-cfWorld"
            "-b"; "Hello"
        |]
        Expected =
            expect
                [
                    "bar", OptionValue.ValueRequired "Hello"
                    "cat", OptionValue.ValueOptional (Some "fWorld")
                ]
                [ "arg", ArgumentValue.Optional None ]
    }
    yield {
        Description = "Multiple shortcut options with single `-` - cat is eager and consume `b` shortuct"
        Command = "six"
        Argv = [| "-cbWorld" |]
        Expected =
            expect
                [
                    "bar", OptionValue.ValueRequired ""
                    "cat", OptionValue.ValueOptional (Some "bWorld")
                ]
                [ "arg", ArgumentValue.Optional None ]
    }
    yield {
        Description = "Multiple shortcut options with single `-` - `f` has no value, so next letter should be another shortcut"
        Command = "six"
        Argv = [| "-fWorld" |]
        Expected =
            OptionsError.UndefinedOption "-W"
            |> InputError.OptionsError
            |> ArgsError.InputError
            |> ConsoleApplicationError.ArgsError
            |> ExpectedError
    }
    yield {
        Description = "Multiple shortcut options with single `-` - `f` has no value, so next letter should be another shortcut - `c` has value after whitespace"
        Command = "six"
        Argv = [| "-fc"; "World" |]
        Expected =
            expect
                [
                    "foo", OptionValue.ValueNone
                    "bar", OptionValue.ValueRequired ""
                    "cat", OptionValue.ValueOptional (Some "World")
                ]
                [ "arg", ArgumentValue.Optional None ]
    }
    yield {
        Description = "Multiple shortcut options with single `-` - `f` has no value, so next letter should be another shortcut - `b` has value, so it consumes c and rest is argument"
        Command = "six"
        Argv = [| "-fbc"; "World" |]
        Expected =
            expect
                [
                    "foo", OptionValue.ValueNone
                    "bar", OptionValue.ValueRequired "c"
                ]
                [ "arg", ArgumentValue.Optional (Some "World") ]
    }
    yield {
        Description = "No value option shortcut passed multiple times"
        Command = "six"
        Argv = [| "-ffffff" |]
        Expected =
            expect
                [
                    "foo", OptionValue.ValueNone
                    "bar", OptionValue.ValueRequired ""
                ]
                [ "arg", ArgumentValue.Optional None ]
    }

    yield {
        Description = "No arguments and empty options with no-interaction shortuct"
        Command = "one"
        Argv = [| "-n"; "arg" |]
        Expected = expect [
            "no-interaction", OptionValue.ValueNone
            "opt1", OptionValue.ValueRequired "opt1-default-value"
        ] [
            "mandatoryArg", ArgumentValue.Required "arg"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }
    yield {
        Description = "No arguments and empty options with no-interaction"
        Command = "one"
        Argv = [| "--no-interaction"; "arg" |]
        Expected = expect [
            "no-interaction", OptionValue.ValueNone
            "opt1", OptionValue.ValueRequired "opt1-default-value"
        ] [
            "mandatoryArg", ArgumentValue.Required "arg"
            "optionalArg", ArgumentValue.Optional (Some "default")
        ]
    }

    yield {
        Description = "Match version by shortcut - and do not run execute"
        Command = "one"
        Argv = [| "-V" |]
        Expected =
            ConsoleApplicationError.ConsoleApplicationException Input.InputWasNotSetInTheCommandException
            |> ExpectedError
    }
    yield {
        Description = "Match version by option - and do not run execute"
        Command = "one"
        Argv = [| "--version" |]
        Expected =
            ConsoleApplicationError.ConsoleApplicationException Input.InputWasNotSetInTheCommandException
            |> ExpectedError
    }
    yield {
        Description = "Match verbosity by option"
        Command = "two"
        Argv = [| "--verbose"; "arg" |]
        Expected = expect [
            "verbose", OptionValue.ValueNone
        ] [
            "mandatoryArg", ArgumentValue.Required "arg"
            "argumentList", ArgumentValue.Array []
        ]
    }
    yield {
        Description = "Match verbosity (not version) by shortcut"
        Command = "two"
        Argv = [| "-v"; "arg" |]
        Expected = expect [
            "verbose", OptionValue.ValueNone
        ] [
            "mandatoryArg", ArgumentValue.Required "arg"
            "argumentList", ArgumentValue.Array []
        ]
    }
    yield {
        Description = "Match verbosity - very verbose by shortcut"
        Command = "two"
        Argv = [| "-vv"; "arg" |]
        Expected = expect [
            "verbose", OptionValue.ValueNone
        ] [
            "mandatoryArg", ArgumentValue.Required "arg"
            "argumentList", ArgumentValue.Array []
        ]
    }
    yield {
        Description = "Match both verbosity and quiet by shortcut, and not set debug mode, since queit has higher priority"
        Command = "two"
        Argv = [| "-vvv"; "-q"; "arg" |]
        Expected = expect [
            "verbose", OptionValue.ValueNone
            "quiet", OptionValue.ValueNone
        ] [
            "mandatoryArg", ArgumentValue.Required "arg"
            "argumentList", ArgumentValue.Array []
        ]
    }

    yield {
        Description = "Use command 7 without any input"
        Command = "seven"
        Argv = [| |]
        Expected = expect [
            "api-url", OptionValue.ValueRequired "https://api"
        ] [
            "arg", ArgumentValue.Optional None
        ]
    }
    yield {
        Description = "Use command 7 with force and stdout as output"
        Command = "seven"
        Argv = [| "-fo" |]
        Expected = expect [
            "api-url", OptionValue.ValueRequired "https://api"
            "force", OptionValue.ValueNone
            "output", OptionValue.ValueOptional None
        ] [
            "arg", ArgumentValue.Optional None
        ]
    }
    yield {
        Description = "Use command 7 with output to the file and arg"
        Command = "seven"
        Argv = [| "-o"; "output.txt"; "arg" |]
        Expected = expect [
            "api-url", OptionValue.ValueRequired "https://api"
            "output", OptionValue.ValueOptional (Some "output.txt")
        ] [
            "arg", ArgumentValue.Optional (Some "arg")
        ]
    }
    yield {
        Description = "Use command 7 with empty api-url option"
        Command = "seven"
        Argv = [| "--api-url"; "--"; "arg" |]
        Expected =
            OptionsError.RequiredValueNotSet "api-url"
            |> InputError.OptionsError
            |> ArgsError.InputError
            |> ConsoleApplicationError.ArgsError
            |> ExpectedError
    }
}

let runConsoleApplication command argv =
    let argv = [|
        yield command
        yield! argv
    |]
    let mutable (input: Input option) = None

    let setInput parsedInput =
        input <- Some parsedInput

    let result =
        consoleApplication {
            useAsk (fun _question -> "answer" )

            command "one" (commandOne setInput)
            command "two" (commandTwo setInput)
            command "three" (commandThree setInput)
            command "four" (commandFour setInput)
            command "five" (commandFive setInput)
            command "six" (commandSix setInput)
            command "seven" (commandSeven setInput)
        }
        |> runResult argv

    result
    |> Result.bind (fun _ ->
        match input with
        | Some input -> Ok input
        | None -> Error (ConsoleApplicationError.ConsoleApplicationException Input.InputWasNotSetInTheCommandException)
    )

[<Tests>]
let parseArgsTests =
    testList "ConsoleApplication - parsing input" [
        yield!
            provideArgs
            |> Seq.map (fun { Command = command; Argv = argv; Expected = expected; Description = description } ->
                testCase $"from console args - {description}" <| fun _ ->
                    let description = sprintf "args: %s\n%s" (argv |> String.concat " ") description

                    let expected =
                        match expected with
                        | Input expectedInput -> Input { expectedInput with Arguments = expectedInput.Arguments.Add("command", ArgumentValue.Required command)}
                        | ExpectedError e -> ExpectedError e

                    let result = runConsoleApplication command argv
                    let description = sprintf "%s\nResult:\n%A\n" description (result |> Result.map (fun input -> { input with ArgumentDefinitions = []; OptionDefinitions = [] }))

                    match result, expected with
                    | Ok result, Input expected ->
                        Expect.equal result.Options expected.Options description
                        Expect.equal result.Arguments expected.Arguments description
                    | Error result, ExpectedError expected ->
                        Expect.equal result expected description
                    | _ ->
                        failtestf "Unexpected case ...\n%A" description
            )
    ]
