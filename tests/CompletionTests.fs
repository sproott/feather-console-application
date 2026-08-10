module Feather.ConsoleApplication.Tests.Completion

open Expecto
open Feather.ConsoleApplication
open Feather.ConsoleApplication.Tests.Commands
open Feather.ErrorHandling

let private commandNameOrFail name =
    CommandName.createInRuntime name |> Result.orFail

let private commandsOrFail definitions =
    definitions
    |> List.map (fun (name, definition) -> commandNameOrFail name, CommandDefinition.validate definition |> Result.orFail)
    |> Map.ofList

let private commandWith arguments options: CommandDefinition =
    {
        Description = "Test command"
        Help = None
        Arguments = arguments
        Options = options
        Initialize = None
        Interact = None
        Execute = Execute <| fun (_, _) -> ExitCode.Success
    }

let private noApplicationOptions: OptionsDefinitions = []

let private commandWithSuggestedInput =
    commandWith
        [
            Argument.required "target" "Target"
            |> Argument.suggest (Suggest.values [ "alpha"; "beta" ])
        ]
        [
            Option.required "opt1" (Some "o") "Option one" ""
            |> Option.suggest (Suggest.values [ "val1"; "val2" ])

            Option.required "opt1-extra" None "Option one extra" ""
        ]

let private commandWithClusteredShortcuts =
    commandWith
        [
            Argument.required "target" "Target"
            |> Argument.suggest (Suggest.values [ "alpha"; "beta" ])
        ]
        [
            Option.noValue "foo" (Some "f") "Flag foo"
            Option.noValue "loud" (Some "l") "Flag loud"

            Option.required "opt1" (Some "o") "Option one" ""
            |> Option.suggest (Suggest.values [ "val1"; "val2" ])
        ]

let private commandWithTwoSuggestedArguments =
    commandWith
        [
            Argument.required "first" "First"
            |> Argument.suggest (Suggest.values [ "alpha"; "beta" ])

            Argument.required "second" "Second"
            |> Argument.suggest (Suggest.values [ "one"; "two" ])
        ]
        [
            Option.required "opt1" (Some "o") "Option one" ""
            |> Option.suggest (Suggest.values [ "val1"; "val2" ])
        ]

let private commandWithSpacedSuggestions =
    commandWith
        [
            Argument.required "target" "Target"
            |> Argument.suggest (Suggest.values [ "my value"; "my other value"; "plain" ])
        ]
        []

let private runDescribedSuggest (suggest: Suggest) (context: CompletionContext) =
    match suggest with
    | Suggest.SuggestSync suggestion -> suggestion context
    | Suggest.SuggestAsync suggestion -> suggestion context |> Async.RunSynchronously

let private runSuggest (suggest: Suggest) (context: CompletionContext) =
    runDescribedSuggest suggest context |> List.map CompletionSuggestion.value

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

let private runApplication buildApplication args =
    use buffer = new Feather.ConsoleStyle.Output.BufferOutput(Feather.ConsoleStyle.Verbosity.Normal)
    let console = Feather.ConsoleStyle.ConsoleStyle(buffer)

    let result = buildApplication console |> runResult args

    result, (buffer.Fetch() |> console.RemoveMarkup)

let private runScriptApplication executableName shell =
    runApplication
        (fun console ->
            consoleApplication {
                name "Test app"
                version "1.0.0"
                enableCompletion { CompletionConfig.defaults with ExecutableName = executableName }
                useOutput console
                command "deploy" (commandWith [] [])
            })
        [| "completion"; shell |]

let private runCompletionApplication completionCommandName =
    runApplication
        (fun console ->
            consoleApplication {
                name "Test app"
                version "1.0.0"
                enableCompletion { CompletionConfig.defaults with CommandName = completionCommandName }
                useOutput console
                command "deploy" (commandWith [] [])
            })
        [| "list" |]

let private fakeListers: Suggest.FileSystemListers =
    {
        DirectoryExists = (fun path ->
            path = "." ||
            path = "src" || path = "./src" ||
            path = "docs" || path = "./docs" ||
            path = ".hidden" || path = "./.hidden")
        FileExists = (fun path ->
            path.EndsWith "README.md" ||
            path.EndsWith "tool.fsx" ||
            path.EndsWith "notes.txt" ||
            path.EndsWith ".env")
        EnumerateFileSystemEntries = (fun path ->
            [ "src"; "docs"; "README.md"; "tool.fsx"; "notes.txt"; ".env" ]
            |> List.map (fun name -> System.IO.Path.Combine(path, name))
            |> Seq.ofList)
        EnumerateDirectories = (fun path ->
            [ "src"; "docs"; ".hidden" ]
            |> List.map (fun name -> System.IO.Path.Combine(path, name))
            |> Seq.ofList)
    }

