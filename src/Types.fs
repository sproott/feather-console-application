namespace Feather.ConsoleApplication

open Feather.ErrorHandling

// Common types
// ***************************

type Name = private Name of string

[<RequireQualifiedAccess>]
type NameError =
    | Empty
    | StartsWith of name: string * string
    | Contains of name: string * string
    | EndsWith of name: string * string

[<RequireQualifiedAccess>]
module internal Name =
    let create invalidPrefixes invalidSubStrings invalidSufixes = function
        | String.IsNullOrEmpty -> Error NameError.Empty
        | String.StartsWith invalidPrefixes invalidPrefix -> Error (NameError.StartsWith invalidPrefix)
        | String.Contains invalidSubStrings invalidSubstring -> Error (NameError.Contains invalidSubstring)
        | String.EndsWith invalidSufixes invalidSufix -> Error (NameError.EndsWith invalidSufix)
        | name -> Ok (Name name)

[<RequireQualifiedAccess>]
module internal NameError =
    let format prefix = function
        | NameError.Empty -> sprintf "%s name is empty." prefix
        | NameError.StartsWith (name, invalid) -> sprintf "%s name \"%s\" must not starts with \"%s\"." prefix name invalid
        | NameError.Contains (name, invalid) -> sprintf "%s \"%s\" name must not contains \"%s\"." prefix name invalid
        | NameError.EndsWith (name, invalid) -> sprintf "%s name \"%s\" must not ends with \"%s\"." prefix name invalid

type ListWithItems<'Item> = private ListWithItems of 'Item list

type NotEmptyList<'Item> =
    | WithItems of ListWithItems<'Item>
    | PartiallyCreated of 'Item list

[<RequireQualifiedAccess>]
type NotEmptyListError =
    | NoValues

[<RequireQualifiedAccess>]
module internal NotEmptyListError =
    let format = function
        | NotEmptyListError.NoValues -> "List must have at least one value."

[<RequireQualifiedAccess>]
module NotEmptyList =
    let create = function
        | [] -> Error NotEmptyListError.NoValues
        | list -> Ok (WithItems (ListWithItems list))

    /// Create NotEmptyList with values, or fail with Exception. This function should not be used unless it you are sure, there are values.
    let ofListWithValues values =
        values
        |> create
        |> Result.orFail

    let values = function
        | WithItems (ListWithItems items) -> items
        | PartiallyCreated _ -> failwithf "NotEmptyList values are accessible only after is created with items! (Use \"NotEmptyList.complete\" function to finish creation)"

    /// Create NotEmptyList with values, which may be empty now, but to get values, you have to complete the creation first.
    /// Use "NotEmptyList.complete" function to do it.
    let internal createPartial = function
        | [] -> PartiallyCreated []
        | items -> WithItems (ListWithItems items)

    /// Complete a creation of NotEmptyList and validate current values.
    let internal complete = function
        | PartiallyCreated values -> values |> create
        | WithItems items -> Ok (WithItems items)

    let internal append values = function
        | PartiallyCreated [] -> createPartial values
        | PartiallyCreated items
        | WithItems (ListWithItems items) -> items @ values |> ofListWithValues

// Terminal I/O
// ***********************

type InputValue = string

[<RequireQualifiedAccess>]
type ExitCode =
    | Success
    | Error

[<RequireQualifiedAccess>]
module ExitCode =
    let code = function
        | ExitCode.Success -> 0
        | ExitCode.Error -> 1

    let internal fromResult printError = function
        | Ok code -> code
        | Error (error, commandInfo) ->
            error |> printError commandInfo
            ExitCode.Error

// Input / Output
// ***********************

open Feather.ConsoleStyle

type ProgressBar = ShellProgressBar.ProgressBar option

type Output = ConsoleStyle

[<RequireQualifiedAccess>]
module internal Output =
    let defaults = ConsoleStyle()

// Command
// ***************************

[<RequireQualifiedAccess>]
module internal ArgumentNames =
    [<Literal>]
    let Command = "command"

