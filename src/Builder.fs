namespace Feather.ConsoleApplication

open Feather.ErrorHandling
open Feather.ErrorHandling.Result.Operators

[<RequireQualifiedAccess>]
type ApplicationInfo =
    | Hidden
    | MainTitle
    | NameAndVersion
    | OnlyNameAndVersion
    | Interactive
    | All

type internal DefinitionParts = {
    Meta: ApplicationMeta
    ApplicationInfo: ApplicationInfo
    ApplicationOptions: OptionsDefinitions
    Output: Output
    Ask: string -> string
    Commands: Commands
    DefaultCommand: CommandName
    OptionDecorationLevel: OptionDecorationLevel
    Completion: CompletionConfig option
}

[<RequireQualifiedAccess>]
module internal DefinitionParts =
    let defaults =
        let output = Output.defaults
        {
            Meta = {
                Name = ApplicationName (Name "Console Application")
                Version = None
                Title = None
                Description = None
                GitRepository = None
                GitBranch = None
                GitCommit = None
                CreatedAt = None
                Meta = []
            }
            ApplicationInfo = ApplicationInfo.Hidden
            ApplicationOptions = Commands.applicationOptions
            Output = output
            Ask = output.Ask
            Commands = Map.empty
            DefaultCommand = CommandName (Name CommandNames.List)
            OptionDecorationLevel = Minimal
            Completion = None
        }

    let output { Output = output } = output

type Definition = private Definition of Result<DefinitionParts, ConsoleApplicationError>
type ConsoleApplication = internal ConsoleApplication of Result<DefinitionParts, ConsoleApplicationError>

[<RequireQualifiedAccess>]
module internal ConsoleApplication =
    let iter f (ConsoleApplication application) =
        application
        |> Result.iter f

    let map f (ConsoleApplication application) =
        ConsoleApplication (application <!> f)

    let output (ConsoleApplication appliction) =
        appliction
        |> Result.toOption
        |> Option.map DefinitionParts.output