let private fakeMissingDirectoryListers: Suggest.FileSystemListers =
    {
        DirectoryExists = (fun _ -> false)
        FileExists = (fun _ -> false)
        EnumerateFileSystemEntries = (fun _ -> Seq.empty)
        EnumerateDirectories = (fun _ -> Seq.empty)
    }

let private fakeThrowingListers: Suggest.FileSystemListers =
    {
        DirectoryExists = (fun _ -> true)
        FileExists = (fun _ -> false)
        EnumerateFileSystemEntries = (fun _ -> failwith "boom")
        EnumerateDirectories = (fun _ -> failwith "boom")
    }

[<Tests>]
let suggestCombinatorsTests =
    testList "Suggest combinators" [
        testList "Suggest.map" [
            testCase "should transform every value when the source suggests a static list" <| fun _ ->
                let suggest = Suggest.values [ "dev"; "prod" ] |> Suggest.map _.ToUpperInvariant()

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal suggestions [ "DEV"; "PROD" ] "Every suggested value should be upper-cased"
        ]

        testList "Suggest.mapAsync" [
            testCase "should transform every value when the source suggests a static list" <| fun _ ->
                let suggest =
                    Suggest.values [ "dev"; "prod" ]
                    |> Suggest.mapAsync (fun value -> async { return value + "-x" })

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal suggestions [ "dev-x"; "prod-x" ] "Every suggested value should have the suffix appended"
        ]

        testList "Suggest.filter" [
            testCase "should keep only values matching the predicate" <| fun _ ->
                let suggest = Suggest.values [ "dev"; "prod" ] |> Suggest.filter ((=) "dev")

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal suggestions [ "dev" ] "Only values matching the predicate should be suggested"
        ]

        testList "Suggest.filterAsync" [
            testCase "should keep only values matching the predicate" <| fun _ ->
                let suggest =
                    Suggest.values [ "dev"; "prod" ]
                    |> Suggest.filterAsync (fun value -> async { return value = "dev" })

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal suggestions [ "dev" ] "Only values matching the predicate should be suggested"
        ]

        testList "Suggest.describedValues" [
            testCase "should suggest every value with its description" <| fun _ ->
                let suggest = Suggest.describedValues [ "dev", "Development"; "prod", "Production" ]

                let suggestions = runDescribedSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal
                    suggestions
                    [
                        { Value = "dev"; Description = Some "Development" }
                        { Value = "prod"; Description = Some "Production" }
                    ]
                    "Every value should keep the description given with it"

            testCase "should describe no value when the description is empty" <| fun _ ->
                let suggest = Suggest.describedValues [ "dev", "" ]

                let suggestions = runDescribedSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal suggestions [ { Value = "dev"; Description = None } ] "An empty description should be the same as none"

            testCase "should keep the descriptions when the values are transformed" <| fun _ ->
                let suggest =
                    Suggest.describedValues [ "dev", "Development" ]
                    |> Suggest.map _.ToUpperInvariant()

                let suggestions = runDescribedSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal
                    suggestions
                    [ { Value = "DEV"; Description = Some "Development" } ]
                    "Transforming the value should leave its description untouched"

            testCase "should keep the descriptions when the values are filtered" <| fun _ ->
                let suggest =
                    Suggest.describedValues [ "dev", "Development"; "prod", "Production" ]
                    |> Suggest.filter ((=) "prod")

                let suggestions = runDescribedSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal
                    suggestions
                    [ { Value = "prod"; Description = Some "Production" } ]
                    "Filtering by the value should leave the description of the kept value untouched"

            testCase "should keep the descriptions when the values are transformed asynchronously" <| fun _ ->
                let suggest =
                    Suggest.describedValues [ "dev", "Development" ]
                    |> Suggest.mapAsync (fun value -> async { return value + "-x" })

                let suggestions = runDescribedSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal
                    suggestions
                    [ { Value = "dev-x"; Description = Some "Development" } ]
                    "Transforming the value should leave its description untouched"

            testCase "should keep the descriptions when the values are filtered asynchronously" <| fun _ ->
                let suggest =
                    Suggest.describedValues [ "dev", "Development"; "prod", "Production" ]
                    |> Suggest.filterAsync (fun value -> async { return value = "dev" })

                let suggestions = runDescribedSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal
                    suggestions
                    [ { Value = "dev"; Description = Some "Development" } ]
                    "Filtering by the value should leave the description of the kept value untouched"
        ]
    ]