[<RequireQualifiedAccess>]
module OptionNames =
    [<Literal>]
    let Help = "help"
    [<Literal>]
    let Quiet = "quiet"
    [<Literal>]
    let Version = "version"
    [<Literal>]
    let Verbose = "verbose"
    [<Literal>]
    let NoInteraction = "no-interaction"
    [<Literal>]
    let NoProgress = "no-progress"
    [<Literal>]
    let NoAnsi = "no-ansi"

    let internal reserved = [ Help; Quiet; Version; Verbose; NoInteraction; NoProgress; NoAnsi ]

type ReservedShortcut = ReservedShortcut of shortcut: string * optionName: string

[<RequireQualifiedAccess>]
module OptionShortcuts =
    [<Literal>]
    let Help = "h"
    [<Literal>]
    let Quiet = "q"
    [<Literal>]
    let Version = "V"
    [<Literal>]
    let Verbose = "v|vv|vvv"
    [<Literal>]
    let NoInteraction = "n"

    let private reserved =
        [
            Help, OptionNames.Help
            Quiet, OptionNames.Quiet
            Version, OptionNames.Version
            Verbose, OptionNames.Verbose
            NoInteraction, OptionNames.NoInteraction
        ]
        |> Map.ofList

    let internal (|IsReserved|_|) shortcut: ReservedShortcut option =
        reserved
        |> Map.tryFind shortcut
        |> Option.map (fun option -> ReservedShortcut (shortcut, option))

//
// Command name
//

[<RequireQualifiedAccess>]
type CommandNameError =
    | Reserved of string
    | NameError of NameError
    | Invalid of string

[<RequireQualifiedAccess>]
module internal CommandNameError =
    let format = function
        | CommandNameError.Reserved reservedName -> sprintf "Command name \"%s\" is reserved. Please use something else." reservedName
        | CommandNameError.NameError error -> NameError.format "Command" error
        | CommandNameError.Invalid invalidName -> sprintf "Command name \"%s\" is invalid." invalidName

type CommandName = private CommandName of Name

[<RequireQualifiedAccess>]
module CommandNames =
    [<Literal>]
    let List = "list"
    [<Literal>]
    let Help = "help"
    [<Literal>]
    let About = "about"
    [<Literal>]
    let Exit = "exit"

    let internal all = [ List; Help; About; Exit ]

[<RequireQualifiedAccess>]
module internal CommandName =
    open Feather.ErrorHandling.Result.Operators

    [<Literal>]
    let NamespaceSeparator = ":"

    let private createName = function
        | Regex @"^([\w\.\-: ]*)$" _ as name ->
            name
            |> Name.create [ NamespaceSeparator; "-" ] [ " "; NamespaceSeparator + NamespaceSeparator ] [ NamespaceSeparator ]
            <!> CommandName
            <@> CommandNameError.NameError
        | invalidName -> Error (CommandNameError.Invalid invalidName)

    let create = function
        | ArgumentNames.Command -> Error (CommandNameError.Reserved ArgumentNames.Command)
        | name when CommandNames.all |> List.contains name -> Error (CommandNameError.Reserved name)
        | name -> name |> createName

    let createInRuntime = function
        | ArgumentNames.Command -> Error (CommandNameError.Invalid ArgumentNames.Command)
        | name -> name |> createName

    let value (CommandName (Name name)) = name
    let splitByNamespaces (CommandName (Name name)) = name.Split NamespaceSeparator |> Seq.toList
    let namespaceValue = splitByNamespaces >> List.tryHead

    let isMatchingPattern pattern name =
        match name |> value with
        | Regex pattern _ -> Some name
        | _ -> None

    /// Match string as CommandName.
    /// NOTE: It does NOT check, whether such command exists.
    let (|IsCommandName|_|) = createInRuntime >> Result.toOption

//
// Application name, version, ...
//

[<RequireQualifiedAccess>]
type ApplicationNameError =
    | NameError of NameError

[<RequireQualifiedAccess>]
module internal ApplicationNameError =
    let format = function
        | ApplicationNameError.NameError error -> NameError.format "Application" error

type ApplicationName = private ApplicationName of Name

[<RequireQualifiedAccess>]
module internal ApplicationName =
    open Feather.ErrorHandling.Result.Operators

    let create name =
        name
        |> Name.create [] [] []
        <!> ApplicationName
        <@> ApplicationNameError.NameError

    let value (ApplicationName (Name name)) = name

