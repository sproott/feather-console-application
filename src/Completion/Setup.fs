namespace Feather.ConsoleApplication.Completion

open Feather.ConsoleApplication
open Feather.ErrorHandling

/// Adds the two commands completion needs to the application: the user-facing one printing the
/// shell script, and the hidden one the script calls back for suggestions.
[<RequireQualifiedAccess>]
module internal Setup =
    open System
    open Feather.ErrorHandling.Result.Operators

    [<Literal>]
    let HiddenResolverCommandName = "__completion"

    type private ValidatedConfig = {
        CommandName: CommandName
        ResolverName: CommandName
        IsVisible: bool
        ExecutableName: string
    }

    let private validateConfig (userCommandNames: string list) (config: CompletionConfig): Result<ValidatedConfig, ConsoleApplicationError> =
        let ensureAvailable name =
            if userCommandNames |> List.contains name then Error (CommandNameError.Reserved name)
            else name |> CommandName.create

        result {
            do!
                if config.CommandName = HiddenResolverCommandName
                then Error (CommandNameError.Reserved HiddenResolverCommandName)
                else Ok ()

            let! commandName = ensureAvailable config.CommandName
            let! resolverName = ensureAvailable HiddenResolverCommandName

            return {
                CommandName = commandName
                ResolverName = resolverName
                IsVisible = config.IsVisible
                ExecutableName = config.ExecutableName |> Option.defaultValue AppDomain.CurrentDomain.FriendlyName
            }
        }
        <@> ConsoleApplicationError.CommandNameError

    let private scriptCommand (config: ValidatedConfig) =
        let commandName = config.CommandName |> CommandName.value

        CommandDefinition.validateAsSystemCommand {
            Description = "Generate shell completion scripts"
            Help = None
            Arguments = [
                Argument.optional "shell" (sprintf "Shell name (%s) for script generation" (Shell.names |> String.concat "|")) None
                |> Argument.suggest (Suggest.values Shell.names)
            ]
            Options = []
            Initialize = None
            Interact = None
            Execute = Execute <| fun (input, output) ->
                match input with
                | Input.Argument.OptionalValue "shell" shellName ->
                    match shellName |> Shell.create with
                    | Ok shell ->
                        output.Message (ShellScript.generate shell config.ExecutableName commandName HiddenResolverCommandName)
                        ExitCode.Success
                    | Error error ->
                        output.Message (ShellError.format error)
                        ExitCode.Error
                | _ ->
                    output.Message (sprintf "Usage: %s <%s>" commandName (Shell.names |> String.concat "|"))
                    ExitCode.Error
        }
        <@> ConsoleApplicationError.CommandDefinitionError

    /// The scripts of the shells showing descriptions split the line at the tab, the others cut it there.
    let private formatSuggestion (suggestion: CompletionSuggestion) =
        match suggestion |> CompletionSuggestion.description with
        | Some description -> sprintf "%s\t%s" suggestion.Value description
        | None -> suggestion.Value

    let private resolverCommand (commands: Commands) (applicationOptions: OptionsDefinitions) =
        CommandDefinition.validateAsSystemCommand {
            Description = "Resolve shell completion suggestions"
            Help = None
            Arguments = [
                Argument.required "index" "Index of the word being completed"
                Argument.requiredArray "words" "Words of the command line, as the shell tokenized them"
            ]
            Options = []
            Initialize = None
            Interact = None
            // Everything this command writes is read back as suggestions, so a malformed call stays silent.
            Execute = ExecuteAsync <| fun (input, output) ->
                async {
                    match input with
                    | Input.Argument.Value "index" index & Input.Argument.ListValue "words" words ->
                        match index |> String.toInt with
                        | Some index ->
                            let! suggestions =
                                { Words = words; Index = index }
                                |> Suggestions.resolve commands applicationOptions

                            suggestions |> List.iter (formatSuggestion >> output.Message)

                            return ExitCode.Success
                        | None -> return ExitCode.Error
                    | _ -> return ExitCode.Error
                }
        }
        <@> ConsoleApplicationError.CommandDefinitionError

    /// Returns the commands available for dispatch (including the hidden resolver) and the ones shown by list/help.
    let wire
        (userCommandNames: string list)
        (applicationOptions: OptionsDefinitions)
        (config: CompletionConfig)
        (commands: Commands)
        : Result<Commands * Commands, ConsoleApplicationError> =
        result {
            let! config = validateConfig userCommandNames config

            let! script = scriptCommand config
            let withScript = commands |> Map.add config.CommandName script

            // The resolver suggests the script command too, but never itself.
            let! resolver = resolverCommand withScript applicationOptions

            let dispatch = withScript |> Map.add config.ResolverName resolver
            let discovery = if config.IsVisible then withScript else commands

            return dispatch, discovery
        }
