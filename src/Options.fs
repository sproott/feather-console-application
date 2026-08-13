namespace Feather.ConsoleApplication

open Feather.ErrorHandling

type OptionDecorationLevel =
    | Minimal
    | Complete

[<RequireQualifiedAccess>]
type OptionValueDefinition =
    | ValueNone
    | ValueRequired of string
    | ValueOptional of string option
    | ValueIsArray of (string list) option
    | ValueRequiredArray of (string list) option

type OptionName = private OptionName of Name

[<RequireQualifiedAccess>]
module internal OptionName =
    open Feather.ErrorHandling.Result.Operators

    let create = function
        | name when OptionNames.reserved |> List.contains name -> Error (OptionNameError.Reserved name)
        | name ->
            name
            |> Name.create ["-"] ["="; " "] []
            <!> OptionName
            <@> OptionNameError.NameError

    let parseRaw (value: string) =
        value.Split("=") |> Seq.head

    let value (OptionName (Name name)) = name

type internal Letters = char list

[<RequireQualifiedAccess>]
module internal Letters =
    let fromShortcut (shortcut: InputValue): Letters =
        shortcut.TrimStart '-'
        |> Seq.toList

    let toString (letters: char list) =
        letters |> System.String.Concat

    let toShortcut (letter: char) =
        sprintf "-%c" letter

type OptionShortcut = private OptionShortcut of string

[<RequireQualifiedAccess>]
module internal OptionShortcut =
    let create = function
        | String.IsNullOrEmpty -> Error OptionShortcutError.Empty
        | String.Contains ["-"] invalidSubstring -> Error (OptionShortcutError.Contains invalidSubstring)
        | shortcut when shortcut.Length > 1 -> Error (OptionShortcutError.MoreThanSingleLetter shortcut)
        | OptionShortcuts.IsReserved reserved -> Error (OptionShortcutError.Reserved reserved)
        | shortcut -> Ok (OptionShortcut shortcut)

    let value (OptionShortcut shortcut) = shortcut

    /// A shortcut definition may hold several `|`-separated variants (`v|vv|vvv`).
    let variants (OptionShortcut shortcut) = shortcut.Split '|' |> Seq.toList

[<RequireQualifiedAccess>]
type OptionValue =
    | ValueNone
    | ValueRequired of InputValue
    | ValueOptional of InputValue option
    | ValueIsArray of InputValue list
    | ValueRequiredArray of InputValue list

[<RequireQualifiedAccess>]
module OptionValue =
    open Feather.ErrorHandling.Option.Operators

    /// Get Option value as a single string value, it will fail with exception otherwise.
    let value option = function
        | OptionValue.ValueRequired value -> value
        | OptionValue.ValueOptional value ->
            match value with
            | Some value -> value
            | _ -> failwithf "Option \"%s\" does not have a value." option
        | OptionValue.ValueNone -> failwithf "Option \"%s\" has no value." option
        | OptionValue.ValueIsArray _
        | OptionValue.ValueRequiredArray _ -> failwithf "Option \"%s\" has list value." option

    /// Get Option value as string option, if it is not a string (it is list, ...) it will return None.
    let stringValue = function
        | OptionValue.ValueRequired value -> Some value
        | OptionValue.ValueOptional value -> value
        | OptionValue.ValueNone -> None
        | OptionValue.ValueIsArray _
        | OptionValue.ValueRequiredArray _ -> None

    /// Get Option value as int, if it is not an int, it will fail with exception.
    let intValue optionValue =
        optionValue |> stringValue <!> int

    /// Get Option value as int option, if it is not an int, it will return None.
    let tryIntValue optionValue =
        optionValue |> stringValue >>= String.toInt

    /// Get Option value as list, even single or none value will be converted to list.
    let listValue = function
        | OptionValue.ValueRequired value -> [value]
        | OptionValue.ValueOptional value -> value |> Option.toList
        | OptionValue.ValueNone -> []
        | OptionValue.ValueIsArray list
        | OptionValue.ValueRequiredArray list -> list

    /// Check Option value whether is set, it has a value (it means, value is not empty, null or None).
    let isSet = function
        | OptionValue.ValueRequired value -> value |> isNotNull
        | OptionValue.ValueOptional value ->
            match value with
            | Some value -> value |> isNotNull
            | None -> false
        | OptionValue.ValueNone -> true
        | OptionValue.ValueIsArray list
        | OptionValue.ValueRequiredArray list -> list |> List.isEmpty |> not

    let internal parse (value: InputValue) =
        match value.Split ("=", 2) with
        | [| _; value |] -> value
        | _ -> value

    let internal appendListValues newValues = function
        | OptionValue.ValueNone
        | OptionValue.ValueRequired _
        | OptionValue.ValueOptional _ -> failwith "Option is not a list."
        | OptionValue.ValueIsArray values -> values @ newValues |> OptionValue.ValueIsArray
        | OptionValue.ValueRequiredArray values-> values @ newValues |> OptionValue.ValueRequiredArray

