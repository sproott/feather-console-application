module Feather.ConsoleApplication.Tests.CompletionSetup

open Expecto
open Feather.ConsoleApplication
open Feather.ConsoleApplication.Completion
open Feather.ConsoleApplication.Tests.Commands
open Feather.ErrorHandling


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
                Expect.stringContains output System.AppDomain.CurrentDomain.FriendlyName "Generated script should default to the running executable name"

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
            let result, _ = runCompletionApplication Setup.HiddenResolverCommandName

            Expect.isError result "The hidden resolver name should not be accepted"

        testCase "should fail when the completion command name is not a valid command name" <| fun _ ->
            let result, _ = runCompletionApplication "not a command!"

            Expect.isError result "An invalid command name should not be accepted"

        testCase "should succeed when the completion command name is free" <| fun _ ->
            let result, _ = runCompletionApplication "shell-completion"

            Expect.equal result (Ok ExitCode.Success) "A free command name should be accepted"
    ]
