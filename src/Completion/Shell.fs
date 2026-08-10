namespace Feather.ConsoleApplication.Completion

[<RequireQualifiedAccess>]
type internal Shell =
    | Bash
    | Zsh
    | Fish

[<RequireQualifiedAccess>]
module internal Shell =
    let all = [ Shell.Bash; Shell.Zsh; Shell.Fish ]

    let value = function
        | Shell.Bash -> "bash"
        | Shell.Zsh -> "zsh"
        | Shell.Fish -> "fish"

    let names = all |> List.map value

    let parse (name: string) =
        all |> List.tryFind (fun shell -> shell |> value = name.ToLowerInvariant())