type Option = private {
    Name: OptionName
    Shortcut: OptionShortcut option
    Description: string
    Value: OptionValueDefinition
    Suggest: Suggest option
}

type RawOptionDefinition = internal {
    Name: OptionName
    Shortcut: OptionShortcut option
    Description: string
    Value: OptionValueDefinition
    Suggest: Suggest option
}

[<RequireQualifiedAccess>]
module internal RawOptionDefinition =
    open Feather.ErrorHandling.Result.Operators

    let create name shortcut description value =
        result {
            let! name = name |> OptionName.create <@> OptionDefinitionError.OptionNameError
            let! shortcut =
                match shortcut with
                | Some shortcut -> shortcut |> OptionShortcut.create <!> Some <@> OptionDefinitionError.OptionShortcutError
                | _ -> Ok None

            return {
                Name = name
                Shortcut = shortcut
                Description = description
                Value = value
                Suggest = None
            }
        }

    let name ({ Name = name }: RawOptionDefinition) = name
    let shortcut ({ Shortcut = shortcut }: RawOptionDefinition) = shortcut

[<RequireQualifiedAccess>]
module Option =
    //
    // Public create functions for RawOptionDefinition
    //
    let create = RawOptionDefinition.create

    /// Create an Option with required value.
    let required name shortcut description defaultValue =
        create name shortcut description (OptionValueDefinition.ValueRequired defaultValue)

    /// Create an Option with optional value.
    let optional name shortcut description defaultValue =
        create name shortcut description (OptionValueDefinition.ValueOptional defaultValue)

    /// Create an Option without a value.
    let noValue name shortcut description =
        create name shortcut description OptionValueDefinition.ValueNone

    /// Create an Option with list value.
    let optionalArray name shortcut description defaultValues =
        create name shortcut description (OptionValueDefinition.ValueIsArray defaultValues)

    /// Create an Option with required list value (it means at least one item must be there).
    let requiredArray name shortcut description defaultValues =
        create name shortcut description (OptionValueDefinition.ValueRequiredArray defaultValues)

    //
    // Completion helper
    //

    /// Attach completion suggestions to an option definition.
    let suggest (suggestion: Suggest) (def: Result<RawOptionDefinition, OptionDefinitionError>): Result<RawOptionDefinition, OptionDefinitionError> =
        def |> Result.map (fun d -> { d with Suggest = Some suggestion })

    //
    // Internal options functions
    //
    let internal name ({ Name = name }: Option) = name
    let internal nameValue = name >> OptionName.value
    let internal shortcut ({ Shortcut = shortcut }: Option) = shortcut
    let internal suggestion ({ Suggest = suggest }: Option) = suggest

    let internal createApplicationOption name shortcut description value: Option =
        {
            Name = OptionName (Name name)
            Shortcut = Some (OptionShortcut shortcut)
            Description = description
            Value = value
            Suggest = None
        }

    let internal createApplicationOptionWithoutShortcut name description value: Option =
        {
            Name = OptionName (Name name)
            Shortcut = None
            Description = description
            Value = value
            Suggest = None
        }

    let internal isOption (value: InputValue) =
        value.StartsWith "--" && value.Length > 2

    let internal isShortcut (value: InputValue) =
        value.StartsWith "-" && not (value |> isOption) && value.Length > 1

    let internal isOptionOrShortcut value =
        value |> isOption ||
        value |> isShortcut

    let internal (|IsOptionOrShortcut|_|) value =
        value |> isOptionOrShortcut |> Bool.toOption

    let internal containsValue (value: InputValue) =
        value.Contains "="

    let internal isMatchingOption (value: InputValue) name =
        if value |> isOption then value.TrimStart '-' = (name |> OptionName.value)
        else false

    let internal isMatchingShortcut (value: InputValue) (shortcut: OptionShortcut option) =
        if value |> isShortcut then
            match shortcut with
            | Some shortcut -> shortcut |> OptionShortcut.variants |> List.contains (value.TrimStart '-')
            | None -> false
        else false

    let internal isMatching (option: Option) (value: InputValue) =
        option.Name |> isMatchingOption value ||
        option.Shortcut |> isMatchingShortcut value

    let internal usage (option: Option) =
        let shortcut =
            match option.Shortcut with
            | Some (OptionShortcut shortcut) -> sprintf "-%s|" shortcut
            | _ -> ""

        let optionName = option.Name |> OptionName.value

        let value =
            let nameUpper = optionName |> String.toUpper

            match option.Value with
            | OptionValueDefinition.ValueNone -> ""
            | OptionValueDefinition.ValueRequired _
            | OptionValueDefinition.ValueRequiredArray _ -> sprintf " %s" nameUpper
            | OptionValueDefinition.ValueOptional _
            | OptionValueDefinition.ValueIsArray _ -> sprintf " [%s]" nameUpper

        sprintf "[%s--%s%s]" shortcut optionName value

