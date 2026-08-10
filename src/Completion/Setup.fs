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

    let private scriptCommand (config: CompletionConfig) (executableName: string) =
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
                let usage () =
                    output.Message (sprintf "Usage: %s <%s>" config.CommandName (Shell.names |> String.concat "|"))
                    ExitCode.Error

                match input with
                | Input.Argument.OptionalValue "shell" shell ->
                    match shell |> Shell.parse with
                    | Some shell ->
                        output.Message (ShellScript.generate shell executableName config.CommandName HiddenResolverCommandName)
                        ExitCode.Success
                    | None -> usage ()
                | _ -> usage ()
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

    /// Fails when the configured name is not a usable command name or when it is already taken.
    let validateConfig (userCommandNames: string list) (config: CompletionConfig) : Result<unit, ConsoleApplicationError> =
        let ensureAvailable name =
            if userCommandNames |> List.contains name then Error (CommandNameError.Reserved name)
            else name |> CommandName.create <!> ignore

        result {
            do! ensureAvailable config.CommandName
            do! ensureAvailable HiddenResolverCommandName

            do!
                if config.CommandName = HiddenResolverCommandName
                then Error (CommandNameError.Reserved HiddenResolverCommandName)
                else Ok ()
        }
        <@> ConsoleApplicationError.CommandNameError

    /// Returns the commands available for dispatch (including the hidden resolver) and the ones shown by list/help.
    let wire (config: CompletionConfig) (applicationOptions: OptionsDefinitions) (commands: Commands) =
        result {
            let executableName =
                config.ExecutableName
                |> Option.defaultValue AppDomain.CurrentDomain.FriendlyName

            let! script = scriptCommand config executableName
            let withScript = commands |> Commands.add config.CommandName script

            // The resolver suggests the script command too, but never itself.
            let! resolver = resolverCommand withScript applicationOptions

            let dispatch = withScript |> Commands.add HiddenResolverCommandName resolver
            let discovery = if config.IsVisible then withScript else commands

            return dispatch, discovery
        }
