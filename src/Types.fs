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
    // Worklist recursion keeps the traversal tail-recursive, which the lint gate requires of every `rec`.
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
