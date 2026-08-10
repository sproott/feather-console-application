namespace Feather.ConsoleApplication

[<AutoOpen>]
module internal Utils =
    let debug (output: Feather.ConsoleStyle.ConsoleStyle) message =
        if output.IsDebug() then
            output.Message("<c:gray>[DEBUG]</c> %s", message)

    let tee f a =
        f a
        a

    let inline isNotNull a =
        a
        |> isNull
        |> not

[<RequireQualifiedAccess>]
module internal Bool =
    let toOption bool =
        if bool then Some ()
        else None

    let fromOption = function
        | Some _ -> true
        | _ -> false

[<RequireQualifiedAccess>]
module internal String =
    open System

    let append suffix string =
        sprintf "%s%s" string suffix

    let toUpper (string: string) = string.ToUpper()

    let startsWith (prefix: string) (string: string) =
        string.StartsWith(prefix, StringComparison.Ordinal)

    let toInt (string: string) =
        match string |> Int32.TryParse with
        | true, int -> Some int
        | _ -> None

    let replace: string -> string * string -> string =
        fun string (placeholder, replacement) ->
            string.Replace(placeholder, replacement)

    let isNullOrEmpty = String.IsNullOrWhiteSpace

    let (|IsNullOrEmpty|_|) string =
        string |> isNullOrEmpty |> Bool.toOption

    let (|StartsWith|_|) (prefixes: string list) (string: string) =
        prefixes
        |> List.tryPick (fun prefix -> if string.StartsWith(prefix) then Some (string, prefix) else None)

    let (|Contains|_|) (subStrings: string list) (string: string) =
        subStrings
        |> List.tryPick (fun subString -> if string.Contains(subString) then Some (string, subString) else None)

    let (|EndsWith|_|) (sufixes: string list) (string: string) =
        sufixes
        |> List.tryPick (fun sufix -> if string.EndsWith(sufix) then Some (string, sufix) else None)

[<AutoOpen>]
module internal Regexp =
    open System.Text.RegularExpressions

    // http://www.fssnip.net/29/title/Regular-expression-active-pattern
    let internal (|Regex|_|) pattern input =
        let m = Regex.Match(input, pattern)
        if m.Success then Some (List.tail [ for g in m.Groups -> g.Value ])
        else None

[<RequireQualifiedAccess>]
module internal List =
    let getDuplicatesBy f items =
        items
        |> List.groupBy f
        |> List.choose (fun (key, set) ->
            if set.Length > 1 then Some key
            else None
        )

[<RequireQualifiedAccess>]
module Help =
    let lines lines =
        lines
        |> String.concat "\n\n"
        |> Some
