module Feather.ConsoleApplication.Tests.DefaultCommands

open System
open Expecto
open Feather.ConsoleApplication

type LineMatch =
    | Exact of string
    | Contains of string
    | ContainsAll of string list

type TestCase = {
    Description: string
    Command: string
    ExpectedOutput: LineMatch list
}

let provideDefaultCommands = seq {
    let header = [
        Exact "Default command test <1.0.0>"
        Exact "============================"
        Exact ""
    ]

    {
        Description = "Help command"
        Command = "help"
        ExpectedOutput =
            [
                yield! header

                Exact "Description:"
                Exact "    Displays help for a command"
                Exact ""
                Exact "Usage:"
                Exact "    help [options] [--] [<command_name>]"
                Exact ""
                Exact "Arguments:"
                Exact "    command_name  The command name [default: \"help\"]"
                Exact ""
                Exact "Options:"
                Exact "    -h, --help            Display this help message                         "
                Exact "    -q, --quiet           Do not output any message                         "
                Exact "    -V, --version         Display this application version                  "
                Exact "    -n, --no-interaction  Do not ask any interactive question               "
                Exact "        --no-progress     Whether to disable all progress bars              "
                Exact "        --no-ansi         Whether to disable all markup with ansi formatting"
                Exact "    -v|vv|vvv, --verbose  Increase the verbosity of messages                "
                Exact ""
                Exact "Help:"
                Exact "    The help command displays help for a given command:"
                Exact ""
                Contains "help list"
                Exact ""
                Exact "    To display list of available commands, please use list command."
                Exact ""
                Exact ""
            ]
    }

    {
        Description = "List command"
        Command = "list"
        ExpectedOutput =
            [
                yield! header

                Exact "Usage:"
                Exact "    command [options] [--] [arguments]  "
                Exact ""
                Exact "Options:"
                Exact "    -h, --help            Display this help message                         "
                Exact "    -q, --quiet           Do not output any message                         "
                Exact "    -V, --version         Display this application version                  "
                Exact "    -n, --no-interaction  Do not ask any interactive question               "
                Exact "        --no-progress     Whether to disable all progress bars              "
                Exact "        --no-ansi         Whether to disable all markup with ansi formatting"
                Exact "    -v|vv|vvv, --verbose  Increase the verbosity of messages                "
                Exact ""
                Exact "Available commands:"
                Exact "    about  Displays information about the current project"
                Exact "    help   Displays help for a command                   "
                Exact "    list   Lists commands                                "
                Exact ""
                Exact ""
            ]
    }

    {
        Description = "About command - help"
        Command = "about -h"
        ExpectedOutput =
            [
                yield! header

                Exact "Description:"
                Exact "    Displays information about the current project"
                Exact ""
                Exact "Usage:"
                Exact "    about [options] "
                Exact ""
                Exact "Options:"
                Exact "    -h, --help            Display this help message                         "
                Exact "    -q, --quiet           Do not output any message                         "
                Exact "    -V, --version         Display this application version                  "
                Exact "    -n, --no-interaction  Do not ask any interactive question               "
                Exact "        --no-progress     Whether to disable all progress bars              "
                Exact "        --no-ansi         Whether to disable all markup with ansi formatting"
                Exact "    -v|vv|vvv, --verbose  Increase the verbosity of messages                "
                Exact ""
                Exact "Help:"
                Exact "    The about command displays information about the current project:"
                Exact ""
                Contains "about about"
                Exact ""
                Exact "    There are multiple sections shown in the output:"
                Exact "      - current project details/meta information"
                Exact "      - environment"
                Exact "      - console application library"
                Exact ""
                Exact ""
            ]
    }

    {
        Description = "About command"
        Command = "about"
        ExpectedOutput =
            let version = AssemblyVersionInformation.AssemblyVersion
            let createdAt = AssemblyVersionInformation.AssemblyMetadata_createdAt.[0 .. "yyyy-mm-dd".Length - 1]
            let gitCommit = AssemblyVersionInformation.AssemblyMetadata_gitcommit

            [
                yield! header

                Contains ""
                ContainsAll [ "Default command test"; ".NET Core"; "Git Branch"; "Feather.ConsoleApplication" ]
                ContainsAll [ "1.0.0"; "<branch>"; version; createdAt ]
                Contains ""
                Exact ""
                Contains "----------------------------"
                Contains " Application"
                Contains " ---------------------"
                Contains " Name                         Default command test"
                Contains " Version                      1.0.0"
                Contains " Description                  About command"
                Contains " Environment                  Test"
                Contains " command                      about"
                Contains " args"
                Contains " ---------------------"
                Contains " Environment"
                Contains " ---------------------"
                Contains " .NET Core "
                Contains " Command Line "
                Contains " Current Directory "
                Contains " Machine Name "
                Contains " OS Version "
                Contains " Processor Count "
                Contains " ---------------------"
                Contains " Git"
                Contains " ---------------------"
                Contains " Branch                       <branch>"
                Contains " Commit                       <commit>"
                Contains " ---------------------"
                Contains " Feather.ConsoleApplication"
                Contains " ---------------------"
                Contains $" Version                      {version}"
                Contains $" Commit                       {gitCommit}"
                Contains $" Released                     {createdAt}"
                Contains "----------------------------"
                Exact ""
                Exact ""
            ]
    }
}

[<Tests>]
let defaultCommandsTests =
    testList "ConsoleApplication - default commands" [
        yield!
            provideDefaultCommands
            |> Seq.map (fun { Description = desc; Command = command; ExpectedOutput = expected } ->
                testCase $"command {desc}" <| fun _ ->
                    use buffer = new Feather.ConsoleStyle.Output.BufferOutput(Feather.ConsoleStyle.Verbosity.Normal)
                    let console = Feather.ConsoleStyle.ConsoleStyle(buffer)

                    let cmd, args =
                        match command.Split(" ") |> List.ofArray with
                        | [] -> "", ""
                        | cmd :: args -> cmd, (args |> String.concat " ")

                    let app =
                        consoleApplication {
                            title "Feather.ConsoleApplication.Test"
                            name "Default command test"
                            description desc
                            version "1.0.0"
                            info ApplicationInfo.NameAndVersion

                            git (
                                None,
                                Some "<branch>",
                                Some "<commit>"
                            )

                            meta ("Environment", "Test")
                            meta [
                                "command", cmd
                                "args", args
                            ]

                            useOutput console
                        }
                        |> runResult (command.Split " ")

                    Expect.equal app (Ok ExitCode.Success) $"Command {command} should be always successful"

                    let output =
                        (buffer.Fetch() |> console.RemoveMarkup).Split "\n"
                        |> List.ofArray

                    expected
                    |> List.iteri (fun i expectedLine ->
                        match expectedLine with
                        | Exact expected ->
                            Expect.stringHasLength output[i] expected.Length $"Output line {i + 1} should have correct length ({expected.Length} but has {output[i].Length})."
                            Expect.equal output[i] expected $"Line {i + 1} should match."
                        | Contains substring ->
                            Expect.stringContains output[i] substring $"Output line {i + 1} should contain '{substring}'."
                        | ContainsAll substrings ->
                            substrings
                            |> List.iter (fun substring ->
                                Expect.stringContains output[i] substring $"Output line {i + 1} should contain '{substring}'."
                            )
                    )
            )
    ]