type ApplicationVersion = internal ApplicationVersion of string

[<RequireQualifiedAccess>]
module internal ApplicationVersion =
    let value (ApplicationVersion version) = version

type ApplicationTitle = internal ApplicationTitle of string

[<RequireQualifiedAccess>]
module internal ApplicationTitle =
    let value (ApplicationTitle title) = title

type ApplicationMeta = {
    Name: ApplicationName
    Version: ApplicationVersion option
    Title: ApplicationTitle option
    Description: string option
    GitRepository: string option
    GitBranch: string option
    GitCommit: string option
    CreatedAt: System.DateTime option
    Meta: string list list
}

// Errors
// ***************************

// Definitions

[<RequireQualifiedAccess>]
type ArgumentNameError =
    | NameError of NameError
    | Reserved of string

[<RequireQualifiedAccess>]
module internal ArgumentNameError =
    let format = function
        | ArgumentNameError.NameError error -> NameError.format "Argument" error
        | ArgumentNameError.Reserved reservedName -> sprintf "Argument name \"%s\" is reserved. Please use something else." reservedName

[<RequireQualifiedAccess>]
type ArgumentDefinitionError =
    | ArgumentNameError of ArgumentNameError
    | ArgumentAlreadyExists of string
    | ArgumentAfterArrayArgument
    | RequiredArgumentAfterOptional

[<RequireQualifiedAccess>]
module internal ArgumentDefinitionError =
    let format = function
        | ArgumentDefinitionError.ArgumentNameError error -> ArgumentNameError.format error
        | ArgumentDefinitionError.ArgumentAlreadyExists notUnique -> sprintf "An argument with name \"%s\" already exists." notUnique
        | ArgumentDefinitionError.ArgumentAfterArrayArgument -> "Cannot add an argument after an array argument."
        | ArgumentDefinitionError.RequiredArgumentAfterOptional -> "Cannot add a required argument after an optional one."

[<RequireQualifiedAccess>]
type OptionNameError =
    | NameError of NameError
    | Reserved of string

[<RequireQualifiedAccess>]
module internal OptionNameError =
    let format = function
        | OptionNameError.NameError error -> NameError.format "Option" error
        | OptionNameError.Reserved reservedName -> sprintf "Option name \"%s\" is reserved. Please use something else." reservedName

[<RequireQualifiedAccess>]
type OptionShortcutError =
    | Empty
    | Contains of shortcut: string * string
    | MoreThanSingleLetter of string
    | Reserved of ReservedShortcut

[<RequireQualifiedAccess>]
module internal OptionShortcutError =
    let format = function
        | OptionShortcutError.Empty -> "An option shortcut cannot be empty."
        | OptionShortcutError.Contains (shortcut, invalid) -> sprintf "An option shortcut \"%s\" cannot contain \"%s\"." shortcut invalid
        | OptionShortcutError.MoreThanSingleLetter shortcut -> sprintf "An option shortcut \"%s\" cannon be more than single letter." shortcut
        | OptionShortcutError.Reserved (ReservedShortcut (shortut, option)) -> sprintf "An option shortcut \"%s\" is reserved for option \"%s\". Please use something else." shortut option

[<RequireQualifiedAccess>]
type OptionDefinitionError =
    | OptionNameError of OptionNameError
    | OptionShortcutError of OptionShortcutError
    | RequiredArrayDefaultValueError of NotEmptyListError
    | OptionAlreadyExists of string
    | OptionShortcutAlreadyExists of string

[<RequireQualifiedAccess>]
module internal OptionDefinitionError =
    let format = function
        | OptionDefinitionError.OptionNameError error -> OptionNameError.format error
        | OptionDefinitionError.OptionShortcutError error -> OptionShortcutError.format error
        | OptionDefinitionError.RequiredArrayDefaultValueError error -> NotEmptyListError.format error
        | OptionDefinitionError.OptionAlreadyExists notUnique -> sprintf "An option named \"%s\" already exists." notUnique
        | OptionDefinitionError.OptionShortcutAlreadyExists notUnique -> sprintf "An option with shortcut \"%s\" already exists." notUnique

[<RequireQualifiedAccess>]
type CommandDefinitionError =
    | ArgumentDefinitionError of ArgumentDefinitionError list
    | OptionDefinitionError of OptionDefinitionError list
    | InvalidCustomTags of string list

