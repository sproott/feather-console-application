module Feather.ConsoleApplication.Tests.Input

open Expecto
open Feather.ConsoleApplication
open Feather.ConsoleApplication.Tests.Commands

type ArgsHasOption = {
    Description: string
    Argv: string []
    Option: string
    ExpectedExists: OptionValue option
    ExpectedWithValue: OptionValue option
}

exception InputWasNotSetInTheCommandException

let provideArgsHasOption = seq {
    yield {
        Description = "Empty options with interaction - should not exists"
        Argv = [| |]
        Option = "opt2"
        ExpectedExists = None
        ExpectedWithValue = None
    }
    yield {
        Description = "Empty options with interaction - should exists"
        Argv = [| |]
        Option = "message"
        ExpectedExists = Some (OptionValue.ValueOptional (Some "from-interaction"))
        ExpectedWithValue = Some (OptionValue.ValueOptional (Some "from-interaction"))
    }
    yield {
        Description = "Option by name without `=`"
        Argv = [|
            "-o"; "value1"
            "--opt2"; "value2"
        |]
        Option = "opt2"
        ExpectedExists = Some (OptionValue.ValueOptional (Some "value2"))
        ExpectedWithValue = Some (OptionValue.ValueOptional (Some "value2"))
    }
    yield {
        Description = "Options by shortuct"
        Argv = [|
            "arg1"
            "--opt2"; "overriden_value"
            "-o"; "value1"
            "--opt2=value2"
        |]
        Option = "opt1"
        ExpectedExists = Some (OptionValue.ValueRequired "value1")
        ExpectedWithValue = Some (OptionValue.ValueRequired "value1")
    }

    yield {
        Description = "No interaction"
        Argv = [| "-n"; "--"; "value" |]
        Option = "message"
        ExpectedExists = None
        ExpectedWithValue = None
    }
    yield {
        Description = "Option value not consume separator -- as value"
        Argv = [| "--opt2"; "--"; "arg1" |]
        Option = "opt2"
        ExpectedExists = Some (OptionValue.ValueOptional None)
        ExpectedWithValue = None
    }
    yield {
        Description = "Option value not consume shortcut as value"
        Argv = [|
            "--opt2"
            "-o"; "value1"
            "--"; "arg1"
        |]
        Option = "opt2"
        ExpectedExists = Some (OptionValue.ValueOptional None)
        ExpectedWithValue = None
    }
    yield {
        Description = "Option as shortcut not consume shortcut as value"
        Argv = [|
            "-O"
            "-o"; "value1"
            "--"; "arg1"
        |]
        Option = "opt2"
        ExpectedExists = Some (OptionValue.ValueOptional None)
        ExpectedWithValue = None
    }
    yield {
        Description = "Option 2 without a value"
        Argv = [|
            "--opt2"
            "--opt1=x"
            "--"; "--opt1=as-argument"
        |]
        Option = "opt2"
        ExpectedExists = Some (OptionValue.ValueOptional None)
        ExpectedWithValue = None
    }
    yield {
        Description = "Option 1 with `=`, Option after separator used as argument"
        Argv = [|
            "--opt2"
            "--opt1=x"
            "--"; "--opt1=as-argument"
        |]
        Option = "opt1"
        ExpectedExists = Some (OptionValue.ValueRequired "x")
        ExpectedWithValue = Some (OptionValue.ValueRequired "x")
    }
    yield {
        Description = "Option with empty value"
        Argv = [|
            "--opt2="
            "-o"; "value1"
            "--"; "arg1"
        |]
        Option = "opt2"
        ExpectedExists = Some (OptionValue.ValueOptional (Some ""))
        ExpectedWithValue = Some (OptionValue.ValueOptional (Some ""))
    }

    yield {
        Description = "No arguments and empty options with no-interaction shortuct - should not exists"
        Argv = [| |]
        Option = "no-interaction"
        ExpectedExists = None
        ExpectedWithValue = None
    }
    yield {
        Description = "No arguments and empty options with no-interaction shortuct - should exists"
        Argv = [| "-n" |]
        Option = "no-interaction"
        ExpectedExists = Some OptionValue.ValueNone
        ExpectedWithValue = Some OptionValue.ValueNone
    }
    yield {
        Description = "No arguments and empty options with no-interaction"
        Argv = [| "--no-interaction" |]
        Option = "no-interaction"
        ExpectedExists = Some OptionValue.ValueNone
        ExpectedWithValue = Some OptionValue.ValueNone
    }
}

