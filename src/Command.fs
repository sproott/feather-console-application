namespace Feather.ConsoleApplication

open System
open Feather.ConsoleStyle
open Feather.ErrorHandling

type InteractiveInput = {
    Input: Input
    Ask: string -> string
}

[<RequireQualifiedAccess>]
module internal InteractiveInput =
    let input ask input = {
        Input = input
        Ask = ask
    }

type IO = Input * Output
type InteractiveIO = InteractiveInput * Output

[<RequireQualifiedAccess>]
module internal IO =
    let toInteractive ask: IO -> InteractiveIO =
        fun (input, output) ->
            (InteractiveInput.input ask input, output)

// Command
// ***********************

type Initialize = IO -> IO
type Interact = InteractiveInput * Output -> IO

[<RequireQualifiedAccess>]
module internal Interact =
    let id: Interact =
        fun ({ Input = input }, output) -> input, output

    let map ask: Interact -> IO -> IO =
        fun interact (input, output) ->
            match input with
            | Input.Option.Has OptionNames.NoInteraction _ -> (input, output)
            | _ -> (input, output) |> IO.toInteractive ask |> interact

type Execute =
    | Execute of (IO -> ExitCode)
    | ExecuteResult of (IO -> Result<ExitCode, ConsoleApplicationError>)
    | ExecuteAsync of (IO -> Async<ExitCode>)
    | ExecuteAsyncResult of (IO -> Async<Result<ExitCode, ConsoleApplicationError>>)

[<RequireQualifiedAccess>]
module internal Execute =
    let run io = function
        | Execute e -> e io |> AsyncResult.ofSuccess
        | ExecuteResult e -> e io |> AsyncResult.ofResult
        | ExecuteAsync e -> e io |> AsyncResult.ofAsyncCatch ConsoleApplicationError.ConsoleApplicationException
        | ExecuteAsyncResult e -> e io

type CommandDefinition = {
    Description: string
    Help: string option
    Arguments: Result<RawArgumentDefinition, ArgumentDefinitionError> list
    Options: Result<RawOptionDefinition, OptionDefinitionError> list
    Initialize: Initialize option
    Interact: Interact option
    Execute: Execute
}

type Command = private {
    Description: string
    Help: string option
    Arguments: ArgumentsDefinitions
    Options: OptionsDefinitions
    Initialize: Initialize option
    Interact: Interact option
    Execute: Execute
}

[<RequireQualifiedAccess>]
module internal CommandDefinition =
    open Feather.ErrorHandling.Result.Operators

    let validate (definition: CommandDefinition): Result<Command, CommandDefinitionError> =
        result {
            let! arguments =
                definition.Arguments
                |> Validation.ofResults
                >>= ArgumentsDefinitions.validate <@> CommandDefinitionError.ArgumentDefinitionError

            let! options =
                definition.Options
                |> Validation.ofResults
                >>= OptionsDefinitions.validate <@> CommandDefinitionError.OptionDefinitionError

            return {
                Description = definition.Description
                Help = definition.Help
                Arguments = arguments
                Options = options
                Initialize = definition.Initialize
                Interact = definition.Interact
                Execute = definition.Execute
            }
        }

[<RequireQualifiedAccess>]
module internal Command =
    let description ({ Description = description }: Command) = description
    let definitions ({ Options = options; Arguments = arguments }: Command) = options, arguments

type internal Commands = Map<CommandName, Command>

type internal FindResult =
    | ExactlyOne of CommandName * Command
    | MoreThanOne of CommandName * CommandName list
    | NoCommand of CommandName