type ConsoleApplicationBuilder<'Application> internal (buildApplication: Definition -> 'Application) =
    let (>>=) (Definition definition) f =
        Definition (definition >>= f)

    let (<!>) state f =
        state >>= (f >> Ok)

    let (>>*) (Definition state as definition) f: Definition =
        match state with
        | Ok parts ->
            f parts
            definition
        | _ -> definition

    member _.Yield (_): Definition =
        DefinitionParts.defaults
        |> Ok
        |> Definition

    member _.Run (state) =
        buildApplication state

    [<CustomOperation("name")>]
    member _.Name(state, name): Definition =
        state >>= fun parts ->
            result {
                let! name =
                    name
                    |> ApplicationName.create <@> ConsoleApplicationError.ApplicationNameError

                return { parts with Meta = { parts.Meta with Name = name }}
            }

    [<CustomOperation("version")>]
    member _.Version(state, version): Definition =
        state <!> fun parts -> { parts with Meta = { parts.Meta with Version = Some (ApplicationVersion version) }}

    [<CustomOperation("title")>]
    member _.Title(state, title): Definition =
        state <!> fun parts -> { parts with Meta = { parts.Meta with Title = Some (ApplicationTitle title) }}

    [<CustomOperation("description")>]
    member _.Description(state, description): Definition =
        state <!> fun parts -> { parts with Meta = { parts.Meta with Description = Some description }}

    /// Add a single line for an `about` command
    [<CustomOperation("meta")>]
    member _.Meta(state, (meta, value)): Definition =
        state <!> fun parts -> { parts with Meta = { parts.Meta with Meta = [ meta; value ] :: parts.Meta.Meta }}

    /// Add multiple meta lines for an `about` command
    [<CustomOperation("meta")>]
    member _.Meta(state, meta: (string * string) list): Definition =
        state <!> fun parts -> { parts with Meta = { parts.Meta with Meta = (meta |> List.map (fun (meta, value) -> [ meta; value ]) |> List.rev) @ parts.Meta.Meta }}

    [<CustomOperation("git")>]
    member _.Git(state, (repository, branch, commit)): Definition =
        state <!> fun parts ->
            { parts with
                Meta = {
                    parts.Meta with
                        GitRepository = repository
                        GitBranch = branch
                        GitCommit = commit
                }
            }

    [<CustomOperation("git")>]
    member this.Git(state, repository, branch, commit): Definition = this.Git(state, (Some repository, Some branch, Some commit))

    [<CustomOperation("gitRepository")>]
    member _.GitRepository(state, repository): Definition =
        state <!> fun parts -> { parts with Meta = { parts.Meta with GitRepository = Some repository }}

    [<CustomOperation("gitBranch")>]
    member _.GitBranch(state, branch): Definition =
        state <!> fun parts -> { parts with Meta = { parts.Meta with GitBranch = Some branch }}

    [<CustomOperation("gitCommit")>]
    member _.GitCommit(state, commit): Definition =
        state <!> fun parts -> { parts with Meta = { parts.Meta with GitCommit = Some commit }}

    [<CustomOperation("info")>]
    member _.Info(state, applicationInfo): Definition =
        state <!> fun parts -> { parts with ApplicationInfo = applicationInfo }

    [<CustomOperation("defaultCommand")>]
    member _.DefaultCommand(state, defaultCommand): Definition =
        state >>= fun parts ->
            result {
                let! commandName =
                    defaultCommand
                    |> CommandName.create <@> ConsoleApplicationError.CommandNameError

                return { parts with DefaultCommand = commandName }
            }

    /// <summary>
    /// When options are shown, this `decorationLevel` is used to determine, how much information should be shown.
    /// <para>Minimal is just: [options]</para>
    /// <para>Complete is: [a|optionA OPTIONA] [b|optionB OPTIONB] ...</para>
    /// </summary>
    [<CustomOperation("showOptions")>]
    member _.ShowOptions(state, decorationLevel): Definition =
        state <!> fun parts -> { parts with OptionDecorationLevel = decorationLevel }

    [<CustomOperation("command")>]
    member _.Command(state, name, command): Definition =
        state >>= fun parts ->
            result {
                let! commandName =
                    name
                    |> CommandName.create <@> ConsoleApplicationError.CommandNameError

                let! command =
                    command
                    |> CommandDefinition.validate <@> ConsoleApplicationError.CommandDefinitionError

                return { parts with Commands = parts.Commands.Add(commandName, command) }
            }

    [<CustomOperation("useOutput")>]
    member _.UseOutput(state, output): Definition =
        state <!> fun parts -> { parts with Output = output; Ask = output.Ask }

    [<CustomOperation("useAsk")>]
    member _.UseAsk(state, ask): Definition =
        state <!> fun parts -> { parts with Ask = ask }

    [<CustomOperation("updateOutput")>]
    member _.UpdateOutput(state, update: Output -> Output): Definition =
        state <!> fun parts ->
            let output = parts.Output |> update
            { parts with Output = output; Ask = output.Ask }

    /// This will override a style in the Output (ConsoleStyle), and even a custom tags, which are defined there.
    [<CustomOperation("withStyle")>]
    member _.WithStyle(state, style): Definition =
        state >>* fun parts -> parts.Output.ChangeStyle style

    [<CustomOperation("withCustomTags")>]
    member _.WithCustomTags(state, customTags): Definition =
        state >>* fun parts -> parts.Output.UpdateStyle (fun style ->
            { style with CustomTags = style.CustomTags @ customTags }
        )

    [<CustomOperation("withCustomTags")>]
    member _.WithCustomTags(state, customTags): Definition =
        state >>= fun parts ->
            result {
                let! customTags =
                    customTags
                    |> Validation.ofResults
                    <@> (CommandDefinitionError.InvalidCustomTags >> ConsoleApplicationError.CommandDefinitionError)

                parts.Output.UpdateStyle (fun style ->
                    { style with CustomTags = style.CustomTags @ customTags }
                )

                return parts
            }

    [<CustomOperation("enableCompletion")>]
    member _.EnableCompletion(state): Definition =
        state <!> fun parts -> { parts with Completion = Some CompletionConfig.defaults }

    [<CustomOperation("enableCompletion")>]
    member _.EnableCompletion(state, config: CompletionConfig): Definition =
        state <!> fun parts -> { parts with Completion = Some config }

[<RequireQualifiedAccess>]
module internal ConsoleApplicationBuilder =
    let buildApplication showHelpForCommand (Definition definition) =
        definition
        |> Result.bind (fun parts -> result {
            let showHelpForCommand = showHelpForCommand parts.Output parts.OptionDecorationLevel parts.ApplicationOptions
            let aboutCommand = Commands.aboutCommand parts.Meta

            let commands =
                [
                    // dummy commands are here just as placeholder to be shown in the list, etc.
                    yield (CommandName (Name CommandNames.List), Commands.listCommandDummy)
                    yield (CommandName (Name CommandNames.Help), Commands.helpCommandDummy)
                    yield (CommandName (Name CommandNames.About), aboutCommand)

                    yield! parts.Commands |> Map.toList
                ]
                |> Map.ofList

            let! dispatchCommands, discoveryCommands =
                match parts.Completion with
                | None -> Ok (commands, commands)
                | Some config ->
                    let userCommandNames = parts.Commands |> Map.toList |> List.map (fst >> CommandName.value)
                    commands |> Completion.Setup.wire userCommandNames parts.ApplicationOptions config

            let finalCommands =
                dispatchCommands
                |> Commands.add CommandNames.List (Commands.listCommand parts.ApplicationOptions discoveryCommands)
                |> Commands.add CommandNames.Help (Commands.helpCommand showHelpForCommand discoveryCommands)
                |> Commands.add CommandNames.About aboutCommand

            return { parts with Commands = finalCommands }
        })
        |> ConsoleApplication
