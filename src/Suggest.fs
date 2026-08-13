namespace Feather.ConsoleApplication

open Feather.ErrorHandling

type CompletionSuggestion = {
    Value: string
    /// Shown next to the value by the shells able to display it.
    Description: string option
}

[<RequireQualifiedAccess>]
module CompletionSuggestion =
    let create value = { Value = value; Description = None }

    /// An empty description is the same as none, so nothing is shown for it.
    let createDescribed description value = {
        Value = value
        Description = if description |> String.isNullOrEmpty then None else Some description
    }

    let value (suggestion: CompletionSuggestion) = suggestion.Value

    let description (suggestion: CompletionSuggestion) = suggestion.Description

type CompletionContext = {
    /// Token being typed at the cursor, empty when the cursor starts a new one.
    CurrentWord: string
    /// Tokens on the line before the current word, including the executable name.
    PrecedingWords: string list
}

/// Provides value suggestions for an option or a positional argument.
[<RequireQualifiedAccess>]
type Suggest =
    | SuggestSync of (CompletionContext -> CompletionSuggestion list)
    | SuggestAsync of (CompletionContext -> Async<CompletionSuggestion list>)

[<RequireQualifiedAccess>]
module Suggest =
    open System.IO

    type FileSystemListers = {
        DirectoryExists: string -> bool
        FileExists: string -> bool
        EnumerateFileSystemEntries: string -> seq<string>
        EnumerateDirectories: string -> seq<string>
    }

    let fileSystemListers: FileSystemListers = {
        DirectoryExists = Directory.Exists
        FileExists = File.Exists
        EnumerateFileSystemEntries = Directory.EnumerateFileSystemEntries
        EnumerateDirectories = Directory.EnumerateDirectories
    }

    let private completionPathParts (currentWord: string) =
        let dirPath =
            if String.isNullOrEmpty currentWord then
                "."
            else
                let dir = Path.GetDirectoryName currentWord
                if String.isNullOrEmpty dir then "." else dir

        let baseName = Path.GetFileName currentWord
        dirPath, baseName

    let private includeByDotfileRule (baseName: string) (name: string) =
        if name |> String.startsWith "." then
            baseName |> String.startsWith "."
        else
            not (baseName |> String.startsWith ".")

    let private prefixedName dirPath name =
        if dirPath = "." then
            name
        else
            Path.Combine(dirPath, name)

    let private matchesBaseName (baseName: string) (name: string) =
        name |> String.startsWith baseName

    let private isVisibleAndMatchingName baseName name =
        includeByDotfileRule baseName name
        && matchesBaseName baseName name

    let private tryListNames (listers: FileSystemListers) (enumeratePath: string -> seq<string>) (dirPath: string) =
        try
            if listers.DirectoryExists dirPath then
                enumeratePath dirPath
                |> Seq.toList
                |> List.map (fun path -> Path.GetFileName(path))
            else
                []
        with _ ->
            []

    let private matchingNames listers enumeratePath currentWord =
        let dirPath, baseName = completionPathParts currentWord

        let names =
            tryListNames listers enumeratePath dirPath
            |> List.filter (isVisibleAndMatchingName baseName)

        dirPath, names

    let private asAsync (suggest: Suggest): CompletionContext -> Async<CompletionSuggestion list> =
        match suggest with
        | Suggest.SuggestSync callback -> callback >> async.Return
        | Suggest.SuggestAsync callback -> callback

    let private withValue value (suggestion: CompletionSuggestion) =
        { suggestion with Value = value }

    /// A completion round must stay snappy, so per-value callbacks are not fanned out without a bound.
    [<Literal>]
    let private MaxConcurrentValues = 8

    let private mapSuggestions (f: CompletionSuggestion list -> CompletionSuggestion list) = function
        | Suggest.SuggestSync callback -> Suggest.SuggestSync (callback >> f)
        | Suggest.SuggestAsync callback -> Suggest.SuggestAsync (callback >> Async.map f)

    let private chooseBounded (f: CompletionSuggestion -> Async<CompletionSuggestion option>) (suggest: Suggest): Suggest =
        Suggest.SuggestAsync (fun ctx ->
            asAsync suggest ctx
            |> Async.bind (fun suggestions ->
                Async.Parallel(suggestions |> List.map f, MaxConcurrentValues)
                |> Async.map (Array.toList >> List.choose id)
            )
        )

    let values (values: string list): Suggest =
        Suggest.SuggestSync (fun _ -> values |> List.map CompletionSuggestion.create)

    /// Values paired with the description shown next to them.
    let describedValues (values: (string * string) list): Suggest =
        Suggest.SuggestSync (fun _ ->
            values |> List.map (fun (value, description) -> value |> CompletionSuggestion.createDescribed description)
        )

    let map (f: string -> string): Suggest -> Suggest =
        mapSuggestions (List.map (fun suggestion -> suggestion |> withValue (f suggestion.Value)))

    let mapAsync (f: string -> Async<string>): Suggest -> Suggest =
        chooseBounded (fun suggestion ->
            f suggestion.Value |> Async.map (fun value -> Some (suggestion |> withValue value)))

    let filter (predicate: string -> bool): Suggest -> Suggest =
        mapSuggestions (List.filter (CompletionSuggestion.value >> predicate))

    let filterAsync (predicate: string -> Async<bool>): Suggest -> Suggest =
        chooseBounded (fun suggestion ->
            predicate suggestion.Value |> Async.map (fun isValid -> if isValid then Some suggestion else None))

    let internal filesWith (listers: FileSystemListers): Suggest =
        Suggest.SuggestSync (fun ctx ->
            let dirPath, names = matchingNames listers listers.EnumerateFileSystemEntries ctx.CurrentWord

            names
            |> List.map (fun name ->
                let fullPath = Path.Combine(dirPath, name)
                let withPrefix = prefixedName dirPath name

                if listers.DirectoryExists fullPath then
                    withPrefix + "/"
                else
                    withPrefix
                |> CompletionSuggestion.create
            )
        )

    /// Files and directories under the current word, directories with a trailing slash.
    /// Dot-prefixed entries are suggested only when the current word starts with a dot.
    let files: Suggest =
        filesWith fileSystemListers

    let internal directoriesWith (listers: FileSystemListers): Suggest =
        Suggest.SuggestSync (fun ctx ->
            let dirPath, names = matchingNames listers listers.EnumerateDirectories ctx.CurrentWord

            names
            |> List.map (fun name ->
                let withPrefix = prefixedName dirPath name
                withPrefix + "/" |> CompletionSuggestion.create
            )
        )

    /// Directories under the current word, each with a trailing slash.
    /// Dot-prefixed directories are suggested only when the current word starts with a dot.
    let directories: Suggest =
        directoriesWith fileSystemListers

    let internal filesWithExtensionWith (listers: FileSystemListers) (extensions: string list): Suggest =
        let normalizedExts = extensions |> List.map (fun e -> if e |> String.startsWith "." then e else "." + e)

        Suggest.SuggestSync (fun ctx ->
            let dirPath, names = matchingNames listers listers.EnumerateFileSystemEntries ctx.CurrentWord

            names
            |> List.choose (fun name ->
                let fullPath = Path.Combine(dirPath, name)
                if listers.DirectoryExists fullPath then
                    Some (prefixedName dirPath name + "/" |> CompletionSuggestion.create)
                elif listers.FileExists fullPath then
                    let ext = Path.GetExtension name
                    if normalizedExts |> List.contains ext then
                        Some (prefixedName dirPath name |> CompletionSuggestion.create)
                    else
                        None
                else
                    None
            )
        )

    /// Files with one of the given extensions (given with or without the leading dot),
    /// plus all directories, so nested paths stay reachable.
    let filesWithExtension (extensions: string list): Suggest =
        filesWithExtensionWith fileSystemListers extensions

    // Constructors are declared last, so that `async` does not shadow the builder for the rest of the module.

    let sync (f: CompletionContext -> string list): Suggest =
        Suggest.SuggestSync (f >> List.map CompletionSuggestion.create)

    let async (f: CompletionContext -> Async<string list>): Suggest =
        Suggest.SuggestAsync (f >> Async.map (List.map CompletionSuggestion.create))
