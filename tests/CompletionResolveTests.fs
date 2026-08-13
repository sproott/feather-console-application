module Feather.ConsoleApplication.Tests.CompletionResolve

open Expecto
open Feather.ConsoleApplication
open Feather.ConsoleApplication.Completion
open Feather.ConsoleApplication.Tests.Commands
open Feather.ErrorHandling

let private commandNameOrFail name =
    CommandName.createInRuntime name |> Result.orFail

let private commandsOrFail definitions =
    definitions
    |> List.map (fun (name, definition) -> commandNameOrFail name, CommandDefinition.validate definition |> Result.orFail)
    |> Map.ofList
let private noApplicationOptions: OptionsDefinitions = []
let private resolveSuggestions applicationOptions commands words index =
    { Words = words; Index = index }
    |> Completion.Suggestions.resolve commands applicationOptions
    |> Async.RunSynchronously
    |> List.map CompletionSuggestion.value
    |> List.sort

let private resolveDescribedSuggestions applicationOptions commands words index =
    { Words = words; Index = index }
    |> Completion.Suggestions.resolve commands applicationOptions
    |> Async.RunSynchronously
    |> List.map (fun suggestion -> suggestion.Value, suggestion.Description)
    |> List.sortBy fst

[<Tests>]
let completionResolveTests =
    testList "Completion.Suggestions.resolve" [
        testList "command names" [
            testCase "should suggest every command when the line is empty" <| fun _ ->
                let commands =
                    commandsOrFail [
                        "foo", commandOne ignore
                        "bar", commandTwo ignore
                        "baz", commandThree ignore
                    ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "" ] 0

                Expect.equal suggestions [ "bar"; "baz"; "foo" ] "Every command name should be suggested"

            testCase "should suggest only commands with the typed prefix when a partial name is typed" <| fun _ ->
                let commands =
                    commandsOrFail [
                        "foo", commandOne ignore
                        "foobar", commandTwo ignore
                        "bar", commandThree ignore
                    ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "fo" ] 1

                Expect.equal suggestions [ "foo"; "foobar" ] "Only commands starting with the typed prefix should be suggested"
        ]

        testList "option values" [
            testCase "should suggest every callback value when the cursor follows an option expecting a value" <| fun _ ->
                let commands =
                    commandsOrFail [
                        "test", commandWith [] [
                            Option.required "opt1" (Some "o") "Option one" ""
                            |> Option.suggest (Suggest.values [ "val1"; "val2"; "other" ])
                        ]
                    ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1"; "" ] 3

                Expect.equal suggestions [ "other"; "val1"; "val2" ] "Every value of the option callback should be suggested"

            testCase "should suggest only values with the typed prefix when part of a value is typed" <| fun _ ->
                let commands =
                    commandsOrFail [
                        "test", commandWith [] [
                            Option.required "opt1" (Some "o") "Option one" ""
                            |> Option.suggest (Suggest.values [ "val1"; "val2"; "other" ])
                        ]
                    ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1"; "val" ] 3

                Expect.equal suggestions [ "val1"; "val2" ] "Only values starting with the typed prefix should be suggested"

            testCase "should suggest the option callback values when the option is written as its shortcut" <| fun _ ->
                let commands =
                    commandsOrFail [
                        "test", commandWith [] [
                            Option.required "opt1" (Some "o") "Option one" ""
                            |> Option.suggest (Suggest.values [ "shortval1"; "shortval2" ])
                        ]
                    ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "-o"; "" ] 3

                Expect.equal suggestions [ "shortval1"; "shortval2" ] "Shortcut should resolve to the same option callback as the long name"

            testCase "should return no suggestions when the option has no suggestion callback" <| fun _ ->
                let commands =
                    commandsOrFail [
                        "test", commandWith [] [ Option.required "opt1" (Some "o") "Option one" "" ]
                    ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1"; "" ] 3

                Expect.equal suggestions [] "Option without a callback should produce no suggestions"

            testCase "should suggest the values of the option when the index points past the last word" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1" ] 3

                Expect.equal suggestions [ "val1"; "val2" ] "An index past the last word starts an empty word at the cursor"
        ]

        testList "positional arguments" [
            testCase "should suggest every callback value when the cursor is at a positional argument" <| fun _ ->
                let commands =
                    commandsOrFail [
                        "test", commandWith
                            [
                                Argument.required "myArg" "Description"
                                |> Argument.suggest (Suggest.values [ "foo"; "bar"; "baz" ])
                            ]
                            []
                    ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "" ] 2

                Expect.equal suggestions [ "bar"; "baz"; "foo" ] "Every value of the argument callback should be suggested"

            testCase "should suggest only values with the typed prefix when part of the argument is typed" <| fun _ ->
                let commands =
                    commandsOrFail [
                        "test", commandWith
                            [
                                Argument.required "myArg" "Description"
                                |> Argument.suggest (Suggest.values [ "foo"; "bar"; "baz" ])
                            ]
                            []
                    ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "b" ] 2

                Expect.equal suggestions [ "bar"; "baz" ] "Only values starting with the typed prefix should be suggested"

            testCase "should return no suggestions when the argument has no suggestion callback" <| fun _ ->
                let commands =
                    commandsOrFail [
                        "test", commandWith [ Argument.required "myArg" "Description" ] []
                    ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "" ] 2

                Expect.equal suggestions [] "Argument without a callback should produce no suggestions"

            testCase "should suggest the values of the first argument when an option with a value precedes it" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1"; "val1"; "" ] 4

                Expect.equal suggestions [ "alpha"; "beta" ] "The value of the option should not be counted as a positional argument"

            testCase "should suggest the values of the first argument when an assigned option precedes it" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1=val1"; "" ] 3

                Expect.equal suggestions [ "alpha"; "beta" ] "An assigned option should not be counted as a positional argument"

            testCase "should return no suggestions when the argument callback throws" <| fun _ ->
                let commands =
                    commandsOrFail [
                        "test", commandWith
                            [
                                Argument.required "myArg" "Description"
                                |> Argument.suggest (Suggest.sync (fun _ -> failwith "boom"))
                            ]
                            []
                    ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "" ] 2

                Expect.equal suggestions [] "A failing callback should produce no suggestions"
        ]

        testList "assigned option values" [
            testCase "should suggest every callback value when the option is assigned with an empty value" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1=" ] 2

                Expect.equal suggestions [ "val1"; "val2" ] "Every value of the option callback should be suggested without the assignment"

            testCase "should suggest only values with the typed prefix when part of an assigned value is typed" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1=val2" ] 2

                Expect.equal suggestions [ "val2" ] "Only values starting with the typed prefix should be suggested"

            testCase "should suggest the values of the assigned option when the shell split the word at the equals sign" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions =
                    resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1"; "="; "va" ] 4

                Expect.equal suggestions [ "val1"; "val2" ] "The split parts should resolve as one assigned option word"

            testCase "should return no suggestions when an unknown option is assigned" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--unknown=" ] 2

                Expect.equal suggestions [] "An unknown option should produce no suggestions"
        ]

        testList "option names" [
            testCase "should suggest every long option name when only -- is typed" <| fun _ ->
                let commands = commandsOrFail [ "test", commandSix ignore ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--" ] 2

                Expect.equal suggestions [ "--bar"; "--cat"; "--foo" ] "Every long option name of the command should be suggested"

            testCase "should suggest only long option names with the typed prefix when a partial option is typed" <| fun _ ->
                let commands = commandsOrFail [ "test", commandSix ignore ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--ba" ] 2

                Expect.equal suggestions [ "--bar" ] "Only long option names starting with the typed prefix should be suggested"

            testCase "should suggest every short option name when only - is typed" <| fun _ ->
                let commands = commandsOrFail [ "test", commandSix ignore ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "-" ] 2

                Expect.equal suggestions [ "-b"; "-c"; "-f" ] "Every short option name of the command should be suggested"

            testCase "should suggest the option names when a complete option name is typed" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1" ] 2

                Expect.equal suggestions [ "--opt1"; "--opt1-extra" ] "A typed option name should be completed by option names, not by its values"

            testCase "should suggest application options alongside command options when only -- is typed" <| fun _ ->
                let commands = commandsOrFail [ "test", commandSix ignore ]
                let applicationOptions =
                    [ Option.noValue "debug" (Some "d") "" |> Result.orFail ]
                    |> OptionsDefinitions.validate
                    |> Result.orFail

                let suggestions = resolveSuggestions applicationOptions commands [ "myapp"; "test"; "--" ] 2

                Expect.contains suggestions "--debug" "Application option should be suggested"
                Expect.contains suggestions "--foo" "Command option should be suggested"
        ]

        testList "quoted words" [
            testCase "should suggest the values containing a space when the shell passed the current word unquoted" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSpacedSuggestions ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "my v" ] 2

                Expect.equal suggestions [ "my value" ] "A word containing a space should be completed as a single word"

            testCase "should suggest the values of the first argument when a quoted option value contains a space" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions =
                    resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1"; "\"my value\""; "" ] 4

                Expect.equal suggestions [ "alpha"; "beta" ] "The quoted option value should be counted as a single word"

            testCase "should suggest the values of the first argument when the command name is quoted" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "\"test\""; "" ] 2

                Expect.equal suggestions [ "alpha"; "beta" ] "The quotes around the command name should be stripped"
        ]

        testList "descriptions" [
            testCase "should describe a command name with the description of the command" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWith [] [] ]

                let suggestions = resolveDescribedSuggestions noApplicationOptions commands [ "myapp"; "te" ] 1

                Expect.equal suggestions [ "test", Some "Test command" ] "The description of the command should be suggested with its name"

            testCase "should describe a long option name with the description of the option" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveDescribedSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1" ] 2

                Expect.equal
                    suggestions
                    [ "--opt1", Some "Option one"; "--opt1-extra", Some "Option one extra" ]
                    "The description of the option should be suggested with its name"

            testCase "should describe a short option name with the description of the option" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithClusteredShortcuts ]

                let suggestions = resolveDescribedSuggestions noApplicationOptions commands [ "myapp"; "test"; "-" ] 2

                Expect.equal
                    suggestions
                    [ "-f", Some "Flag foo"; "-l", Some "Flag loud"; "-o", Some "Option one" ]
                    "The description of the option should be suggested with its shortcut"

            testCase "should describe the added shortcut when a cluster is extended" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithClusteredShortcuts ]

                let suggestions = resolveDescribedSuggestions noApplicationOptions commands [ "myapp"; "test"; "-f" ] 2

                Expect.equal
                    suggestions
                    [ "-fl", Some "Flag loud"; "-fo", Some "Option one" ]
                    "The shortcut added to the cluster should carry its own description"

            testCase "should describe no value suggested by a callback" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveDescribedSuggestions noApplicationOptions commands [ "myapp"; "test"; "" ] 2

                Expect.equal suggestions [ "alpha", None; "beta", None ] "A value coming from a callback has no description"
        ]

        testList "end of options" [
            testCase "should suggest the values of the first argument when the separator precedes the cursor" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--"; "" ] 3

                Expect.equal suggestions [ "alpha"; "beta" ] "The separator itself should not be counted as a positional value"

            testCase "should return no option names when a word starting with dashes is typed after the separator" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--"; "--" ] 3

                Expect.equal suggestions [] "After the separator a typed word is a positional value, not an option name"

            testCase "should suggest the values of the second argument when a value follows the separator" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithTwoSuggestedArguments ]

                let suggestions =
                    resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--"; "alpha"; "" ] 4

                Expect.equal suggestions [ "one"; "two" ] "Only the value after the separator should be counted as a positional one"

            testCase "should suggest the values of the second argument when an option name follows the separator" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithTwoSuggestedArguments ]

                let suggestions =
                    resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--"; "--opt1"; "" ] 4

                Expect.equal suggestions [ "one"; "two" ] "After the separator an option name is a positional value, so it takes no value of its own"
        ]

        testList "short option clusters" [
            testCase "should suggest the remaining shortcuts when a value-less shortcut is typed" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithClusteredShortcuts ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "-f" ] 2

                Expect.equal suggestions [ "-fl"; "-fo" ] "The typed shortcut should be extended by the shortcuts not used yet"

            testCase "should suggest the values of the clustered option when the cluster ends with an option expecting a value" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithClusteredShortcuts ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "-fo"; "" ] 3

                Expect.equal suggestions [ "val1"; "val2" ] "The last shortcut of the cluster should provide the values"

            testCase "should suggest the values inside the cluster when the value is typed right after the shortcut" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithClusteredShortcuts ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "-fov" ] 2

                Expect.equal suggestions [ "-foval1"; "-foval2" ] "The value typed in the cluster should be completed within the same word"

            testCase "should return no suggestions when the cluster contains an undefined shortcut" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithClusteredShortcuts ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "-fx" ] 2

                Expect.equal suggestions [] "An undefined shortcut should produce no suggestions"

            testCase "should extend the cluster only with single-letter shortcuts when an option has multi-letter shortcuts" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithClusteredShortcuts ]

                let suggestions = resolveSuggestions Commands.applicationOptions commands [ "myapp"; "test"; "-v" ] 2

                Expect.contains suggestions "-vf" "A single-letter command shortcut should extend the cluster"
                Expect.isFalse
                    (suggestions |> List.exists (fun suggestion -> suggestion.Contains "vv"))
                    "A multi-letter shortcut should not extend the cluster"

            testCase "should suggest the values of the first argument when a value-less cluster precedes it" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithClusteredShortcuts ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "-fl"; "" ] 3

                Expect.equal suggestions [ "alpha"; "beta" ] "A cluster of value-less shortcuts should not shift the position of the argument"

            testCase "should suggest the values of the first argument when a cluster expecting a value and its value precede it" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithClusteredShortcuts ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "-fo"; "val1"; "" ] 4

                Expect.equal suggestions [ "alpha"; "beta" ] "The value of the clustered option should not be counted as a positional argument"
        ]
    ]

