namespace Feather.ConsoleApplication

open Feather.ErrorHandling

[<RequireQualifiedAccess>]
module internal Completion =

    [<Literal>]
    let HiddenResolverCommandName = "__completion"

    /// Tokenize a line by splitting on spaces and include only tokens up to the cursor position.
    /// Returns the tokens, the current partial token, and preceding tokens.
    let private tokenize (line: string) (cursorPosition: int) =
        let truncatedLine = line.Substring(0, System.Math.Min(cursorPosition, line.Length))
        let tokens = truncatedLine.Split(' ') |> Array.toList

        match tokens with
        | [] -> ([], "", [])
        | [ single ] -> (tokens, single, [])
        | _ ->
            let allButLast = tokens |> List.take (tokens.Length - 1)
            let currentWord = tokens |> List.last
            (tokens, currentWord, allButLast)

    /// Check if a string matches a prefix (for completion suggestions).
    let private isPrefixMatch (prefix: string) (str: string) : bool =
        str.StartsWith(prefix)

    /// Get all long option names from the options list.
    let private getLongOptionNames (options: OptionsDefinitions) : string list =
        options
        |> List.map (fun (option: Option) ->
            let optionName = option |> Option.name |> OptionName.value
            "--" + optionName)

    /// Get all short option names from the options list.
    let private getShortOptionNames (options: OptionsDefinitions) =
        options
        |> List.choose (fun (option: Option) ->
            match option |> Option.shortcut with
            | Some (OptionShortcut shortcut) ->
                shortcut.Split('|')
                |> Seq.toList
                |> List.map (fun s -> "-" + s)
                |> Some
            | None -> None
        )
        |> List.concat

    /// Check if an option definition requires a value.
    let private optionRequiresValue (option: Option) =
        match option.Value with
        | OptionValueDefinition.ValueNone -> false
        | OptionValueDefinition.ValueRequired _ -> true
        | OptionValueDefinition.ValueOptional _ -> false
        | OptionValueDefinition.ValueIsArray _ -> false
        | OptionValueDefinition.ValueRequiredArray _ -> true

    /// Find an option definition by name (long form, without --).
    let private findOptionByName (name: string) (options: OptionsDefinitions) =
        let cleanName = name.TrimStart('-')
        options
        |> List.tryFind (fun option -> option |> Option.name |> OptionName.value = cleanName)

    /// Find an option definition by shortcut.
    let private findOptionByShortcut (shortcut: string) (options: OptionsDefinitions) =
        let cleanShortcut = shortcut.TrimStart('-')
        options
        |> List.tryFind (fun option ->
            match option |> Option.shortcut with
            | Some (OptionShortcut s) -> s.Split('|') |> Array.contains cleanShortcut
            | None -> false
        )

    /// Run a suggestion provider, mapping/filtering its results into completion suggestions.
    let private runSuggestion ctx (filter: string list -> string list) suggestion : Async<CompletionSuggestion list> =
        async {
            match suggestion with
            | Some (Suggest.SuggestSync f) -> return f ctx |> filter |> List.map CompletionSuggestion
            | Some (Suggest.SuggestAsync f) -> return! f ctx |> Async.map (filter >> List.map CompletionSuggestion)
            | None -> return []
        }

    /// Suggest command names matching a prefix (empty prefix = all).
    let private commandNameSuggestions prefix (commands: Commands) =
        commands
        |> Map.toList
        |> List.map (fst >> CommandName.value)
        |> List.filter (isPrefixMatch prefix)
        |> List.map CompletionSuggestion

    /// Count positional (non-option) tokens after the executable and command name.
    let private positionalArgCount (precedingTokens: string list) =
        precedingTokens
        |> List.skip (min 2 (List.length precedingTokens))
        |> List.filter (fun t -> not (t.StartsWith("-")))
        |> List.length

    /// Suggest values for a positional argument at a given index.
    let private suggestArgumentValue (command: Command) (precedingTokens: string list) (currentWord: string) (argIndex: int) : Async<CompletionSuggestion list> =
        match command.Arguments |> List.tryItem argIndex with
        | Some argDef ->
            argDef
            |> Argument.suggestion
            |> runSuggestion { CurrentWord = currentWord; PrecedingWords = precedingTokens } (List.filter (isPrefixMatch currentWord))
        | None -> async { return [] }

    /// Resolve completion suggestions based on context.
    let resolve
        (commands: Commands)
        (applicationOptions: OptionsDefinitions)
        (completionInput: CompletionInput)
        : Async<CompletionSuggestion list> =
        async {
            let (allTokens, currentWord, precedingTokens) = tokenize completionInput.Line completionInput.CursorPosition

            match allTokens with
            // Nothing or only executable: suggest command names.
            | []
            | [ _ ] ->
                return commands |> commandNameSuggestions currentWord

            | _ :: cmdNameStr :: _ ->
                match commands |> Map.tryFind (CommandName (Name cmdNameStr)) with
                // Unknown command: suggest partial command-name matches.
                | None ->
                    return commands |> commandNameSuggestions cmdNameStr

                | Some command ->
                    let allOptions = applicationOptions @ (command |> Command.definitions |> fst)
                    let filter = List.filter (isPrefixMatch currentWord)

                    if currentWord.StartsWith("--") then
                        match findOptionByName currentWord allOptions with
                        // Complete long option needing a value: suggest its values.
                        | Some option when optionRequiresValue option ->
                            return! option |> Option.suggestion |> runSuggestion { CurrentWord = ""; PrecedingWords = precedingTokens } id
                        // Otherwise suggest long option names.
                        | _ ->
                            return allOptions |> getLongOptionNames |> filter |> List.map CompletionSuggestion

                    elif currentWord.StartsWith("-") then
                        // Completing short option (single dash).
                        return allOptions |> getShortOptionNames |> filter |> List.map CompletionSuggestion

                    else
                        // Resolve option referred by previous token, if any.
                        let previousOption =
                            match precedingTokens |> List.tryLast with
                            | Some prev when prev.StartsWith("--") -> findOptionByName prev allOptions
                            | Some prev when prev.StartsWith("-") -> findOptionByShortcut prev allOptions
                            | _ -> None

                        match previousOption with
                        // After option needing value: suggest option values.
                        | Some option when optionRequiresValue option ->
                            return! option |> Option.suggestion |> runSuggestion { CurrentWord = currentWord; PrecedingWords = precedingTokens } filter
                        // Otherwise complete positional argument.
                        | _ ->
                            return! suggestArgumentValue command precedingTokens currentWord (positionalArgCount precedingTokens)
        }

    [<RequireQualifiedAccess>]
    module ShellScript =
        let generate (shell: string) (appName: string) (scriptCommandName: string) (resolverCommandName: string) =
            let scriptInvocation = sprintf "%s %s" appName scriptCommandName
            let resolverInvocation = sprintf "%s %s" appName resolverCommandName

            let bashScript =
                sprintf "# Bash completion script for %s\n# Source this file in your .bashrc or .bash_profile:\n# source <(%s bash)\n\n_%s_completion() {\n    local IFS=$'\\n'\n    local suggestions\n    suggestions=$(%s \"$COMP_LINE\" \"$COMP_POINT\" 2>/dev/null)\n    COMPREPLY=($(compgen -W \"$suggestions\" -- \"${COMP_WORDS[COMP_CWORD]}\"))\n}\ncomplete -F _%s_completion %s" appName scriptInvocation appName resolverInvocation appName appName

            let zshScript =
                sprintf "#compdef %s\n# Zsh completion script for %s\n# Source this file in your .zshrc:\n# source <(%s zsh)\n\n_%s_completion() {\n    local suggestions\n    suggestions=($(echo \"$words\" | %s \"$words\" \"$CURSOR\" 2>/dev/null))\n    compadd -a suggestions\n}\ncompdef _%s_completion %s" appName appName scriptInvocation appName resolverInvocation appName appName

            let fishScript =
                sprintf "# Fish completion script for %s\n# Source this file in your config.fish:\n# source (%s fish | psub)\n\nfunction __%s_completion\n    set -l line (commandline -cp)\n    set -l cursor (commandline -C)\n    %s \"$line\" $cursor 2>/dev/null\nend\ncomplete -c %s -f -a '(__%s_completion)'" appName scriptInvocation appName resolverInvocation appName appName

            match shell.ToLowerInvariant() with
            | "bash" -> bashScript
            | "zsh" -> zshScript
            | "fish" -> fishScript
            | unknown -> sprintf "Unsupported shell: %s. Supported: bash, zsh, fish" unknown

    let scriptCommand (config: CompletionConfig) (appName: string) : Command =
        CommandDefinition.validateWithSuppressedBanner {
            Description = "Generate shell completion scripts"
            Help = None
            Arguments = [
                Argument.optional "shell" "Shell name (bash|zsh|fish) for script generation" None
            ]
            Options = []
            Initialize = None
            Interact = None
            Execute = ExecuteAsync <| fun (input, output) ->
                async {
                    match input with
                    | Input.Argument.OptionalValue "shell" shell ->
                        let script = ShellScript.generate shell appName config.CommandName HiddenResolverCommandName
                        output.Message script
                        return ExitCode.Success
                    | _ ->
                        output.Message (sprintf "Usage: %s <shell>" config.CommandName)
                        return ExitCode.Error
                }
        }
        |> Result.orFail

    let resolverCommand (commands: Commands) (applicationOptions: OptionsDefinitions) : Command =
        CommandDefinition.validateWithSuppressedBanner {
            Description = "Resolve shell completion suggestions"
            Help = None
            Arguments = [
                Argument.required "line" "Current command line"
                Argument.required "cursor" "Cursor position (integer)"
            ]
            Options = []
            Initialize = None
            Interact = None
            Execute = ExecuteAsync <| fun (input, output) ->
                async {
                    let usage () =
                        output.Message "Usage: __completion <line> <cursor>"
                        ExitCode.Error

                    match input with
                    | Input.Argument.Value "line" line & Input.Argument.Value "cursor" cursorStr ->
                        match System.Int32.TryParse cursorStr with
                        | true, cursor ->
                            let! suggestions = resolve commands applicationOptions { Line = line; CursorPosition = cursor }
                            suggestions |> List.iter (CompletionSuggestion.value >> output.Message)
                            return ExitCode.Success
                        | false, _ -> return usage ()
                    | _ -> return usage ()
                }
        }
        |> Result.orFail

    /// Ensure the completion command name (and the hidden resolver) don't collide with reserved or user commands.
    let validateConfig (userCommandNames: string list) (config: CompletionConfig) : Result<unit, ConsoleApplicationError> =
        let reserved name = Error (ConsoleApplicationError.CommandNameError (CommandNameError.Reserved name))
        let taken = CommandNames.all @ userCommandNames

        if taken |> List.contains config.CommandName then reserved config.CommandName
        elif config.CommandName = HiddenResolverCommandName || taken |> List.contains HiddenResolverCommandName then reserved HiddenResolverCommandName
        else Ok ()

    /// Wire completion commands into the application.
    /// Returns the commands available for dispatch (including the hidden resolver) and the ones shown by list/help.
    let wire (config: CompletionConfig) (appName: string) (applicationOptions: OptionsDefinitions) (commands: Commands) =
        let withScript = commands |> Commands.add config.CommandName (scriptCommand config appName)
        let dispatch = withScript |> Commands.add HiddenResolverCommandName (resolverCommand commands applicationOptions)
        let discovery = if config.IsVisible then withScript else commands
        dispatch, discovery