[<Tests>]
let suggestFileHelpersTests =
    testList "Suggest file helpers" [
        testList "Suggest.files" [
            testCase "should append a trailing slash when the entry is a directory" <| fun _ ->
                let suggest = Suggest.filesWith fakeListers

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.contains suggestions "src/" "Directory should be suggested with a trailing slash"

            testCase "should suggest file entries by their plain name" <| fun _ ->
                let suggest = Suggest.filesWith fakeListers

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.contains suggestions "README.md" "File entry should be suggested by its name"
                Expect.contains suggestions "tool.fsx" "File entry should be suggested by its name"

            testCase "should omit dotfiles when the current word does not start with a dot" <| fun _ ->
                let suggest = Suggest.filesWith fakeListers

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.isFalse (suggestions |> List.contains ".env") "Dotfile should not be suggested"

            testCase "should return no suggestions when the directory does not exist" <| fun _ ->
                let suggest = Suggest.filesWith fakeMissingDirectoryListers

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal suggestions [] "Missing directory should produce no suggestions"

            testCase "should return no suggestions when listing the directory throws" <| fun _ ->
                let suggest = Suggest.filesWith fakeThrowingListers

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.equal suggestions [] "Failed listing should produce no suggestions"
        ]

        testList "Suggest.directories" [
            testCase "should suggest dot-prefixed directories when the current word starts with a dot" <| fun _ ->
                let suggest = Suggest.directoriesWith fakeListers

                let suggestions = runSuggest suggest { CurrentWord = "."; PrecedingWords = [] }

                Expect.contains suggestions ".hidden/" "Dot-prefixed directory should be suggested"
        ]

        testList "Suggest.filesWithExtension" [
            testCase "should suggest directories even when they do not match the extension" <| fun _ ->
                let suggest = Suggest.filesWithExtensionWith fakeListers [ ".fsx" ]

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.contains suggestions "src/" "Directory should be suggested regardless of the extension filter"

            testCase "should suggest files when their extension is in the filter" <| fun _ ->
                let suggest = Suggest.filesWithExtensionWith fakeListers [ ".fsx" ]

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.contains suggestions "tool.fsx" "File with an allowed extension should be suggested"

            testCase "should omit files when their extension is not in the filter" <| fun _ ->
                let suggest = Suggest.filesWithExtensionWith fakeListers [ ".fsx" ]

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.isFalse (suggestions |> List.contains "README.md") "File with a disallowed extension should not be suggested"

            testCase "should omit every file when the extension filter is empty" <| fun _ ->
                let suggest = Suggest.filesWithExtensionWith fakeListers []

                let suggestions = runSuggest suggest { CurrentWord = ""; PrecedingWords = [] }

                Expect.isFalse (suggestions |> List.contains "tool.fsx") "No file should be suggested for an empty extension filter"
        ]
    ]

