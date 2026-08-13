module Feather.ConsoleApplication.Tests.FallbackCommand

open Expecto
open Feather.ConsoleApplication
open Feather.ConsoleApplication.Tests.Commands

let private runWithFallback argv =
    let mutable (input: Input option) = None
    let setInput parsedInput = input <- Some parsedInput

    let result =
        consoleApplication {
            useAsk (fun _question -> "answer")

            command "two" (commandTwo setInput)
            command "six" (commandSix setInput)

            fallbackCommand "two"
        }
        |> runResult argv

    result
    |> Result.map (fun _ -> input)

let private runWithoutFallback argv =
    consoleApplication {
        useAsk (fun _question -> "answer")

        command "two" (commandTwo ignore)
    }
    |> runResult argv

let private expectInput expectedOptions expectedArguments result message =
    match result with
    | Ok (Some (input: Input)) ->
        Expect.equal input.Options (expectedOptions |> Map.ofList) $"{message} - options should match"
        Expect.equal input.Arguments (expectedArguments |> Map.ofList) $"{message} - arguments should match"
    | Ok None -> failtest $"{message} - command did not execute"
    | Error error -> failtest $"{message} - unexpected error: %A{error}"

[<Tests>]
let fallbackCommandTests =
    testList "ConsoleApplication - fallback command" [
        testCase "should run fallback command with value as argument when first argument matches no command" <| fun _ ->
            let result = runWithFallback [| "foo" |]

            expectInput
                []
                [
                    "command", ArgumentValue.Required "two"
                    "mandatoryArg", ArgumentValue.Required "foo"
                    "argumentList", ArgumentValue.Array []
                ]
                result
                "args: foo"

        testCase "should pass all values and options to fallback command when first argument matches no command" <| fun _ ->
            let result = runWithFallback [| "foo"; "bar"; "-o"; "value1" |]

            expectInput
                [ "opt1", OptionValue.ValueOptional (Some "value1") ]
                [
                    "command", ArgumentValue.Required "two"
                    "mandatoryArg", ArgumentValue.Required "foo"
                    "argumentList", ArgumentValue.Array [ "bar" ]
                ]
                result
                "args: foo bar -o value1"

        testCase "should run fallback command when first argument is not a valid command name" <| fun _ ->
            let result = runWithFallback [| "foo bar" |]

            expectInput
                []
                [
                    "command", ArgumentValue.Required "two"
                    "mandatoryArg", ArgumentValue.Required "foo bar"
                    "argumentList", ArgumentValue.Array []
                ]
                result
                "args: 'foo bar'"

        testCase "should run fallback command when arguments are given only after separator" <| fun _ ->
            let result = runWithFallback [| "--"; "foo" |]

            expectInput
                []
                [
                    "command", ArgumentValue.Required "two"
                    "mandatoryArg", ArgumentValue.Required "foo"
                    "argumentList", ArgumentValue.Array []
                ]
                result
                "args: -- foo"

        testCase "should run matched command when first argument matches a command name" <| fun _ ->
            let result = runWithFallback [| "six" |]

            expectInput
                [ "bar", OptionValue.ValueRequired "" ]
                [
                    "command", ArgumentValue.Required "six"
                    "arg", ArgumentValue.Optional None
                ]
                result
                "args: six"

        testCase "should return CommandNotFound error when no fallback command is set" <| fun _ ->
            let result = runWithoutFallback [| "foo" |]

            let expected = CommandNotFound.create "foo" |> ConsoleApplicationError.ArgsError
            Expect.equal result (Error expected) "Unknown first argument should still be an error without a fallback command"

        testCase "should return Reserved error when fallback command name is reserved" <| fun _ ->
            let result =
                consoleApplication {
                    fallbackCommand "list"
                }
                |> runResult [| "foo" |]

            let expected = ConsoleApplicationError.CommandNameError (CommandNameError.Reserved "list")
            Expect.equal result (Error expected) "Reserved fallback command name should fail application definition"
    ]
