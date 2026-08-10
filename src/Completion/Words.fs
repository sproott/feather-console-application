namespace Feather.ConsoleApplication.Completion

open Feather.ConsoleApplication

/// Turns the words a shell hands over into the words a completion is resolved against: bash
/// breaks a word at `=` and every shell leaves its own quoting in the words it passes, neither
/// of which the resolution rules should have to know about.
[<RequireQualifiedAccess>]
module internal Words =
    open System

    /// Splits `--name=value` into the option token and the value typed after the `=`.
    let splitAssignment (token: string) =
        match token.IndexOf '=' with
        | -1 -> None
        | index -> Some (token.Substring(0, index), token.Substring(index + 1))

    [<TailCall>]
    let rec private rejoinAssignments accumulated words =
        match words with
        | [] -> accumulated |> List.rev
        | option :: "=" :: value :: rest -> rest |> rejoinAssignments (option + "=" + value :: accumulated)
        | [ option; "=" ] -> [] |> rejoinAssignments (option + "=" :: accumulated)
        | word :: rest -> rest |> rejoinAssignments (word :: accumulated)

    let private unquote (word: string) =
        if word.Length >= 2 && (word[0] = '"' || word[0] = '\'') && word[word.Length - 1] = word[0]
        then word.Substring(1, word.Length - 2)
        else word

    [<TailCall>]
    let rec private unescapeChars accumulated characters =
        match characters with
        | []
        | [ '\\' ] -> accumulated |> List.rev |> List.toArray |> String
        | '\\' :: escaped :: rest -> rest |> unescapeChars (escaped :: accumulated)
        | character :: rest -> rest |> unescapeChars (character :: accumulated)

    let private unescape (word: string) =
        word |> List.ofSeq |> unescapeChars []

    /// The word at the index is the one being completed; what follows the cursor is not part of it.
    let normalize (words: string list) (index: int) : CompletionContext =
        let index = index |> max 0

        let upToCursor =
            (words |> List.truncate index) @ [ words |> List.tryItem index |> Option.defaultValue "" ]
            |> rejoinAssignments []
            |> List.map (unquote >> unescape)

        match upToCursor |> List.rev with
        | currentWord :: reversedPrecedingWords -> {
            CurrentWord = currentWord
            PrecedingWords = reversedPrecedingWords |> List.rev
          }
        | [] -> { CurrentWord = ""; PrecedingWords = [] }
