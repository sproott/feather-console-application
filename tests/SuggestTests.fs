module Feather.ConsoleApplication.Tests.SuggestTests

open Expecto
open Feather.ConsoleApplication
open Feather.ConsoleApplication.Completion
open Feather.ConsoleApplication.Tests.Commands
open Feather.ErrorHandling

let private runDescribedSuggest (suggest: Suggest) (context: CompletionContext) =
    match suggest with
    | Suggest.SuggestSync suggestion -> suggestion context
    | Suggest.SuggestAsync suggestion -> suggestion context |> Async.RunSynchronously

let private runSuggest (suggest: Suggest) (context: CompletionContext) =
    runDescribedSuggest suggest context |> List.map CompletionSuggestion.value

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

