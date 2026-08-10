namespace Feather.ConsoleApplication.Completion

open Feather.ConsoleApplication

/// Answers the questions a completion asks about the application's definitions: which option
/// or command a token names, and whether the position after it belongs to an option value.
[<RequireQualifiedAccess>]
module internal Definitions =
    open System

    let isOptionToken token =
        token |> String.startsWith "-"

    let longOptionSuggestions (options: OptionsDefinitions): CompletionSuggestion list =
        options
        |> List.map (fun (option: Option) ->
            let optionName = option |> Option.name |> OptionName.value
            "--" + optionName |> CompletionSuggestion.describe option.Description)

    let shortOptionSuggestions (options: OptionsDefinitions): CompletionSuggestion list =
        options
        |> List.choose (fun (option: Option) ->
            match option |> Option.shortcut with
            | Some (OptionShortcut shortcut) ->
                shortcut.Split('|')
                |> Seq.toList
                |> List.map (fun s -> "-" + s |> CompletionSuggestion.describe option.Description)
                |> Some
            | None -> None
        )
        |> List.concat

    let optionRequiresValue (option: Option) =
        match option.Value with
        | OptionValueDefinition.ValueNone -> false
        | OptionValueDefinition.ValueRequired _ -> true
        | OptionValueDefinition.ValueOptional _ -> false
        | OptionValueDefinition.ValueIsArray _ -> false
        | OptionValueDefinition.ValueRequiredArray _ -> true

    let optionTakesValue (option: Option) =
        match option.Value with
        | OptionValueDefinition.ValueNone -> false
        | _ -> true

    let findOptionByName (name: string) (options: OptionsDefinitions) =
        let cleanName = name.TrimStart('-')
        options
        |> List.tryFind (fun option -> option |> Option.name |> OptionName.value = cleanName)

    let findOptionByShortcut (shortcut: string) (options: OptionsDefinitions) =
        let cleanShortcut = shortcut.TrimStart('-')
        options
        |> List.tryFind (fun option ->
            match option |> Option.shortcut with
            | Some (OptionShortcut s) -> s.Split('|') |> Array.contains cleanShortcut
            | None -> false
        )

    let findOption (token: string) (options: OptionsDefinitions) =
        if token |> String.startsWith "--" then options |> findOptionByName token
        elif token |> String.startsWith "-" then options |> findOptionByShortcut token
        else None

    /// A short token may cluster several shortcuts (`-abc`). Parsing stops at the first shortcut
    /// taking a value, since the rest of the token is then that value.
    type ShortcutCluster =
        | ValueLessShortcuts
        | ShortcutWithValue of Option * string
        | UndefinedShortcut

    [<TailCall>]
    let rec private resolveShortcutLetters (options: OptionsDefinitions) letters =
        match letters with
        | [] -> ValueLessShortcuts
        | letter :: rest ->
            match options |> findOptionByShortcut (string (letter: char)) with
            | None -> UndefinedShortcut
            | Some option when option |> optionTakesValue -> ShortcutWithValue (option, rest |> List.toArray |> String)
            | Some _ -> rest |> resolveShortcutLetters options

    let resolveShortcutCluster (token: string) (options: OptionsDefinitions) =
        token.TrimStart '-' |> List.ofSeq |> resolveShortcutLetters options

    /// The option whose value is completed at the position right after the given token.
    let optionExpectingValue (token: string) (options: OptionsDefinitions) =
        if token |> String.startsWith "--" then
            options |> findOptionByName token |> Option.filter optionRequiresValue
        elif token |> String.startsWith "-" then
            match options |> resolveShortcutCluster token with
            | ShortcutWithValue (option, "") when option |> optionRequiresValue -> Some option
            | _ -> None
        else None

    let findCommand (name: string) (commands: Commands) =
        match name |> CommandName.createInRuntime with
        | Error _ -> None
        | Ok commandName ->
            match commands |> Commands.find commandName with
            | ExactlyOne (_, command) -> Some command
            | MoreThanOne _
            | NoCommand _ -> None
