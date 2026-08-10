namespace Feather.ConsoleApplication

open Feather.ErrorHandling

type internal ParsedInput = {
    Arguments: Arguments
    Options: Options
    UnfilledArgumentDefinitions: UnfilledArgumentDefinitions
}

[<RequireQualifiedAccess>]
module internal ParsedInput =
    let internal empty = {
        Arguments = Map.empty
        Options = Map.empty
        UnfilledArgumentDefinitions = []
    }

type Input = {
    Arguments: Arguments
    Options: Options
    ArgumentDefinitions: ArgumentsDefinitions
    OptionDefinitions: OptionsDefinitions
}

[<RequireQualifiedAccess>]
module Input =
    open Feather.ErrorHandling.Result.Operators

    let internal empty = {
        Arguments = Map.empty
        Options = Map.empty
        ArgumentDefinitions = []
        OptionDefinitions = []
    }

    [<TailCall>]
    let rec internal parse output allArgumentDefinitions definitionsToParse (input: ParsedInput) (args: InputValue list) =
        let (optionDefinitions: OptionsDefinitions, argumentDefinitions: ArgumentsDefinitions) = definitionsToParse
        let debug = debug output

        match args with
        | [] -> Ok { input with UnfilledArgumentDefinitions = argumentDefinitions }
        | Arguments.Separator :: rawArguments ->
            result {
                let! (arguments, unfilledArgumentDefinitions) =
                    rawArguments
                    |> Arguments.parse output input.Arguments argumentDefinitions <@> InputError.ArgumentsError

                return { input with Arguments = arguments; UnfilledArgumentDefinitions = unfilledArgumentDefinitions }
            }
        | rawArg :: rawArgs ->
            result {
                let! (parsedInput, definitionsToParse, rawArgs) =
                    match rawArg with
                    | OptionsDefinitions.IsDefinedOption optionDefinitions option ->
                        debug <| sprintf "Parse option \"%s\" value for %A" rawArg option.Name

                        result {
                            let! options, rawArgs =
                                if rawArg |> Option.containsValue
                                    then (rawArg |> OptionValue.parse) :: rawArgs
                                    else rawArgs
                                |> Options.parseValue option input.Options <@> InputError.OptionsError

                            return { input with Options = options }, definitionsToParse, rawArgs
                        }

                    | OptionsDefinitions.IsShortcut ->
                        debug <| sprintf "Parse shortcut(s) \"%s\"" rawArg

                        result {
                            let! options, rawArgs =
                                rawArg
                                |> Letters.fromShortcut
                                |> Options.parseShortcut optionDefinitions input.Options rawArgs <@> InputError.OptionsError

                            return { input with Options = options }, definitionsToParse, rawArgs
                        }

                    | Option.IsOptionOrShortcut ->
                        Error <| InputError.OptionsError (OptionsError.UndefinedOption rawArg)

                    | rawArgument ->
                        debug <| sprintf "Parse argument \"%s\"" rawArgument

                        result {
                            let! (arguments, argumentDefinitions) =
                                rawArgument
                                |> Arguments.parseArgument input.Arguments allArgumentDefinitions argumentDefinitions <@> InputError.ArgumentsError

                            return { input with Arguments = arguments }, (optionDefinitions, argumentDefinitions), rawArgs
                        }

                return!
                    rawArgs
                    |> parse output allArgumentDefinitions definitionsToParse parsedInput
            }

    let internal prepareUnfilledArguments (unfilledArgumentDefinitions: UnfilledArgumentDefinitions) (input: Input) =
        match unfilledArgumentDefinitions with
        | [] -> Ok input
        | unfilledDefinitions ->
            result {
                let! arguments =
                    input.Arguments
                    |> Arguments.prepareMissingArguments unfilledDefinitions <@> InputError.ArgumentsError

                return { input with Arguments = arguments }
            }

    //
    // Matching & Getters
    //

    [<RequireQualifiedAccess>]
    module Option =
        /// Check whether option is defined.
        let (|IsDefined|_|) option input =
            match input.OptionDefinitions with
            | OptionsDefinitions.HasDefinedOption option _ -> Some ()
            | _ -> None

        /// Check whether Input.Options contains an option, fail on exception when option is not defined.
        let (|Has|_|) option = function
            | IsDefined option as input ->
                match input.Options with
                | Options.HasOption option value -> Some value
                | _ -> None
            | _ -> failwithf "The \"--%s\" option does not exists." option

        /// Check whether Input.Options contains option name and the value is set or fail with exception when option is not defined.
        let (|IsSet|_|) option = function
            | Has option value when value |> OptionValue.isSet -> Some value
            | _ -> None

        /// Check whether Input.Options contains option name or fail with exception when option is not defined.
        /// Access the value directly in matching.
        let (|Value|_|) option = function
            | Has option value -> Some (value |> OptionValue.value option)
            | _ -> None

        /// Check whether Input.Options contains option name or fail with exception when option is not defined.
        /// Access the value directly in matching.
        let (|OptionalValue|_|) option = function
            | Has option value -> value |> OptionValue.stringValue
            | _ -> None

        /// Check whether Input.Options contains option name or fail with exception when option is not defined.
        /// Access the value as list directly in matching.
        let (|ListValue|_|) option = function
            | Has option value -> Some (value |> OptionValue.listValue)
            | _ -> None

        /// Checks whether option value is set or fail with exception when option is not defined.
        let isValueSet option = function
            | IsSet option _ -> true
            | _ -> false

        /// Checks whether input has an option or fail with exception when option is not defined.
        /// This is helpful for a Option.noValue.
        let has option = function
            | Has option _ -> true
            | _ -> false

        /// Try to find an OptionValue in Input.Options, fail on exception when option is not defined.
        let tryGet option = function
            | Has option value -> Some value
            | _ -> None

        /// Get an OptionValue from Input.Options by option name, fail on exception when option is not defined or presented.
        let get option = function
            | Has option value -> value
            | _ -> failwithf "The \"--%s\" option does not have a value." option

        /// Get option value or fail with exception when option is not defined or presented.
        /// This function should be used with Options with Required values.
        let value option input =
            input |> get option |> OptionValue.value option

        /// Get option value as string or fail with exception when option is not defined or presented.
        let asString option input =
            input |> get option |> OptionValue.stringValue

        /// Get option value as int or fail with exception when option is not defined or presented.
        let asInt option input =
            input |> get option |> OptionValue.intValue

        /// Get option value as list or fail with exception when option is not defined or presented.
        let asList option input =
            input |> get option |> OptionValue.listValue

        /// Try to parse option value as int or fail with exception when option is not defined or presented.
        let tryAsInt option input =
            input |> get option |> OptionValue.tryIntValue

        /// Set a value for an option or fail with exception when option is not defined.
        /// You can set only a single string value (even for option with array value).
        /// If you want to set more then one value for array option, use `Input.setOptionListValue` instead.
        let set (input: Input) (option: string) value =
            match input.OptionDefinitions with
            | OptionsDefinitions.HasDefinedOption option definition ->
                let value =
                    match definition.Value with
                    | OptionValueDefinition.ValueNone -> failwithf "The \"%s\" option does not accept any value. (Input.Option.set)" option
                    | OptionValueDefinition.ValueRequired _ ->
                        match value with
                        | String.IsNullOrEmpty -> failwithf "The \"%s\" option does not accept an empty value. (Input.Option.set required value)" option
                        | value -> OptionValue.ValueRequired value
                    | OptionValueDefinition.ValueOptional _ ->
                        match value with
                        | null -> OptionValue.ValueOptional None
                        | value -> OptionValue.ValueOptional (Some value)
                    | OptionValueDefinition.ValueIsArray _ -> OptionValue.ValueIsArray [value]
                    | OptionValueDefinition.ValueRequiredArray _ ->
                        match value with
                        | String.IsNullOrEmpty -> failwithf "The \"%s\" option does not accept an empty value. (Input.Option.set required array)" option
                        | value -> OptionValue.ValueRequiredArray [value]

                { input with Options = input.Options.Add(option, value) }
            | _ -> failwithf "The \"--%s\" option does not exists. (Input.Option.set)" option

        /// Set a list value for an option with array value or fail with exception when option is not defined.
        /// You can set only a list value, if you want to set a single value for option, use `Input.setOptionValue` instead.
        let setList (input: Input) (option: string) values =
            match input.OptionDefinitions with
            | OptionsDefinitions.HasDefinedOption option definition ->
                let value =
                    match definition.Value with
                    | OptionValueDefinition.ValueNone -> failwithf "The \"%s\" option does not accept any value. (Input.Option.setList)" option
                    | OptionValueDefinition.ValueIsArray _ -> OptionValue.ValueIsArray values
                    | OptionValueDefinition.ValueRequiredArray _ ->
                        match values with
                        | [] -> failwithf "The \"%s\" option does not accept an empty list." option
                        | values -> OptionValue.ValueRequiredArray values
                    | OptionValueDefinition.ValueRequired _
                    | OptionValueDefinition.ValueOptional _ -> failwithf "The \"%s\" option does not accept a list value. Use `Input.setOptionValue` instead." option

                { input with Options = input.Options.Add(option, value) }
            | _ -> failwithf "The \"--%s\" option does not exists. (Input.Option.setList)" option


    [<RequireQualifiedAccess>]
    module Argument =
        /// Check whether argument is defined.
        let (|IsDefined|_|) argument input =
            match argument with
            | ArgumentNames.Command -> Some ()
            | _ ->
                match input.ArgumentDefinitions with
                | ArgumentsDefinitions.HasDefinedArgument argument _ -> Some ()
                | _ -> None

        /// Check whether Input.Arguments contains argument name or fail with exception when argument is not defined.
        let (|Has|_|) argument = function
            | IsDefined argument as input ->
                match input.Arguments with
                | Arguments.HasArgument argument value -> Some value
                | _ -> None
            | _ -> failwithf "The \"%s\" argument does not exists." argument

        /// Check whether Input.Arguments contains argument name or fail with exception when argument is not defined.
        /// Access the value directly in matching.
        let (|Value|_|) argument = function
            | Has argument value -> Some (value |> ArgumentValue.value argument)
            | _ -> None

        /// Check whether Input.Arguments contains argument name or fail with exception when argument is not defined.
        /// Access the value directly in matching.
        let (|OptionalValue|_|) argument = function
            | Has argument value -> value |> ArgumentValue.stringValue
            | _ -> None

        /// Check whether Input.Arguments contains argument name or fail with exception when argument is not defined.
        /// Access the value as list directly in matching.
        let (|ListValue|_|) argument = function
            | Has argument value -> Some (value |> ArgumentValue.listValue)
            | _ -> None

        /// Try to find an ArgumentValue in Input.Arguments or fail with exception when argument is not defined.
        let tryGet argument = function
            | Has argument value -> Some value
            | _ -> None

        /// Get an ArgumentValue from Input.Arguments by argument name, fail on exception when option is not defined or presented.
        let get argument = function
            | Has argument value -> value
            | _ -> failwithf "The \"%s\" argument does not have a value." argument

        /// Get argument value or fail with exception, fail on exception when option is not defined or presented.
        /// This function should be used with Required Arguments.
        let value argument input =
            input |> get argument |> ArgumentValue.value argument

        /// Get argument value as string, fail on exception when option is not defined or presented.
        let asString argument input =
            input |> get argument |> ArgumentValue.stringValue

        /// Get argument value as int, fail on exception when option is not defined or presented.
        let asInt argument input =
            input |> get argument |> ArgumentValue.intValue

        /// Try to parse argument value as int, fail on exception when option is not defined or presented.
        let tryAsInt argument input =
            input |> get argument |> ArgumentValue.tryIntValue

        /// Get argument value as list, fail on exception when option is not defined or presented.
        let asList argument input =
            input |> get argument |> ArgumentValue.listValue

        /// Check whether Input.Arguments contains argument name and the value is set.
        let (|IsSet|_|) argument = function
            | Has argument value when value |> ArgumentValue.isSet -> Some value
            | _ -> None

        /// Checks whether argument value is set or fail with exception when argument is not defined.
        let isValueSet argument = function
            | IsSet argument _ -> true
            | _ -> false

        /// Set a value for an argument or fail with exception when argument is not defined.
        /// You can set only a single string value (even for array argument).
        /// If you want to set more than one value for Array argument, use `Input.setArgumentListValue` instead.
        let set (input: Input) (argument: string) value =
            match input.ArgumentDefinitions with
            | ArgumentsDefinitions.HasDefinedArgument argument definition ->
                let value =
                    match definition.Value with
                    | ArgumentValueDefinition.Required ->
                        match value with
                        | String.IsNullOrEmpty -> failwithf "The \"%s\" argument does not accept an empty value. (Input.Argument.set required)" argument
                        | value -> ArgumentValue.Required value
                    | ArgumentValueDefinition.Optional _ ->
                        match value with
                        | null -> ArgumentValue.Optional None
                        | value -> ArgumentValue.Optional (Some value)
                    | ArgumentValueDefinition.Array _ -> ArgumentValue.Array [value]
                    | ArgumentValueDefinition.RequiredArray ->
                        match value with
                        | String.IsNullOrEmpty -> failwithf "The \"%s\" argument does not accept an empty value. (Input.Argument.set required array)" argument
                        | value -> ArgumentValue.RequiredArray (NotEmptyList.ofListWithValues [value])

                { input with Arguments = input.Arguments.Add(argument, value) }
            | _ -> failwithf "The \"%s\" argument does not exists. (Input.Argument.set)" argument

        /// Set a list value for an array argument or fail with exception when argument is not defined.
        /// You can set only a list value, if you want to set a single value for argument, use `Input.setArgumentValue` instead.
        let setList (input: Input) (argument: string) values =
            match input.ArgumentDefinitions with
            | ArgumentsDefinitions.HasDefinedArgument argument definition ->
                let value =
                    match definition.Value with
                    | ArgumentValueDefinition.Array _ -> ArgumentValue.Array values
                    | ArgumentValueDefinition.RequiredArray ->
                        match values |> NotEmptyList.create with
                        | Ok notEmptyList -> ArgumentValue.RequiredArray notEmptyList
                        | Error NotEmptyListError.NoValues -> failwithf "The \"%s\" argument does not accept an empty list." argument
                    | ArgumentValueDefinition.Required
                    | ArgumentValueDefinition.Optional _ -> failwithf "The \"%s\" argument does not accept a list value. Use `Input.setArgumentValue` instead." argument

                { input with Arguments = input.Arguments.Add(argument, value) }
            | _ -> failwithf "The \"%s\" argument does not exists. (Input.Argument.setList)" argument

    let internal getCommandName input =
        input
        |> Argument.value ArgumentNames.Command
        |> Name
        |> CommandName