[<Tests>]
let completionWordsTests =
    testList "Completion.Words" [
        testList "Words.normalize" [
            testCase "should join the option with its value when the shell split the word at the equals sign" <| fun _ ->
                let context = Completion.Words.normalize [ "myapp"; "test"; "--opt1"; "="; "va" ] 4

                Expect.equal context.CurrentWord "--opt1=va" "The split word should be joined back into the word at the cursor"
                Expect.equal context.PrecedingWords [ "myapp"; "test" ] "The parts of the joined word should not remain as preceding words"

            testCase "should join the option with an empty value when the equals sign is the last word" <| fun _ ->
                let context = Completion.Words.normalize [ "myapp"; "test"; "--opt1"; "=" ] 3

                Expect.equal context.CurrentWord "--opt1=" "The trailing equals sign should belong to the option word"

            testCase "should drop the backslashes of an escaped word" <| fun _ ->
                let context = Completion.Words.normalize [ "myapp"; "test"; @"my\ o" ] 2

                Expect.equal context.CurrentWord "my o" "The escaped space should be part of the word value"

            testCase "should turn a doubled backslash into a single one" <| fun _ ->
                let context = Completion.Words.normalize [ "myapp"; "test"; @"a\\b" ] 2

                Expect.equal context.CurrentWord @"a\b" "The escaping backslash should be dropped and the escaped one kept"

            testCase "should drop the quotes surrounding a preceding word" <| fun _ ->
                let context = Completion.Words.normalize [ "myapp"; "\"test\""; "" ] 2

                Expect.equal context.PrecedingWords [ "myapp"; "test" ] "The quotes typed around a word should not be part of its value"

            testCase "should yield an empty current word when the index is past the end of the words" <| fun _ ->
                let context = Completion.Words.normalize [ "myapp"; "test" ] 2

                Expect.equal context.CurrentWord "" "A cursor past the last word starts a new one"
                Expect.equal context.PrecedingWords [ "myapp"; "test" ] "Every word typed so far should precede the cursor"

            testCase "should drop the words following the index" <| fun _ ->
                let context = Completion.Words.normalize [ "myapp"; "test"; "al"; "beta" ] 2

                Expect.equal context.CurrentWord "al" "The word at the index is the one being completed"
                Expect.equal context.PrecedingWords [ "myapp"; "test" ] "Words typed after the cursor should be ignored"
        ]
    ]

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

            testCase "should suggest every matching command when several share the typed prefix" <| fun _ ->
                let commands =
                    commandsOrFail [
                        "deploy", commandOne ignore
                        "delete", commandTwo ignore
                        "describe", commandThree ignore
                    ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "d" ] 1

                Expect.equal suggestions [ "delete"; "deploy"; "describe" ] "Every command sharing the prefix should be suggested"
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

            testCase "should return no suggestions when a value-less option precedes the positional argument" <| fun _ ->
                let commands = commandsOrFail [ "test", commandSix ignore ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--foo"; "something" ] 3

                Expect.equal suggestions [] "Argument without a callback should produce no suggestions"

            testCase "should suggest the values of the first argument when an option with a value precedes it" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "--opt1"; "val1"; "" ] 4

                Expect.equal suggestions [ "alpha"; "beta" ] "The value of the option should not be counted as a positional argument"

            testCase "should suggest the values of the first argument when an option written as its shortcut precedes it" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSuggestedInput ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; "-o"; "val1"; "" ] 4

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

            testCase "should suggest the values containing a space when the space is escaped with a backslash" <| fun _ ->
                let commands = commandsOrFail [ "test", commandWithSpacedSuggestions ]

                let suggestions = resolveSuggestions noApplicationOptions commands [ "myapp"; "test"; @"my\ v" ] 2

                Expect.equal suggestions [ "my value" ] "The escaped space should be part of the completed word"

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

