namespace Feather.ConsoleApplication.Completion

[<RequireQualifiedAccess>]
type internal Shell =
    | Bash
    | Zsh
    | Fish

[<RequireQualifiedAccess>]
type internal ShellError =
    | UnknownShell of string

[<RequireQualifiedAccess>]
module internal Shell =
    let all = [ Shell.Bash; Shell.Zsh; Shell.Fish ]

    let value = function
        | Shell.Bash -> "bash"
        | Shell.Zsh -> "zsh"
        | Shell.Fish -> "fish"

    let names = all |> List.map value

    let create (name: string): Result<Shell, ShellError> =
        match all |> List.tryFind (fun shell -> shell |> value = name.ToLowerInvariant()) with
        | Some shell -> Ok shell
        | None -> Error (ShellError.UnknownShell name)

[<RequireQualifiedAccess>]
module internal ShellError =
    let format = function
        | ShellError.UnknownShell name ->
            sprintf "Unknown shell \"%s\", expected one of %s." name (Shell.names |> String.concat "|")