let runConsoleApplication argv =
    let argv = [|
        yield "test"
        yield! argv
    |]
    let mutable (input: Input option) = None

    consoleApplication {
        command "test" (commandOne <| fun parsedInput ->
            input <- Some parsedInput
        )
    }
    |> runResult argv
    |> function
        | Ok _ -> ()
        | Error e -> failtestf "Command did not end successfully.\n%A\n" e

    match input with
    | Some input -> input
    | None -> raise InputWasNotSetInTheCommandException

[<Tests>]
let ``input should have options`` =
    testList "ConsoleApplication - input has option" [
        yield!
            provideArgsHasOption
            |> Seq.map (fun { Argv = argv; Option = option; ExpectedExists = expected; Description = description } ->
                testCase $"from console args - {description}" <| fun _ ->
                    let description = sprintf "args: %s\n%s" (argv |> String.concat " ") description

                    let input = runConsoleApplication (argv |> Array.append [| "argument" |])
                    let description = sprintf "%s\nOptions: %A" description input.Options

                    let result =
                        match input with
                        | Input.Option.Has option value -> Some value
                        | _ -> None

                    let resultValue =
                        try Some (input |> Input.Option.get option)
                        with
                        | _ -> None

                    Expect.equal result expected description
                    Expect.equal resultValue expected description
            )
    ]

[<Tests>]
let ``input should have option with value`` =
    testList "ConsoleApplication - is set option in input" [
        yield!
            provideArgsHasOption
            |> Seq.map (fun { Argv = argv; Option = option; ExpectedExists = expectedValue; ExpectedWithValue = expected; Description = description } ->
                testCase $"from console args - {description}" <| fun _ ->
                    let description = sprintf "args: %s\n%s" (argv |> String.concat " ") description

                    let input = runConsoleApplication (argv |> Array.append [| "argument" |])
                    let description = sprintf "%s\nOptions: %A" description input.Options

                    let result =
                        match input with
                        | Input.Option.IsSet option value -> Some value
                        | _ -> None

                    let resultValue =
                        try Some (input |> Input.Option.get option)
                        with
                        | _ -> None

                    Expect.equal result expected description
                    Expect.equal resultValue expectedValue description
                )
    ]

type ArgsHasArgument = {
    Description: string
    Argv: string []
    Argument: string
    Expected: ArgumentValue option
}

let provideArgsHasArgument = seq {
    yield {
        Description = "No arguments with interaction"
        Argv = [| "value" |]
        Argument = "optionalArg"
        Expected = Some (ArgumentValue.Optional (Some "default"))
    }
    yield {
        Description = "No arguments, just options"
        Argv = [| "-o"; "value1"; "--opt2"; "value2"; "value" |]
        Argument = "optionalArg"
        Expected = Some (ArgumentValue.Optional (Some "default"))
    }

    yield {
        Description = "Argument value with no interaction"
        Argv = [| "-n"; "--"; "mandatory" |]
        Argument = "optionalArg"
        Expected = Some (ArgumentValue.Optional (Some "default"))
    }
    yield {
        Description = "Argument value and option value not consume separator -- as value"
        Argv = [| "--opt2"; "--"; "mandatory"; "arg1" |]
        Argument = "optionalArg"
        Expected = Some (ArgumentValue.Optional (Some "arg1"))
    }
    yield {
        Description = "Argument value as option after separator --"
        Argv = [| "--opt2"; "--opt1=x"; "--"; "--opt1=as-argument" |]
        Argument = "mandatoryArg"
        Expected = Some (ArgumentValue.Required "--opt1=as-argument")
    }
}

[<Tests>]
let ``input should have arguments`` =
    testList "ConsoleApplication - input has argument" [
        yield!
            provideArgsHasArgument
            |> Seq.map (fun { Argv = argv; Argument = argument; Expected = expected; Description = description } ->
                testCase $"from console args - {description}" <| fun _ ->
                    let description = sprintf "args: %s\n%s" (argv |> String.concat " ") description

                    let input = runConsoleApplication argv
                    let description = sprintf "%s\nArguments: %A" description input.Arguments

                    let result =
                        match input with
                        | Input.Argument.Has argument value -> Some value
                        | _ -> None

                    let resultValue = input |> Input.Argument.get argument

                    Expect.equal result expected description
                    Expect.equal (Some resultValue) expected description
            )
    ]