[<RequireQualifiedAccess>]
module internal CommandDefinitionError =
    let format = function
        | CommandDefinitionError.ArgumentDefinitionError errors -> errors |> List.map ArgumentDefinitionError.format
        | CommandDefinitionError.OptionDefinitionError errors -> errors |> List.map OptionDefinitionError.format
        | CommandDefinitionError.InvalidCustomTags errors -> errors |> List.map (sprintf "Invalid Custom Tag definition: %s")

// Runtime

[<RequireQualifiedAccess>]
type OptionsError =
    | RequiredValueNotSet of string
    | UndefinedOption of string

[<RequireQualifiedAccess>]
module internal OptionsError =
    let format = function
        | OptionsError.RequiredValueNotSet option -> sprintf "The \"--%s\" option requires a value." option
        | OptionsError.UndefinedOption undefinedOption -> sprintf "The \"%s\" option does not exist." undefinedOption

[<RequireQualifiedAccess>]
type ArgumentsError =
    | NotEnoughArguments of string
    | TooManyArguments of string list

[<RequireQualifiedAccess>]
module internal ArgumentsError =
    let format = function
        | ArgumentsError.NotEnoughArguments missingArgument -> sprintf "Not enough arguments (missing: \"%s\")." missingArgument
        | ArgumentsError.TooManyArguments definedArguments ->
            match definedArguments with
            | [] -> "Too many arguments, no arguments expected."
            | definedArguments ->
                definedArguments
                |> List.map (sprintf "\"%s\"")
                |> String.concat " "
                |> sprintf "Too many arguments, expected arguments %s."

[<RequireQualifiedAccess>]
type InputError =
    | OptionsError of OptionsError
    | ArgumentsError of ArgumentsError

[<RequireQualifiedAccess>]
module internal InputError =
    let format = function
        | InputError.OptionsError error -> OptionsError.format error
        | InputError.ArgumentsError error -> ArgumentsError.format error

[<RequireQualifiedAccess>]
type ArgsError =
    | CommandNameError of CommandNameError
    | CommandNotFound of CommandName
    | AmbigousCommandFound of CommandName * CommandName list
    | InputError of InputError

[<RequireQualifiedAccess>]
module CommandNotFound =
    let create commandName =
        ArgsError.CommandNotFound (CommandName (Name commandName))

[<RequireQualifiedAccess>]
module AmbigousCommandFound =
    let create command commands =
        ArgsError.AmbigousCommandFound (CommandName (Name command), commands |> List.map (Name >> CommandName))

[<RequireQualifiedAccess>]
module internal ArgsError =
    let format = function
        | ArgsError.CommandNameError error -> CommandNameError.format error
        | ArgsError.CommandNotFound name -> name |> CommandName.value |> sprintf "Command \"%s\" is not defined. Run \"list\" to show available commands."
        | ArgsError.AmbigousCommandFound (name, names) ->
            sprintf "Command \"%s\" is ambiguous.\n\nDid you mean one of these?\n%s"
                (name |> CommandName.value)
                (names |> List.map (CommandName.value >> sprintf "    %s") |> String.concat "\n") // todo <later> add description to the command name
        | ArgsError.InputError error -> InputError.format error

[<RequireQualifiedAccess>]
type CommandError =
    | Exception of exn
    | Message of string
    | Errors of CommandError list

[<RequireQualifiedAccess>]
type ConsoleApplicationError =
    | ArgsError of ArgsError
    | CommandNameError of CommandNameError
    | ApplicationNameError of ApplicationNameError
    | CommandDefinitionError of CommandDefinitionError
    | ConsoleApplicationException of exn
    | CommandError of CommandError

