namespace Feather.ConsoleApplication

open Feather.ErrorHandling

[<RequireQualifiedAccess>]
type ArgumentValueDefinition =
    | Required
    | Optional of InputValue option
    | RequiredArray
    | Array of (InputValue list) option

[<RequireQualifiedAccess>]
module internal ArgumentValueDefinition =
    let isRequired = function
        | ArgumentValueDefinition.Required
        | ArgumentValueDefinition.RequiredArray -> true
        | _ -> false

type ArgumentName = private ArgumentName of Name

[<RequireQualifiedAccess>]
module internal ArgumentName =
    open Feather.ErrorHandling.Result.Operators

    let create = function
        | ArgumentNames.Command -> Error (ArgumentNameError.Reserved ArgumentNames.Command)
        | name ->
            name
            |> Name.create ["-"] [" "] []
            <!> ArgumentName
            <@> ArgumentNameError.NameError

    let value (ArgumentName (Name name)) = name

type Argument = private {
    ArgumentName: ArgumentName
    Description: string
    Value: ArgumentValueDefinition
    Suggest: Suggest option
}

type RawArgumentDefinition = internal {
    ArgumentName: ArgumentName
    Description: string
    Value: ArgumentValueDefinition
    Suggest: Suggest option
}

[<RequireQualifiedAccess>]
module internal RawArgumentDefinition =
    open Feather.ErrorHandling.Result.Operators

    let name ({ ArgumentName = name }: RawArgumentDefinition) = name

    let create name description value: Result<RawArgumentDefinition, ArgumentDefinitionError> =
        result {
            let! name =
                name
                |> ArgumentName.create <@> ArgumentDefinitionError.ArgumentNameError

            return {
                ArgumentName = name
                Description = description
                Value = value
                Suggest = None
            }
        }

[<RequireQualifiedAccess>]
module Argument =
    //
    // Public create functions for RawArgumentDefinition
    //
    let create = RawArgumentDefinition.create

    /// Create a required argument.
    let required name description =
        create name description ArgumentValueDefinition.Required

    /// Create an optional argument.
    let optional name description defaultValue =
        create name description (ArgumentValueDefinition.Optional defaultValue)

    /// Create an optional array argument.
    let requiredArray name description =
        create name description ArgumentValueDefinition.RequiredArray

    /// Create a required array argument.
    let optionalArray name description defaultValue =
        create name description (ArgumentValueDefinition.Array defaultValue)

    /// Attach completion suggestions to an argument definition.
    let suggest (suggestion: Suggest) (def: Result<RawArgumentDefinition, ArgumentDefinitionError>): Result<RawArgumentDefinition, ArgumentDefinitionError> =
        def |> Result.map (fun d -> { d with Suggest = Some suggestion })

    //
    // Internal Argument functions
    //
    let internal name ({ ArgumentName = name }: Argument) = name
    let internal nameValue = name >> ArgumentName.value
    let internal valueDefinition ({ Value = value }: Argument) = value
    let internal suggestion ({ Suggest = suggest }: Argument) = suggest

    let internal usage ({ ArgumentName = name; Value = definition }: Argument) =
        name
        |> ArgumentName.value
        |> match definition with
            | ArgumentValueDefinition.Required -> sprintf "<%s>"
            | ArgumentValueDefinition.Optional _ -> sprintf "[<%s>]"
            | ArgumentValueDefinition.RequiredArray -> sprintf "<%s>..."
            | ArgumentValueDefinition.Array _ -> sprintf "[<%s>...]"

    let internal format ({ ArgumentName = name; Description = description; Value = definition }: Argument) =
        let defaultValue =
            match definition with
            | ArgumentValueDefinition.Optional (Some defaultValue) -> sprintf " <c:dark-yellow>[default: %A]</c>" defaultValue
            | ArgumentValueDefinition.Array (Some defaultValue) -> sprintf " <c:dark-yellow>[default: %A]</c>" defaultValue
            | _ -> ""

        [ sprintf "<c:green>%s</c>" (name |> ArgumentName.value); sprintf "%s%s" description defaultValue ]

