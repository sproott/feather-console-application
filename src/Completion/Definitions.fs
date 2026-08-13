namespace Feather.ConsoleApplication.Completion

open Feather.ConsoleApplication

/// Answers the questions a completion asks about the application's definitions.
[<RequireQualifiedAccess>]
module internal Definitions =
    open System

    /// A short token may cluster several shortcuts (`-abc`). Parsing stops at the first shortcut
    /// taking a value, since the rest of the token is then that value.
    type ShortcutCluster =
        | ValueLessShortcuts
        | ShortcutWithValue of Option * string
        | UndefinedShortcut

    /// What a token already on the line means for the words after it.
    [<RequireQualifiedAccess>]
    type TokenRole =
        | EndOfOptions
        | InlineValueOption
        | ValueExpectingOption of Option
        | FlagOption
        | Positional

    [<Literal>]
    let EndOfOptions = "--"

    /// Accepts a lone `-` or `--` too: the parser has no option to match them against, but
    /// completion must already treat them as the start of one.
    let isOptionLike token =
        token |> String.startsWith "-"

    /// Every shortcut variant an option answers to, paired with its option.
    let shortcutVariants (options: OptionsDefinitions): (string * Option) list =
        options
        |> List.collect (fun (option: Option) ->
            match option |> Option.shortcut with
            | Some shortcut -> shortcut |> OptionShortcut.variants |> List.map (fun variant -> variant, option)
            | None -> []
        )

    let optionRequiresValue (option: Option) =
        match option.Value with
        | OptionValueDefinition.ValueRequired _
        | OptionValueDefinition.ValueRequiredArray _ -> true
        | OptionValueDefinition.ValueNone
        | OptionValueDefinition.ValueOptional _
        | OptionValueDefinition.ValueIsArray _ -> false

    let optionTakesValue (option: Option) =
        match option.Value with
        | OptionValueDefinition.ValueNone -> false
        | _ -> true

    let findOption (token: string) (options: OptionsDefinitions) =
        options |> List.tryFind (fun option -> token |> Option.isMatching option)

    [<TailCall>]
    let rec private resolveShortcutLetters (options: OptionsDefinitions) letters =
        match letters with
        | [] -> ValueLessShortcuts
        | letter :: rest ->
            match options |> findOption ("-" + string (letter: char)) with
            | None -> UndefinedShortcut
            | Some option when option |> optionTakesValue -> ShortcutWithValue (option, rest |> List.toArray |> String)
            | Some _ -> rest |> resolveShortcutLetters options

    let resolveShortcutCluster (token: string) (options: OptionsDefinitions) =
        token.TrimStart '-' |> List.ofSeq |> resolveShortcutLetters options

    /// The option whose value is completed at the position right after the given token.
    let optionExpectingValue (token: string) (options: OptionsDefinitions) =
        if token |> String.startsWith "--" then
            options |> findOption token |> Option.filter optionRequiresValue
        elif token |> String.startsWith "-" then
            match options |> resolveShortcutCluster token with
            | ShortcutWithValue (option, "") when option |> optionRequiresValue -> Some option
            | _ -> None
        else None

    let classifyToken (options: OptionsDefinitions) (token: string): TokenRole =
        if token = EndOfOptions then TokenRole.EndOfOptions
        elif token |> isOptionLike then
            match token |> Words.splitAssignment with
            | Some _ -> TokenRole.InlineValueOption
            | None ->
                match options |> optionExpectingValue token with
                | Some option -> TokenRole.ValueExpectingOption option
                | None -> TokenRole.FlagOption
        else TokenRole.Positional

    let findCommand (name: string) (commands: Commands) =
        match name |> CommandName.createInRuntime with
        | Error _ -> None
        | Ok commandName ->
            match commands |> Commands.find commandName with
            | ExactlyOne (_, command) -> Some command
            | MoreThanOne _
            | NoCommand _ -> None
