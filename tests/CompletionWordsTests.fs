module Feather.ConsoleApplication.Tests.CompletionWords

open Expecto
open Feather.ConsoleApplication
open Feather.ConsoleApplication.Completion
open Feather.ConsoleApplication.Tests.Commands
open Feather.ErrorHandling

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