[<RequireQualifiedAccess>]
type ArgumentValue =
    | Required of InputValue
    | Optional of InputValue option
    | Array of InputValue list
    | RequiredArray of NotEmptyList<InputValue>

[<RequireQualifiedAccess>]
module ArgumentValue =
    open Feather.ErrorHandling.Option.Operators

    /// Get Argument value as a single string value, it will fail with exception otherwise.
    let value argument = function
        | ArgumentValue.Required value -> value
        | ArgumentValue.Optional value ->
            match value with
            | Some value -> value
            | _ -> failwithf "Argument \"%s\" has no value." argument
        | ArgumentValue.Array _
        | ArgumentValue.RequiredArray _ -> failwithf "Argument \"%s\" has list value." argument

    /// Get Argument value as string option, if it is not a string (it is list, ...) it will return None.
    let stringValue = function
        | ArgumentValue.Required value -> Some value
        | ArgumentValue.Optional value -> value
        | ArgumentValue.Array _
        | ArgumentValue.RequiredArray _ -> None

    /// Get Argument value as int, if it is not an int, it will fail with exception.
    let intValue argumentValue =
        argumentValue |> stringValue <!> int

    /// Get Argument value as int option, if it is not an int, it will return None.
    let tryIntValue argumentValue =
        argumentValue |> stringValue >>= String.toInt

    /// Get Argument value as list, even single value will be converted to list.
    let listValue = function
        | ArgumentValue.Required value -> [value]
        | ArgumentValue.Array value -> value
        | ArgumentValue.RequiredArray value -> value |> NotEmptyList.values
        | ArgumentValue.Optional value -> value |> Option.toList

    let internal appendListValues newValues = function
        | ArgumentValue.Required _
        | ArgumentValue.Optional _ -> failwith "Argument is not a list."
        | ArgumentValue.Array values -> values @ newValues |> ArgumentValue.Array
        | ArgumentValue.RequiredArray value ->
            value
            |> NotEmptyList.append newValues
            |> ArgumentValue.RequiredArray

    let internal isSet = function
        | ArgumentValue.Required value -> value |> String.isNullOrEmpty |> not
        | ArgumentValue.Optional value ->
            match value with
            | Some value -> value |> isNotNull
            | _ -> false
        | ArgumentValue.Array values -> values |> List.isEmpty |> not
        | ArgumentValue.RequiredArray _ -> true

type internal RawArgumentsDefinitions = RawArgumentDefinition list
type internal ArgumentsDefinitions = Argument list
type internal UnfilledArgumentDefinitions = ArgumentsDefinitions

[<RequireQualifiedAccess>]
module internal ArgumentsDefinitions =
    let private fromRaw (raw: RawArgumentDefinition): Argument =
        {
            ArgumentName = raw.ArgumentName
            Description = raw.Description
            Value = raw.Value
            Suggest = raw.Suggest
        }

    let validate (arguments: RawArgumentDefinition list) =
        let assertUnique arguments =
            match arguments |> List.getDuplicatesBy RawArgumentDefinition.name with
            | [] -> Ok ()
            | notUnique :: _ -> Error (notUnique |> ArgumentName.value |> ArgumentDefinitionError.ArgumentAlreadyExists)

        let rec assertArrayArgumentIsLast = function
            | [] -> Ok ()
            | (argument: RawArgumentDefinition) :: arguments ->
                match argument.Value with
                | ArgumentValueDefinition.Array _
                | ArgumentValueDefinition.RequiredArray ->
                    match arguments with
                    | [] -> Ok ()
                    | _ -> Error ArgumentDefinitionError.ArgumentAfterArrayArgument
                | _ -> assertArrayArgumentIsLast arguments

        let rec assertOptionalArgumentsAfterRequired hasOptional = function
            | [] -> Ok ()
            | (argument: RawArgumentDefinition) :: arguments ->
                match argument.Value with
                | ArgumentValueDefinition.Required
                | ArgumentValueDefinition.RequiredArray when hasOptional -> Error ArgumentDefinitionError.RequiredArgumentAfterOptional
                | ArgumentValueDefinition.Optional _
                | ArgumentValueDefinition.Array _ -> assertOptionalArgumentsAfterRequired true arguments
                | _ -> assertOptionalArgumentsAfterRequired hasOptional arguments

        result {
            let! _ =
                [
                    arguments |> assertUnique
                    arguments |> assertArrayArgumentIsLast
                    arguments |> assertOptionalArgumentsAfterRequired false
                ]
                |> Validation.ofResults

            return arguments |> List.map fromRaw
        }

    let (|HasDefinedArgument|_|) argument (arguments: ArgumentsDefinitions) =
        arguments |> List.tryFind (Argument.nameValue >> (=) argument)

    let formatRequired (arguments: ArgumentsDefinitions) =
        arguments |> List.map Argument.nameValue