type internal OptionsDefinitions = Option list

[<RequireQualifiedAccess>]
module internal OptionsDefinitions =
    let help = Option.createApplicationOption OptionNames.Help OptionShortcuts.Help "Display this help message" OptionValueDefinition.ValueNone
    let version = Option.createApplicationOption OptionNames.Version OptionShortcuts.Version "Display this application version" OptionValueDefinition.ValueNone
    let noInteraction = Option.createApplicationOption OptionNames.NoInteraction OptionShortcuts.NoInteraction "Do not ask any interactive question" OptionValueDefinition.ValueNone
    let quiet = Option.createApplicationOption OptionNames.Quiet OptionShortcuts.Quiet "Do not output any message" OptionValueDefinition.ValueNone
    let verbose = Option.createApplicationOption OptionNames.Verbose OptionShortcuts.Verbose "Increase the verbosity of messages" OptionValueDefinition.ValueNone
    let noProgress = Option.createApplicationOptionWithoutShortcut OptionNames.NoProgress "Whether to disable all progress bars" OptionValueDefinition.ValueNone
    let noAnsi = Option.createApplicationOptionWithoutShortcut OptionNames.NoAnsi "Whether to disable all markup with ansi formatting" OptionValueDefinition.ValueNone

    let (|HasDefinedOption|_|) option (options: OptionsDefinitions) =
        options |> List.tryFind (Option.nameValue >> (=) option)

    let (|IsDefinedOption|_|) (options: OptionsDefinitions) (value: InputValue) =
        if value |> Option.isOption then
            let optionName = value |> OptionName.parseRaw

            options
            |> List.tryFind (Option.name >> Option.isMatchingOption optionName)
        else None

    let (|IsShortcut|_|) (value: InputValue) =
        value |> Option.isShortcut |> Bool.toOption

    let (|IsDefinedShortcut|_|) (options: OptionsDefinitions) (value: InputValue) =
        if value |> Option.isShortcut then
            options
            |> List.tryFind (Option.shortcut >> Option.isMatchingShortcut value)
        else None

    let usage (options: OptionsDefinitions) = function
        | Minimal -> "[options]"
        | Complete ->
            options
            |> List.map Option.usage
            |> String.concat " "

    let format (options: OptionsDefinitions) =
        options
        |> List.map (fun option ->
            let shortcut =
                match option.Shortcut with
                | Some shortcut -> shortcut |> OptionShortcut.value |> sprintf "-%s, "
                | _ -> "    "

            let optionName = option.Name |> OptionName.value

            let (optionValue, allowMultiple, defaultValue) =
                let nameUpper = optionName |> String.toUpper

                match option.Value with
                | OptionValueDefinition.ValueNone -> ("", false, None)
                | OptionValueDefinition.ValueRequired defaultValue -> (sprintf "=%s" nameUpper, false, defaultValue |> sprintf "%A" |> Some)
                | OptionValueDefinition.ValueOptional defaultValue -> (sprintf "[=%s]" nameUpper, false, defaultValue |> Option.map (sprintf "%A"))
                | OptionValueDefinition.ValueIsArray defaultValue -> (sprintf "[=%s]" nameUpper, true, defaultValue |> Option.map (sprintf "%A"))
                | OptionValueDefinition.ValueRequiredArray defaultValue -> (sprintf "=%s" nameUpper, true, defaultValue |> Option.map (sprintf "%A"))

            let description =
                [
                    yield option.Description

                    yield!
                        defaultValue
                        |> Option.map (sprintf "<c:dark-yellow>[default: %s]</c>")
                        |> Option.toList

                    if allowMultiple then
                        yield "<c:blue>(multiple values allowed)</c>"
                ]
                |> String.concat " "

            [ sprintf "<c:green>%s--%s%s</c>" shortcut optionName optionValue; description ]
        )

    let private fromRaw (option: RawOptionDefinition): Option =
        {
            Name = option.Name
            Shortcut = option.Shortcut
            Description = option.Description
            Value = option.Value
            Suggest = option.Suggest
        }

    let validate (options: RawOptionDefinition list) =
        let assertUniqueOptions options =
            match options |> List.getDuplicatesBy RawOptionDefinition.name with
            | [] -> Ok ()
            | notUnique :: _ -> Error (notUnique |> OptionName.value |> OptionDefinitionError.OptionAlreadyExists)

        let assertUniqueShortcuts options =
            match options |> List.getDuplicatesBy RawOptionDefinition.shortcut with
            | []
            | None :: _ -> Ok ()
            | (Some (OptionShortcut notUnique)) :: _ -> Error (OptionDefinitionError.OptionShortcutAlreadyExists notUnique)

        result {
            let! _ =
                [
                    options |> assertUniqueOptions
                    options |> assertUniqueShortcuts
                ]
                |> Validation.ofResults

            return options |> List.map fromRaw
        }

