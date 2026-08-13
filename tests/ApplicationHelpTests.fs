module Feather.ConsoleApplication.Tests.ApplicationHelp

open Expecto
open Feather.ConsoleApplication

type TestCase = {
    Description: string
    Command: string
    DefaultCommand: string option
    ExpectedOutput: string list
}

let private greetCommand: CommandDefinition = {
    Description = "Greets a person"
    Help = None
    Arguments = []
    Options = []
    Initialize = None
    Interact = None
    Execute = Execute <| fun (_, output) ->
        output.Message "greeting"
        ExitCode.Success
}

let private header = [
    "Application help test <1.0.0>"
    "============================="
    ""
]

let private applicationHelp = [
    yield! header

    "Usage:"
    "    command [options] [--] [arguments]  "
    ""
    "Options:"
    "    -h, --help            Display this help message                         "
    "    -q, --quiet           Do not output any message                         "
    "    -V, --version         Display this application version                  "
    "    -n, --no-interaction  Do not ask any interactive question               "
    "        --no-progress     Whether to disable all progress bars              "
    "        --no-ansi         Whether to disable all markup with ansi formatting"
    "    -v|vv|vvv, --verbose  Increase the verbosity of messages                "
    ""
    "Available commands:"
    "    about  Displays information about the current project"
    "    greet  Greets a person                               "
    "    help   Displays help for a command                   "
    "    list   Lists commands                                "
    ""
    ""
]

let provideApplicationHelp = seq {
    {
        Description = "should show application help when --help given without command"
        Command = "--help"
        DefaultCommand = None
        ExpectedOutput = applicationHelp
    }

    {
        Description = "should show application help when -h given without command"
        Command = "-h"
        DefaultCommand = None
        ExpectedOutput = applicationHelp
    }

    {
        Description = "should show application help when --help given with other options only"
        Command = "--no-interaction --help"
        DefaultCommand = None
        ExpectedOutput = applicationHelp
    }

    {
        Description = "should show application help when --help given and custom default command configured"
        Command = "--help"
        DefaultCommand = Some "greet"
        ExpectedOutput = applicationHelp
    }

    {
        Description = "should run default command when no args given and custom default command configured"
        Command = ""
        DefaultCommand = Some "greet"
        ExpectedOutput = [
            yield! header

            "greeting"
            ""
        ]
    }

    {
        Description = "should run default command when only options without help given and custom default command configured"
        Command = "--no-interaction"
        DefaultCommand = Some "greet"
        ExpectedOutput = [
            yield! header

            "greeting"
            ""
        ]
    }

    {
        Description = "should show name and version when --version given"
        Command = "--version"
        DefaultCommand = None
        ExpectedOutput = [
            "Application help test <1.0.0>"
            ""
        ]
    }
}

[<Tests>]
let applicationHelpTests =
    testList "ConsoleApplication - application help" [
        yield!
            provideApplicationHelp
            |> Seq.map (fun { Description = desc; Command = command; DefaultCommand = customDefault; ExpectedOutput = expected } ->
                testCase desc <| fun _ ->
                    use buffer = new Feather.ConsoleStyle.Output.BufferOutput(Feather.ConsoleStyle.Verbosity.Normal)
                    let console = Feather.ConsoleStyle.ConsoleStyle(buffer)
                    let args =
                        match command with
                        | "" -> [||]
                        | command -> command.Split " "
                    let application =
                        match customDefault with
                        | Some defaultCommandName ->
                            consoleApplication {
                                title "Feather.ConsoleApplication.Test"
                                name "Application help test"
                                version "1.0.0"
                                info ApplicationInfo.NameAndVersion
                                command "greet" greetCommand
                                defaultCommand defaultCommandName
                                useOutput console
                            }
                        | None ->
                            consoleApplication {
                                title "Feather.ConsoleApplication.Test"
                                name "Application help test"
                                version "1.0.0"
                                info ApplicationInfo.NameAndVersion
                                command "greet" greetCommand
                                useOutput console
                            }

                    let result = application |> runResult args

                    Expect.equal result (Ok ExitCode.Success) $"Command \"{command}\" should be successful"
                    let output =
                        (buffer.Fetch() |> console.RemoveMarkup).Split "\n"
                        |> List.ofArray
                    expected
                    |> List.iteri (fun i line ->
                        Expect.equal output.[i] line $"Line {i + 1} should match."
                    )
            )
    ]
