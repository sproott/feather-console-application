namespace Feather.ConsoleApplication.Completion

open Feather.ConsoleApplication
open Feather.ErrorHandling

/// Resolves what the shell offers at the cursor: a command name, an option name, an option
/// value or a positional argument value.
[<RequireQualifiedAccess>]
module internal Suggestions =

    let private runSuggestion context (filter: CompletionSuggestion list -> CompletionSuggestion list) suggestion : Async<CompletionSuggestion list> =
        async {
            match suggestion with
            | Some (Suggest.SuggestSync f) -> return f context |> filter
            | Some (Suggest.SuggestAsync f) -> return! f context |> Async.map filter
            | None -> return []
        }

    let private byPrefix prefix = List.filter (CompletionSuggestion.value >> String.startsWith prefix)

    /// Every suggestion is a whole word, so a value completed inside a longer word carries what precedes it.
    let private withPrefix prefix =
        List.map (fun (suggestion: CompletionSuggestion) -> { suggestion with Value = prefix + suggestion.Value })

    let private commandNameSuggestions prefix (commands: Commands) =
        commands
        |> Map.toList
        |> List.map (fun (name, command) ->
            name |> CommandName.value |> CompletionSuggestion.describe (command |> Command.description))
        |> byPrefix prefix

    [<Literal>]
    let private EndOfOptions = "--"

    /// Counts the values already given for positional arguments, so that the next one can be suggested.
    /// A value belonging to an option is not a positional one.
    [<TailCall>]
    let rec private countPositionalValues options tokens count =
        match tokens with
        | [] -> count
        | EndOfOptions :: rest -> count + (rest |> List.length)
        | token :: rest when token |> Definitions.isOptionToken ->
            match token |> Words.splitAssignment with
            | Some _ -> countPositionalValues options rest count
            | None ->
                match options |> Definitions.optionExpectingValue token with
                | Some _ ->
                    match rest with
                    | [] -> count
                    | _ :: afterValue -> countPositionalValues options afterValue count
                | None -> countPositionalValues options rest count
        | _ :: rest -> countPositionalValues options rest (count + 1)

    /// Completes a short token as a cluster of shortcuts (`-abc`), whose last shortcut may carry
    /// its value in the same token (`-oValue`).
    let private shortcutSuggestions (currentWord: string) context (options: OptionsDefinitions) =
        let typedLetters = currentWord.TrimStart '-'

        if typedLetters |> String.isNullOrEmpty then
            async { return options |> Definitions.shortOptionSuggestions }
        else
            match options |> Definitions.resolveShortcutCluster currentWord with
            | Definitions.UndefinedShortcut -> async { return [] }
            | Definitions.ValueLessShortcuts ->
                async {
                    return
                        options
                        |> Definitions.shortOptionSuggestions
                        |> List.map (fun suggestion -> { suggestion with Value = suggestion.Value.TrimStart '-' })
                        |> List.filter (CompletionSuggestion.value >> typedLetters.Contains >> not)
                        |> List.map (fun suggestion -> { suggestion with Value = currentWord + suggestion.Value })
                }
            | Definitions.ShortcutWithValue (option, typedValue) ->
                let clusterPrefix = currentWord.Substring(0, currentWord.Length - typedValue.Length)

                option
                |> Option.suggestion
                |> runSuggestion { context with CurrentWord = typedValue } (byPrefix typedValue)
                |> Async.map (withPrefix clusterPrefix)

    let private suggestArgumentValue (command: Command) context filter argumentIndex : Async<CompletionSuggestion list> =
        match command |> Command.definitions |> snd |> List.tryItem argumentIndex with
        | Some argument -> argument |> Argument.suggestion |> runSuggestion context filter
        | None -> async { return [] }

    let resolve
        (commands: Commands)
        (applicationOptions: OptionsDefinitions)
        (completionInput: CompletionInput)
        : Async<CompletionSuggestion list> =
        async {
            try
                let context = Words.normalize completionInput.Words completionInput.Index
                let { CurrentWord = currentWord; PrecedingWords = precedingWords } = context

                match precedingWords with
                | []
                | [ _ ] ->
                    return commands |> commandNameSuggestions currentWord

                | _ :: commandNameValue :: _ ->
                    match commands |> Definitions.findCommand commandNameValue with
                    | None -> return []
                    | Some command ->
                        let allOptions = applicationOptions @ (command |> Command.definitions |> fst)
                        let argumentTokens = precedingWords |> List.skip 2
                        let optionsEnded = argumentTokens |> List.contains EndOfOptions

                        if not optionsEnded && currentWord |> Definitions.isOptionToken then
                            match currentWord |> Words.splitAssignment with
                            | Some (optionToken, typedValue) ->
                                match allOptions |> Definitions.findOption optionToken with
                                | Some option when option |> Definitions.optionRequiresValue ->
                                    // The `=` breaks the word in every shell, so only what follows it is replaced.
                                    return!
                                        option
                                        |> Option.suggestion
                                        |> runSuggestion { context with CurrentWord = typedValue } (byPrefix typedValue)
                                | _ -> return []
                            | None ->
                                if currentWord |> String.startsWith "--" then
                                    return
                                        allOptions
                                        |> Definitions.longOptionSuggestions
                                        |> byPrefix currentWord
                                else
                                    return! allOptions |> shortcutSuggestions currentWord context
                        else
                            let previousOption =
                                if optionsEnded then None
                                else
                                    precedingWords
                                    |> List.tryLast
                                    |> Option.filter (Words.splitAssignment >> Option.isNone)
                                    |> Option.bind (fun token -> allOptions |> Definitions.optionExpectingValue token)

                            match previousOption with
                            | Some option ->
                                return! option |> Option.suggestion |> runSuggestion context (byPrefix currentWord)
                            | None ->
                                let argumentIndex = countPositionalValues allOptions argumentTokens 0

                                return! argumentIndex |> suggestArgumentValue command context (byPrefix currentWord)
            with _ ->
                return []
        }