type internal Arguments = Map<string, ArgumentValue>

[<RequireQualifiedAccess>]
module internal Arguments =
    open Feather.ErrorHandling.Option.Operators
    open Feather.ErrorHandling.Result.Operators

    [<Literal>]
    let Separator = "--"

    let (|HasArgument|_|) name (arguments: Arguments) =
        arguments |> Map.tryFind name

    let (|IsSetArgument|_|) name = function
        | HasArgument name value when value |> ArgumentValue.isSet -> Some value
        | _ -> None

    type private ParseArrayType =
        | ConsumeAllNoMatterWhat
        | ConsumeSinglePartially

    type private ParsedResult = {
        ParsedArguments: Arguments
        PartialyParsedDefinition: Argument option
        RestOfArgs: InputValue list
    }

    let private parseValue parseArrayType (definition: Argument) (parsed: Arguments) rawArgs: Result<ParsedResult, ArgumentsError> =
        let argumentName = definition.ArgumentName |> ArgumentName.value

        match definition.Value with
        | ArgumentValueDefinition.Required ->
            match rawArgs with
            | [] -> Error (ArgumentsError.NotEnoughArguments argumentName)
            | value :: rawArgs ->
                Ok {
                    ParsedArguments = parsed.Add(argumentName, ArgumentValue.Required value)
                    PartialyParsedDefinition = None
                    RestOfArgs = rawArgs
                }

        | ArgumentValueDefinition.Optional defaultValue ->
            match rawArgs with
            | [] -> (defaultValue, rawArgs)
            | value :: rawArgs -> (Some value, rawArgs)
            |> fun (value, rawArgs) ->
                Ok {
                    ParsedArguments = parsed.Add(argumentName, ArgumentValue.Optional value)
                    PartialyParsedDefinition = None
                    RestOfArgs = rawArgs
                }

        | ArgumentValueDefinition.Array defaultValues ->
            match parseArrayType with
            | ConsumeAllNoMatterWhat ->
                let arguments =
                    match rawArgs, defaultValues with
                    | [], None -> []
                    | [], Some defaultArguments -> defaultArguments
                    | arguments, _ -> arguments

                let values =
                    match parsed with
                    | HasArgument argumentName value -> value |> ArgumentValue.appendListValues arguments
                    | _ -> arguments |> ArgumentValue.Array

                Ok {
                    ParsedArguments = parsed.Add(argumentName, values)
                    PartialyParsedDefinition = None
                    RestOfArgs = []
                }
            | ConsumeSinglePartially ->
                let values =
                    match parsed with
                    | HasArgument argumentName value -> value |> ArgumentValue.appendListValues rawArgs
                    | _ -> rawArgs |> ArgumentValue.Array

                Ok {
                    ParsedArguments = parsed.Add(argumentName, values)
                    PartialyParsedDefinition = Some definition
                    RestOfArgs = []
                }

        | ArgumentValueDefinition.RequiredArray ->
            match parseArrayType with
            | ConsumeAllNoMatterWhat ->
                result {
                    let! values =
                        match parsed with
                        | HasArgument argumentName value -> value |> ArgumentValue.appendListValues rawArgs |> Ok
                        | _ ->
                            rawArgs
                            |> NotEmptyList.create
                            <!> ArgumentValue.RequiredArray
                            <@> (function
                                | NotEmptyListError.NoValues -> ArgumentsError.NotEnoughArguments argumentName
                            )

                    return {
                        ParsedArguments = parsed.Add(argumentName, values)
                        PartialyParsedDefinition = None
                        RestOfArgs = []
                    }
                }
            | ConsumeSinglePartially ->
                let values =
                    match parsed with
                    | HasArgument argumentName value -> value |> ArgumentValue.appendListValues rawArgs
                    | _ -> rawArgs |> NotEmptyList.createPartial |> ArgumentValue.RequiredArray

                Ok {
                    ParsedArguments = parsed.Add(argumentName, values)
                    PartialyParsedDefinition = Some definition
                    RestOfArgs = []
                }

    let parse output (parsed: Arguments) (definitions: ArgumentsDefinitions) (rawArguments: InputValue list): Result<Arguments * UnfilledArgumentDefinitions, ArgumentsError> =
        debug output <| sprintf "Parse arguments after -- %A\n%-*A" rawArguments 58 (* 58 is lenght of debug line *) (definitions |> List.map Argument.nameValue)

        let rec parseArgument rawArgs (parsed: Arguments) unfilledDefinitions =
            match rawArgs with
            | [] -> Ok (parsed, unfilledDefinitions)
            | _ ->
                match unfilledDefinitions with
                | [] -> Error (ArgumentsError.TooManyArguments (definitions |> ArgumentsDefinitions.formatRequired))
                | definition :: definitions ->
                    result {
                        let! parsedResult =
                            rawArgs
                            |> parseValue ConsumeAllNoMatterWhat definition parsed

                        let definitions =
                            match parsedResult.PartialyParsedDefinition with
                            | Some definition -> definition :: definitions
                            | _ -> definitions

                        return!
                            definitions
                            |> parseArgument parsedResult.RestOfArgs parsedResult.ParsedArguments
                    }

        definitions |> parseArgument rawArguments parsed

    let parseArgument arguments definedArguments (definitionsToParse: ArgumentsDefinitions) (rawArgument: InputValue) =
        match definitionsToParse with
        | [] -> Error (ArgumentsError.TooManyArguments (definedArguments |> ArgumentsDefinitions.formatRequired))
        | definition :: definitions ->
            [ rawArgument ]
            |> parseValue ConsumeSinglePartially definition arguments
            |> Result.map (fun { ParsedArguments = arguments; PartialyParsedDefinition = definition } ->
                let definitions =
                    match definition with
                    | Some definition -> definition :: definitions
                    | _ -> definitions

                arguments, definitions
            )

    let prepareMissingArguments (definitions: ArgumentsDefinitions) (parsed: Arguments): Result<Arguments, ArgumentsError> =
        let rec prepare (arguments: Arguments) = function
            | [] -> Ok arguments
            | definition :: definitions ->
                result {
                    let argumentName = definition |> Argument.nameValue

                    let! value =
                        match definition.Value, arguments with
                        | ArgumentValueDefinition.RequiredArray, HasArgument argumentName value ->
                            result {
                                let! values =
                                    match value with
                                    | ArgumentValue.RequiredArray values -> NotEmptyList.complete values
                                    | _ -> failwith "Logic error: Only RequiredArray argument value could be here."
                                    <@> (function
                                        | NotEmptyListError.NoValues -> ArgumentsError.NotEnoughArguments argumentName
                                    )

                                return ArgumentValue.RequiredArray values
                            }
                        | ArgumentValueDefinition.Required, HasArgument argumentName value when value |> ArgumentValue.isSet -> Ok value

                        | ArgumentValueDefinition.Required, _
                        | ArgumentValueDefinition.RequiredArray, _ ->
                            Error <| ArgumentsError.NotEnoughArguments argumentName

                        | ArgumentValueDefinition.Optional defaultValue, _ ->
                            Ok <| ArgumentValue.Optional defaultValue

                        | ArgumentValueDefinition.Array defaultValues, HasArgument argumentName value ->
                            match value |> ArgumentValue.listValue, defaultValues with
                            | [], Some defaultValues -> defaultValues
                            | [], None -> []
                            | values, _ -> values
                            |> ArgumentValue.Array
                            |> Ok

                        | ArgumentValueDefinition.Array defaultValues, _ ->
                            Ok <| ArgumentValue.Array (defaultValues <?=> [])

                    return! prepare (arguments.Add(argumentName, value)) definitions
                }

        definitions |> prepare parsed