[<RequireQualifiedAccess>]
module internal ConsoleApplicationError =
    [<TailCall>]
    let rec private formatPending showDetails pending acc =
        match pending with
        | [] -> List.rev acc
        | current :: rest ->
            match current with
            | CommandError.Exception ex ->
                let message =
                    if showDetails then
                        sprintf "%A" ex
                    else
                        ex.Message

                formatPending showDetails rest (message :: acc)
            | CommandError.Message message ->
                formatPending showDetails rest (message :: acc)
            | CommandError.Errors errors ->
                formatPending showDetails (errors @ rest) acc

    let private formatCommandError showDetails (error: CommandError) =
        formatPending showDetails [ error ] []

    let format showDetails = function
        | ConsoleApplicationError.ArgsError error -> [ ArgsError.format error ]
        | ConsoleApplicationError.CommandNameError error -> [ CommandNameError.format error ]
        | ConsoleApplicationError.ApplicationNameError error -> [ ApplicationNameError.format error ]
        | ConsoleApplicationError.CommandDefinitionError error -> CommandDefinitionError.format error
        | ConsoleApplicationError.ConsoleApplicationException error ->
            CommandError.Exception error
            |> formatCommandError showDetails
        | ConsoleApplicationError.CommandError error ->
            error
            |> formatCommandError showDetails

// Completion
// ***************************

type CompletionInput = {
    /// Words the shell tokenized, including the executable name.
    Words: string list
    /// Index into `Words` of the word being completed.
    Index: int
}

type CompletionSuggestion = {
    Value: string
    /// Shown next to the value by the shells able to display it.
    Description: string option
}

[<RequireQualifiedAccess>]
module CompletionSuggestion =
    let create value = { Value = value; Description = None }

    /// An empty description is the same as none, so nothing is shown for it.
    let describe description value = {
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

    let values (values: string list): Suggest =
        Suggest.SuggestSync (fun _ -> values |> List.map CompletionSuggestion.create)

    /// Values paired with the description shown next to them.
    let describedValues (values: (string * string) list): Suggest =
        Suggest.SuggestSync (fun _ ->
            values |> List.map (fun (value, description) -> value |> CompletionSuggestion.describe description)
        )

    let map (f: string -> string) (suggest: Suggest): Suggest =
        let mapValues = List.map (fun suggestion -> suggestion |> withValue (f suggestion.Value))

        match suggest with
        | Suggest.SuggestSync callback ->
            Suggest.SuggestSync (fun ctx -> callback ctx |> mapValues)
        | Suggest.SuggestAsync callback ->
            Suggest.SuggestAsync (fun ctx ->
                callback ctx |> Async.map mapValues
            )

    let mapAsync (f: string -> Async<string>) (suggest: Suggest): Suggest =
        Suggest.SuggestAsync (fun ctx ->
            suggest
            |> asAsync
            |> fun run -> run ctx
            |> Async.bind (fun suggestions ->
                let mappedValues =
                    suggestions
                    |> List.map (fun suggestion -> f suggestion.Value |> Async.map (fun value -> suggestion |> withValue value))

                Async.Parallel(mappedValues, MaxConcurrentValues)
                |> Async.map Array.toList
            )
        )

    let filter (predicate: string -> bool) (suggest: Suggest): Suggest =
        let filterValues = List.filter (CompletionSuggestion.value >> predicate)

        match suggest with
        | Suggest.SuggestSync callback ->
            Suggest.SuggestSync (fun ctx -> callback ctx |> filterValues)
        | Suggest.SuggestAsync callback ->
            Suggest.SuggestAsync (fun ctx ->
                callback ctx |> Async.map filterValues
            )

    let filterAsync (predicate: string -> Async<bool>) (suggest: Suggest): Suggest =
        Suggest.SuggestAsync (fun ctx ->
            suggest
            |> asAsync
            |> fun run -> run ctx
            |> Async.bind (fun suggestions ->
                let checkedSuggestions =
                    suggestions
                    |> List.map (fun suggestion ->
                        predicate suggestion.Value |> Async.map (fun isValid -> suggestion, isValid)
                    )

                Async.Parallel(checkedSuggestions, MaxConcurrentValues)
                |> Async.map (
                    Array.toList
                    >> List.choose (fun (suggestion, isValid) -> if isValid then Some suggestion else None)
                )
            )
        )

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

type CompletionConfig = {
    CommandName: string
    /// Whether the completion command is shown by list/help.
    IsVisible: bool
    /// Command the generated script completes; defaults to the running executable name.
    ExecutableName: string option
}

[<RequireQualifiedAccess>]
module CompletionConfig =
    let defaults = {
        CommandName = "completion"
        IsVisible = true
        ExecutableName = None
    }