type internal Options = Map<string, OptionValue>

[<RequireQualifiedAccess>]
module internal Options =
    open Feather.ErrorHandling.Option.Operators
    open Feather.ErrorHandling.Result.Operators

    let (|HasOption|_|) (option: string) (options: Options) =
        options |> Map.tryFind option

    let (|HasOptionName|_|) option (options: Options) =
        options |> Map.tryFind (option |> OptionName.value)

    let (|IsSetOption|_|) option = function
        | HasOption option value when value |> OptionValue.isSet -> Some value
        | _ -> None

    let parseValue option (parsedOptions: Options) (rawArgs: InputValue list): Result<Options * InputValue list, OptionsError> =
        let ({ Name = optionName; Value = optionValue }: Option) = option
        let optionName = optionName |> OptionName.value

        match optionValue with
        | OptionValueDefinition.ValueNone ->
            Ok (parsedOptions.Add(optionName, OptionValue.ValueNone), rawArgs)

        | OptionValueDefinition.ValueRequired _ ->
            match rawArgs with
            | [] -> Error (OptionsError.RequiredValueNotSet optionName)
            | value :: _ when value |> Option.isOptionOrShortcut -> Error (OptionsError.RequiredValueNotSet optionName)
            | value :: rawArgs -> Ok (parsedOptions.Add(optionName, OptionValue.ValueRequired value), rawArgs)

        | OptionValueDefinition.ValueOptional defaultValue ->
            match rawArgs with
            | [] -> (defaultValue, rawArgs)
            | value :: _ when value |> Option.isOptionOrShortcut -> (defaultValue, rawArgs)
            | value :: rawArgs -> (Some value, rawArgs)
            |> fun (value, rawArgs) ->
                Ok (parsedOptions.Add(optionName, OptionValue.ValueOptional value), rawArgs)

        | OptionValueDefinition.ValueIsArray defaultValues ->
            match rawArgs with
            | [] -> (defaultValues <?=> [], [])
            | value :: _ when value |> Option.isOptionOrShortcut -> (defaultValues <?=> [], rawArgs)
            | value :: rawArgs -> ([ value ], rawArgs)
            |> fun (values, rawArgs) ->
                let values =
                    match parsedOptions with
                    | HasOption optionName value -> value |> OptionValue.appendListValues values
                    | _ -> OptionValue.ValueIsArray values

                Ok (parsedOptions.Add(optionName, values), rawArgs)

        | OptionValueDefinition.ValueRequiredArray _ ->
            match rawArgs with
            | [] -> Error (OptionsError.RequiredValueNotSet optionName)
            | value :: _ when value |> Option.isOptionOrShortcut -> Error (OptionsError.RequiredValueNotSet optionName)
            | value :: rawArgs ->
                let values =
                    match parsedOptions with
                    | HasOption optionName values -> values |> OptionValue.appendListValues [ value ]
                    | _ -> OptionValue.ValueRequiredArray [ value ]

                Ok (parsedOptions.Add(optionName, values), rawArgs)

    type private ParsedShortcutResult = {
        Parsed: Options
        PartiallyParsedLetters: Letters
        RestOfArgs: InputValue list
    }

    let private parseShortcutValue option (parsedOptions: Options) (rawArgs: InputValue list) (letters: Letters): Result<ParsedShortcutResult, OptionsError> =
        let ({ Name = optionName; Value = optionValue }: Option) = option
        let optionName = optionName |> OptionName.value

        match optionValue with
        | OptionValueDefinition.ValueNone ->
            Ok {
                Parsed = parsedOptions.Add(optionName, OptionValue.ValueNone)
                PartiallyParsedLetters = letters
                RestOfArgs = rawArgs
            }

        | OptionValueDefinition.ValueRequired _ ->
            match letters, rawArgs with
            | [], [] -> Error (OptionsError.RequiredValueNotSet optionName)
            | [], value :: _ when value |> Option.isOptionOrShortcut -> Error (OptionsError.RequiredValueNotSet optionName)
            | [], value :: rawArgs -> Ok (value, rawArgs)
            | letters, rawArgs -> Ok (letters |> Letters.toString, rawArgs)
            <!> fun (value, rawArgs) ->
                {
                    Parsed = parsedOptions.Add(optionName, OptionValue.ValueRequired value)
                    PartiallyParsedLetters = []
                    RestOfArgs = rawArgs
                }

        | OptionValueDefinition.ValueOptional defaultValue ->
            match letters, rawArgs with
            | [], [] -> defaultValue, rawArgs
            | [], value :: _ when value |> Option.isOptionOrShortcut -> defaultValue, rawArgs
            | [], value :: rawArgs -> Some value, rawArgs
            | letters, rawArgs -> Some (letters |> Letters.toString), rawArgs
            |> fun (value, rawArgs) ->
                Ok {
                    Parsed = parsedOptions.Add(optionName, OptionValue.ValueOptional value)
                    PartiallyParsedLetters = []
                    RestOfArgs = rawArgs
                }

        | OptionValueDefinition.ValueIsArray defaultValues ->
            let defaultValues = defaultValues <?=> []

            match letters, rawArgs with
            | [], [] -> defaultValues, []
            | [], value :: _ when value |> Option.isOptionOrShortcut -> defaultValues, rawArgs
            | [], value :: rawArgs -> [ value ], rawArgs
            | letters, rawArgs -> [ letters |> Letters.toString ], rawArgs
            |> fun (values, rawArgs) ->
                let values =
                    match parsedOptions with
                    | HasOption optionName value -> value |> OptionValue.appendListValues values
                    | _ -> OptionValue.ValueIsArray values

                Ok {
                    Parsed = parsedOptions.Add(optionName, values)
                    PartiallyParsedLetters = []
                    RestOfArgs = rawArgs
                }

        | OptionValueDefinition.ValueRequiredArray _ ->
            match letters, rawArgs with
            | [], [] -> Error (OptionsError.RequiredValueNotSet optionName)
            | [], value :: _ when value |> Option.isOptionOrShortcut -> Error (OptionsError.RequiredValueNotSet optionName)
            | [], value :: rawArgs -> Ok ([ value ], rawArgs)
            | letters, rawArgs -> Ok ([ letters |> Letters.toString ], rawArgs)
            <!> fun (value, rawArgs) ->
                let values =
                    match parsedOptions with
                    | HasOption optionName values -> values |> OptionValue.appendListValues value
                    | _ -> OptionValue.ValueRequiredArray value

                {
                    Parsed = parsedOptions.Add(optionName, values)
                    PartiallyParsedLetters = []
                    RestOfArgs = rawArgs
                }

    [<TailCall>]
    let rec parseShortcut optionDefinitions (parsed: Options) (rawArgs: InputValue list) = function
        | [] -> Ok (parsed, rawArgs)
        | letter :: rest ->
            result {
                let! { Parsed = parsed; PartiallyParsedLetters = rest; RestOfArgs = rawArgs } =
                    match letter |> Letters.toShortcut with
                    | OptionsDefinitions.IsDefinedShortcut optionDefinitions option ->
                        rest
                        |> parseShortcutValue option parsed rawArgs
                    | undefinedShortcut ->
                        Error (OptionsError.UndefinedOption undefinedShortcut)

                return! rest |> parseShortcut optionDefinitions parsed rawArgs
            }

    let private setDefault optionName optionValue (options: Options) = function
        | Some defaultValue -> options.Add(optionName |> OptionName.value, optionValue defaultValue)
        | _ -> options

    [<TailCall>]
    let rec prepareOptionsDefaults (options: Options) = function
        | [] -> options
        | (definition: Option) :: definitions ->
            let options =
                match options with
                | HasOptionName definition.Name _ -> options
                | _ ->
                    match definition.Value with
                    | OptionValueDefinition.ValueNone -> options
                    | OptionValueDefinition.ValueRequired defaultValue -> Some defaultValue |> setDefault definition.Name OptionValue.ValueRequired options
                    | OptionValueDefinition.ValueOptional defaultValue -> defaultValue |> setDefault definition.Name (Some >> OptionValue.ValueOptional) options
                    | OptionValueDefinition.ValueIsArray defaultValues -> defaultValues |> setDefault definition.Name OptionValue.ValueIsArray options
                    | OptionValueDefinition.ValueRequiredArray defaultValues -> defaultValues |> setDefault definition.Name OptionValue.ValueRequiredArray options

            definitions
            |> prepareOptionsDefaults options