[<Tests>]
let completionApplicationTests =
    testList "ConsoleApplication - completion" [
        testList "enabled completion" [
            testCase "should list the completion command when completion is enabled" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                title "Feather.ConsoleApplication.Test"
                                name "Test app"
                                version "1.0.0"
                                info ApplicationInfo.NameAndVersion
                                enableCompletion
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "list" |]

                Expect.equal result (Ok ExitCode.Success) "List command should succeed"
                Expect.stringContains output "completion" "Completion command should be listed"

            testCase "should not list the hidden resolver command when completion is enabled" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                title "Feather.ConsoleApplication.Test"
                                name "Test app"
                                version "1.0.0"
                                info ApplicationInfo.NameAndVersion
                                enableCompletion
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "list" |]

                Expect.equal result (Ok ExitCode.Success) "List command should succeed"
                Expect.isFalse (output.Contains "__completion") "Hidden resolver command should never be listed"

            testCase "should print suggestions when the hidden resolver is invoked with an index and words" <| fun _ ->
                let deployCommand =
                    commandWith [] [
                        Option.required "env" (Some "e") "Environment" ""
                        |> Option.suggest (Suggest.values [ "dev"; "staging"; "prod" ])
                    ]

                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                title "Feather.ConsoleApplication.Test"
                                name "testapp"
                                version "1.0.0"
                                info ApplicationInfo.NameAndVersion
                                enableCompletion
                                useOutput console
                                command "deploy" deployCommand
                            })
                        [| "__completion"; "3"; "--"; "testapp"; "deploy"; "--env" |]

                Expect.equal result (Ok ExitCode.Success) "Hidden resolver command should succeed"
                Expect.stringContains output "dev" "Suggestions of the option callback should be printed"
                Expect.stringContains output "staging" "Suggestions of the option callback should be printed"
                Expect.stringContains output "prod" "Suggestions of the option callback should be printed"

            testCase "should print the description after a tab when the suggestion has one" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                name "myapp"
                                version "1.0.0"
                                enableCompletion
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "__completion"; "1"; "--"; "myapp"; "d" |]

                Expect.equal result (Ok ExitCode.Success) "Hidden resolver command should succeed"
                Expect.stringContains output "deploy\tTest command" "The description should follow the value after a tab"

            testCase "should print no tab when the suggestion has no description" <| fun _ ->
                let deployCommand =
                    commandWith
                        [
                            Argument.required "target" "Target"
                            |> Argument.suggest (Suggest.values [ "alpha" ])
                        ]
                        []

                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                name "myapp"
                                version "1.0.0"
                                enableCompletion
                                useOutput console
                                command "deploy" deployCommand
                            })
                        [| "__completion"; "2"; "--"; "myapp"; "deploy" |]

                Expect.equal result (Ok ExitCode.Success) "Hidden resolver command should succeed"
                Expect.equal (output.Trim()) "alpha" "A value without a description should be printed on its own"

            testCase "should not print the application banner when the hidden resolver is invoked" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                title "Feather.ConsoleApplication.Test"
                                name "myapp"
                                version "1.2.3"
                                info ApplicationInfo.NameAndVersion
                                enableCompletion
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "__completion"; "1"; "--"; "myapp"; "d" |]

                Expect.equal result (Ok ExitCode.Success) "Hidden resolver command should succeed"
                Expect.isFalse (output.Contains "myapp") "Application banner should not be printed"

            testCase "should suggest the completion command itself when its name is partially typed" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                name "myapp"
                                version "1.0.0"
                                enableCompletion
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "__completion"; "1"; "--"; "myapp"; "comp" |]

                Expect.equal result (Ok ExitCode.Success) "Hidden resolver command should succeed"
                Expect.stringContains output "completion" "Completion command should be suggested"
                Expect.isFalse (output.Contains "__completion") "Hidden resolver command should never be suggested"

            testCase "should suggest the supported shells when the completion command expects one" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                name "myapp"
                                version "1.0.0"
                                enableCompletion
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "__completion"; "2"; "--"; "myapp"; "completion" |]

                Expect.equal result (Ok ExitCode.Success) "Hidden resolver command should succeed"
                Expect.stringContains output "bash" "Supported shell should be suggested"
                Expect.stringContains output "zsh" "Supported shell should be suggested"
                Expect.stringContains output "fish" "Supported shell should be suggested"

            testCase "should print nothing when the hidden resolver is invoked with an index which is not a number" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                name "myapp"
                                version "1.0.0"
                                enableCompletion
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "__completion"; "here"; "--"; "myapp"; "d" |]

                Expect.equal result (Ok ExitCode.Error) "Hidden resolver command should fail"
                Expect.equal (output.Trim()) "" "Nothing should be printed, as the output is read back as suggestions"

            testCase "should run the help command successfully when completion is enabled" <| fun _ ->
                let result, _ =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                title "Feather.ConsoleApplication.Test"
                                name "Test app"
                                version "1.0.0"
                                info ApplicationInfo.NameAndVersion
                                enableCompletion
                                useOutput console
                                command "foo" (commandWith [] [])
                            })
                        [| "help" |]

                Expect.equal result (Ok ExitCode.Success) "Help command should succeed"

            testCase "should run the help command successfully when a custom completion config is used" <| fun _ ->
                let result, _ =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                title "Feather.ConsoleApplication.Test"
                                name "Test app"
                                version "1.0.0"
                                info ApplicationInfo.NameAndVersion
                                enableCompletion { CompletionConfig.defaults with CommandName = "custom-completion" }
                                useOutput console
                                command "foo" (commandWith [] [])
                            })
                        [| "help" |]

                Expect.equal result (Ok ExitCode.Success) "Help command should succeed"
        ]

        testList "disabled completion" [
            testCase "should not list the completion command when completion is not enabled" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                title "Feather.ConsoleApplication.Test"
                                name "Test app"
                                version "1.0.0"
                                info ApplicationInfo.NameAndVersion
                                useOutput console
                                command "foo" (commandWith [] [])
                            })
                        [| "list" |]

                Expect.equal result (Ok ExitCode.Success) "List command should succeed"
                Expect.isFalse (output.Contains "completion") "Completion command should not be listed"
        ]

        testList "hidden completion command" [
            testCase "should not list the completion command when IsVisible is false" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                name "myapp"
                                version "1.0.0"
                                enableCompletion { CompletionConfig.defaults with IsVisible = false; ExecutableName = Some "myapp" }
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "list" |]

                Expect.equal result (Ok ExitCode.Success) "List command should succeed"
                Expect.isFalse (output.Contains "completion") "Completion command should not be listed"

            testCase "should generate the script when IsVisible is false" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                name "myapp"
                                version "1.0.0"
                                enableCompletion { CompletionConfig.defaults with IsVisible = false; ExecutableName = Some "myapp" }
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "completion"; "bash" |]

                Expect.equal result (Ok ExitCode.Success) "Completion script command should succeed"
                Expect.stringContains output "myapp" "Generated script should be printed"

            testCase "should reject the resolver arguments when they are passed to the public completion command" <| fun _ ->
                let result, _ =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                name "myapp"
                                version "1.0.0"
                                enableCompletion { CompletionConfig.defaults with IsVisible = false; ExecutableName = Some "myapp" }
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "completion"; "1"; "--"; "myapp"; "d" |]

                Expect.isError result "Public completion command should not accept an index and words"

            testCase "should resolve suggestions when the hidden resolver is invoked and the completion command is hidden" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                name "myapp"
                                version "1.0.0"
                                enableCompletion { CompletionConfig.defaults with IsVisible = false; ExecutableName = Some "myapp" }
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "__completion"; "1"; "--"; "myapp"; "d" |]

                Expect.equal result (Ok ExitCode.Success) "Hidden resolver command should succeed"
                Expect.stringContains output "deploy" "Matching command name should be suggested"
        ]

        testList "completion script" [
            testCase "should embed the application name when generating the bash script" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                name "myapp"
                                version "1.0.0"
                                enableCompletion { CompletionConfig.defaults with ExecutableName = Some "myapp" }
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "completion"; "bash" |]

                Expect.equal result (Ok ExitCode.Success) "Completion script command should succeed"
                Expect.stringContains output "myapp" "Generated script should contain the application name"
                Expect.isFalse (output.Contains "{{app}}") "Generated script should not contain an unresolved placeholder"

            testCase "should invoke the hidden resolver when generating the bash script" <| fun _ ->
                let result, output =
                    runApplication
                        (fun console ->
                            consoleApplication {
                                name "myapp"
                                version "1.0.0"
                                enableCompletion { CompletionConfig.defaults with ExecutableName = Some "myapp" }
                                useOutput console
                                command "deploy" (commandWith [] [])
                            })
                        [| "completion"; "bash" |]

                Expect.equal result (Ok ExitCode.Success) "Completion script command should succeed"
                Expect.stringContains output "myapp __completion" "Generated script should call the hidden resolver command"

            testCase "should complete the executable name when it differs from the application name" <| fun _ ->
                let result, output = runScriptApplication (Some "my-tool") "bash"

                Expect.equal result (Ok ExitCode.Success) "Completion script command should succeed"
                Expect.stringContains output "complete -F _my_tool_completion my-tool" "Generated script should complete the executable, not the application name"

            testCase "should replace characters not allowed in a shell function name" <| fun _ ->
                let result, output = runScriptApplication (Some "my tool.exe") "bash"

                Expect.equal result (Ok ExitCode.Success) "Completion script command should succeed"
                Expect.stringContains output "_my_tool_exe_completion() {" "Function name should contain only letters, digits and underscores"

            testCase "should complete the running executable when no executable name is configured" <| fun _ ->
                let result, output = runScriptApplication None "bash"

                Expect.equal result (Ok ExitCode.Success) "Completion script command should succeed"
                Expect.stringContains output (System.AppDomain.CurrentDomain.FriendlyName) "Generated script should default to the running executable name"

            testCase "should pass the words and the index of the completed one when generating the bash script" <| fun _ ->
                let result, output = runScriptApplication (Some "myapp") "bash"

                Expect.equal result (Ok ExitCode.Success) "Completion script command should succeed"
                Expect.stringContains output "myapp __completion \"$index\" -- \"${completionWords[@]}\"" "Resolver should be called with the index and the words"
                Expect.isFalse (output.Contains "COMP_LINE") "The line should not be passed to the resolver"
                Expect.isFalse (output.Contains "COMP_POINT") "The cursor position should not be passed to the resolver"

            testCase "should cut the suggestion at the tab when generating the bash script" <| fun _ ->
                let result, output = runScriptApplication (Some "myapp") "bash"

                Expect.equal result (Ok ExitCode.Success) "Completion script command should succeed"
                Expect.stringContains output "suggestion=\"${suggestion%%$'\\t'*}\"" "Readline shows no descriptions, so the script should drop them"

            testCase "should pass the words and the index of the completed one when generating the zsh script" <| fun _ ->
                let result, output = runScriptApplication (Some "myapp") "zsh"

                Expect.equal result (Ok ExitCode.Success) "Completion script command should succeed"
                Expect.stringContains output "#compdef myapp" "Generated script should be a zsh completion definition"
                Expect.stringContains output "completionWords=(\"${(@Q)words[1,CURRENT-1]}\" \"$PREFIX\")" "Words should be taken from zsh unquoted, with the word at the cursor taken from its prefix"
                Expect.stringContains output "myapp __completion $((CURRENT - 1)) -- \"${completionWords[@]}\"" "Resolver should be called with the words zsh parsed and the index of the current one"
                Expect.stringContains output "compdef _myapp_completion myapp" "Completion function should be registered for the executable"

            testCase "should describe the suggestions when generating the zsh script" <| fun _ ->
                let result, output = runScriptApplication (Some "myapp") "zsh"

                Expect.equal result (Ok ExitCode.Success) "Completion script command should succeed"
                Expect.stringContains output "described+=(\"${value//:/\\\\:}:$description\")" "The description should be paired with its value by a colon"
                Expect.stringContains output "_describe -t suggestions 'suggestion' described" "Zsh should list the suggestions with their descriptions"

            testCase "should pass the words and the index of the completed one when generating the fish script" <| fun _ ->
                let result, output = runScriptApplication (Some "myapp") "fish"

                Expect.equal result (Ok ExitCode.Success) "Completion script command should succeed"
                Expect.stringContains output "commandline --current-process --tokenize --cut-at-cursor" "Words should be read up to the cursor"
                Expect.stringContains output "myapp __completion (count $completionWords) -- $completionWords $currentWord" "Resolver should be called with the words and the index of the current one"
                Expect.stringContains output "complete -c myapp" "Completion function should be registered for the executable"

            testCase "should fail with the supported shells when an unknown shell is given" <| fun _ ->
                let result, output = runScriptApplication (Some "myapp") "powershell"

                Expect.equal result (Ok ExitCode.Error) "Completion script command should fail for an unsupported shell"
                Expect.stringContains output "bash|zsh|fish" "Supported shells should be listed"
                Expect.isFalse (output.Contains "complete -F") "No script should be generated for an unsupported shell"
        ]
    ]

[<Tests>]
let completionConfigValidationTests =
    testList "ConsoleApplication - completion config" [
        testCase "should fail when the completion command name is a reserved one" <| fun _ ->
            let result, _ = runCompletionApplication "list"

            Expect.isError result "A reserved command name should not be accepted"

        testCase "should fail when the completion command name is already used by a command" <| fun _ ->
            let result, _ = runCompletionApplication "deploy"

            Expect.isError result "A command name of the application should not be accepted"

        testCase "should fail when the completion command name is the hidden resolver name" <| fun _ ->
            let result, _ = runCompletionApplication Completion.Setup.HiddenResolverCommandName

            Expect.isError result "The hidden resolver name should not be accepted"

        testCase "should fail when the completion command name is not a valid command name" <| fun _ ->
            let result, _ = runCompletionApplication "not a command!"

            Expect.isError result "An invalid command name should not be accepted"

        testCase "should succeed when the completion command name is free" <| fun _ ->
            let result, _ = runCompletionApplication "shell-completion"

            Expect.equal result (Ok ExitCode.Success) "A free command name should be accepted"
    ]