[<RequireQualifiedAccess>]
module internal Commands =
    open Feather.ErrorHandling.Result.Operators

    let private namesByPattern pattern (commands: Commands): CommandName list =
        commands
        |> Map.toList
        |> List.map fst
        |> List.choose (CommandName.isMatchingPattern pattern)

    let find name (commands: Commands) =
        match commands |> Map.tryFind name with
        | Some command -> ExactlyOne (name, command)
        | _ ->
            let escapeForRegex (name: string) =
                name.Replace(".", @"\.")

            let regexToMatchAllToTheNamespaceSeparator =
                sprintf "[^%s]*?" CommandName.NamespaceSeparator

            let partialNamePattern =
                name
                |> CommandName.splitByNamespaces
                |> List.map (escapeForRegex >> String.append regexToMatchAllToTheNamespaceSeparator)
                |> String.concat CommandName.NamespaceSeparator
                |> sprintf "^%s$"

            match commands |> namesByPattern partialNamePattern with
            | [] -> NoCommand name
            | [ commandName ] -> ExactlyOne (commandName, commands.[commandName])
            | commandNames -> MoreThanOne (name, commandNames)

    let applicationOptions: OptionsDefinitions =
        [
            OptionsDefinitions.help
            OptionsDefinitions.quiet
            OptionsDefinitions.version
            OptionsDefinitions.noInteraction
            OptionsDefinitions.noProgress
            OptionsDefinitions.noAnsi
            OptionsDefinitions.verbose
        ]

    let format (commands: Commands) =
        commands
        |> Map.toList
        |> List.map (fun (name, command) -> [
            sprintf "<c:green>%s</c>" (name |> CommandName.value)
            command |> Command.description
        ])

    let showAvailable (output: Output) commands =
        commands
        |> format
        |> output.GroupedOptions CommandName.NamespaceSeparator "Available commands:"

    let private showUsage (output: Output) =
        output.SimpleOptions "Usage:" [
            [ "command [options] [--] [arguments]"; "" ]
        ]

    let private showOptions (output: Output) applicationOptions =
        applicationOptions
        |> OptionsDefinitions.format
        |> output.SimpleOptions "Options:"

    let showApplicationHelp (output: Output) applicationOptions commands =
        showUsage output
        showOptions output applicationOptions

        commands
        |> showAvailable output

    let add name command (commands: Commands) =
        commands.Add (CommandName (Name name), command)

    let helpCommand showHelpForCommand (commands: Commands): Command =
        CommandDefinition.validate {
            Description = "Displays help for a command"
            Help =
                Help.lines [
                    "The <c:green>{{command.name}}</c> command displays help for a given command:"
                    "        <c:green>dotnet {{command.full_name}} list</c>"
                    "    To display list of available commands, please use <c:green>list</c> command."
                ]
            Arguments = [
                Argument.optional "command_name" "The command name" (Some CommandNames.Help)
            ]
            Options = []
            Initialize = None
            Interact = None
            Execute = ExecuteResult <| fun (input, output) ->
                result {
                    let! rawCommandName =
                        input
                        |> Input.Argument.value "command_name"
                        |> CommandName.createInRuntime <@> ArgsError.CommandNameError

                    let! (commandName, command) =
                        match commands |> find rawCommandName with
                        | ExactlyOne (commandName, command) -> Ok (commandName, command)
                        | MoreThanOne (givenName, names) -> Error (ArgsError.AmbigousCommandFound (givenName, names))
                        | NoCommand unknownName -> Error (ArgsError.CommandNotFound unknownName)

                    showHelpForCommand commandName command

                    return ExitCode.Success
                }
                <@> ConsoleApplicationError.ArgsError
        }
        |> Result.orFail

    let helpCommandDummy =
        let ignore2 _ = ignore
        helpCommand ignore2 Map.empty

    let aboutCommand (meta: ApplicationMeta) =
        CommandDefinition.validate {
            Description = "Displays information about the current project"
            Help =
                Help.lines [
                    "The <c:green>{{command.name}}</c> command displays information about the current project:"
                    "        <c:green>dotnet {{command.full_name}} about</c>"

                    [
                        "There are multiple sections shown in the output:"
                        "  - <c:cyan>current project details/meta information</c>"
                        "  - <c:green>environment</c>"
                        "  - <c:orange>console application library</c>"
                    ]
                    |> List.map (sprintf "    %s")
                    |> String.concat "\n"
                ]
            Arguments = []
            Options = []
            Initialize = None
            Interact = None
            Execute = Execute <| fun (input, output) ->
                let ``---`` =
                    [ String.replicate 21 "-"; String.replicate 100 "-" ] |> List.map (sprintf "<c:gray>%s</c>")

                let sectionHead = sprintf "<c:green|u>%s</c>"
                let head = sprintf "<c:dark-yellow>%s</c>"

                let section headSeparator title lines =
                    [
                        if headSeparator then ``---``
                        [ sectionHead title ]
                        ``---``
                        yield!
                            lines
                            |> List.map (function
                                | [] -> []
                                | first :: rest -> head first :: rest
                            )
                    ]
                let firstSection = section false
                let section = section true

                let appName = meta.Name |> ApplicationName.value

                output.Tabs [
                    { Tab.parseColor "cyan" appName with Value = meta.Version |> Option.map ApplicationVersion.value }

                    { Tab.parseColor "green" ".NET Core" with Value = Some (Environment.Version |> sprintf "%A") }

                    match meta with
                    | { GitBranch = Some branch } -> { Tab.parseColor "dark-yellow" "Git Branch" with Value = Some branch }
                    | { GitCommit = Some commit } -> { Tab.parseColor "dark-yellow" "Git Commit" with Value = Some commit }
                    | _ -> ()

                    { Tab.parseColor "orange" AssemblyVersionInformation.AssemblyProduct with
                        Value = Some (
                            sprintf "%s (%s)"
                                AssemblyVersionInformation.AssemblyVersion
                                AssemblyVersionInformation.AssemblyMetadata_createdAt[ 0 .. "yyyy-mm-dd".Length - 1 ]
                        )
                    }
                ]

                output.Table [] [
                    yield! firstSection "Application" [
                        [ "Name"; appName ]

                        match meta.Version with
                        | Some (ApplicationVersion version) -> [ "Version"; version ]
                        | _ -> ()

                        match meta.Description with
                        | Some description -> [ "Description"; description ]
                        | _ -> ()

                        yield! meta.Meta |> List.rev
                    ]

                    yield! section "Environment" [
                        [ ".NET Core"; Environment.Version |> sprintf "%A" ]
                        [ "Command Line"; Environment.CommandLine ]
                        [ "Current Directory"; Environment.CurrentDirectory ]
                        [ "Machine Name"; Environment.MachineName ]
                        [ "OS Version"; Environment.OSVersion |> sprintf "%A" ]
                        [ "Processor Count"; Environment.ProcessorCount |> sprintf "%A" ]
                    ]

                    match meta with
                    | { GitRepository = Some _ } | { GitBranch = Some _ } | { GitCommit = Some _ } ->
                        yield!
                            [
                                meta.GitRepository |> Option.map (fun value -> [ "Repository"; value ])
                                meta.GitBranch |> Option.map (fun value -> [ "Branch"; value ])
                                meta.GitCommit |> Option.map (fun value -> [ "Commit"; value ])
                            ]
                            |> List.choose id
                            |> section "Git"
                    | _ -> ()

                    yield! section AssemblyVersionInformation.AssemblyProduct [
                        [ "Version"; AssemblyVersionInformation.AssemblyVersion ]
                        [ "Commit"; AssemblyVersionInformation.AssemblyMetadata_gitcommit ]
                        [ "Released"; AssemblyVersionInformation.AssemblyMetadata_createdAt[ 0 .. "yyyy-mm-dd".Length ]]
                    ]
                ]

                ExitCode.Success
        }
        |> Result.orFail

    let private byNamespace namespaceValue (commands: Commands) =
        commands
        |> Map.filter (fun name _ ->
            name |> CommandName.namespaceValue = Some namespaceValue
        )

    let listCommand applicationOptions (commands: Commands): Command =
        CommandDefinition.validate {
            Description = "Lists commands"
            Help =
                Help.lines [
                    "The <c:green>{{command.name}}</c> command lists all commands:"
                    "        <c:green>dotnet {{command.full_name}}</c>"
                    "    You can also display the commands for a specific namespace:"
                    "        <c:green>dotnet {{command.full_name}} test</c>"
                ]
            Arguments = [
                Argument.optional "namespace" "The namespace name" None
            ]
            Options = []
            Initialize = None
            Interact = None
            Execute = Execute <| fun (input, output) ->
                match input with
                | Input.Argument.OptionalValue "namespace" namespaceToList ->
                    match commands |> byNamespace namespaceToList with
                    | commandsByNamespace when commandsByNamespace |> Map.isEmpty ->
                        output.Error <| sprintf "There are no commands defined in the \"%s\" namespace.\n" namespaceToList
                        ExitCode.Error
                    | commandsByNamespace ->
                        showUsage output
                        showOptions output applicationOptions

                        commandsByNamespace
                        |> format
                        |> output.SimpleOptions "Available commands:"

                        ExitCode.Success
                | _ ->
                    showApplicationHelp output applicationOptions commands

                    ExitCode.Success
        }
        |> Result.orFail

    let listCommandDummy =
        listCommand [] Map.empty

    let exitInteractiveModeCommand =
        {
            Description = "Exit interactive mode"
            Help = None
            Arguments = []
            Options = []
            Initialize = None
            Interact = None
            Execute = Execute <| fun _ -> failwith "Exit command should not be executed."
        }
