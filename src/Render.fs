namespace Feather.ConsoleApplication

[<AutoOpen>]
module internal Render =

    [<RequireQualifiedAccess>]
    module Help =
        open System
        open Feather.ErrorHandling.Option.Operators

        let private createUsage decorationLevel options commandName command =
            sprintf "%s %s %s"
                (commandName |> CommandName.value)
                (decorationLevel |> OptionsDefinitions.usage options)
                (
                    match command.Arguments with
                    | [] -> ""
                    | arguments ->
                        let formattedArguments = arguments |> List.map Argument.usage

                        (sprintf "[%s]" Arguments.Separator) :: formattedArguments
                        |> String.concat " "
                )

        let private replaceHelpPlaceholders commandName help =
            // AppDomain.CurrentDomain.FriendlyName  // example
            // AppDomain.CurrentDomain.BaseDirectory // /Users/<user>/fsharp/console-application/example/bin/Debug/netcoreapp2.2/
            // Environment.CurrentDirectory          // /Users/<user>/fsharp/console-application/example
            // Environment.CommandLine               // /Users/<user>/fsharp/console-application/example/bin/Debug/netcoreapp2.2/example.dll list --help

            let baseDir = AppDomain.CurrentDomain.BaseDirectory
            let executableFileName = AppDomain.CurrentDomain.FriendlyName

            let relativePath =
                let trimmedBaseDir = baseDir.TrimEnd '/'
                let pathSegments = trimmedBaseDir.Split '/'
                let executableSegmentIndex =
                    pathSegments
                    |> Array.tryFindIndexBack ((=) executableFileName)

                match executableSegmentIndex with
                | Some index when index + 1 < pathSegments.Length ->
                    pathSegments[(index + 1) ..]
                    |> String.concat "/"
                    |> String.append "/"
                | _ ->
                    baseDir.Replace(Environment.CurrentDirectory, "").TrimStart '/'

            let commandName = commandName |> CommandName.value

            [
                "{{command.name}}", commandName
                "{{command.full_name}}", sprintf "%s%s.dll %s" relativePath executableFileName commandName
            ]
            |> List.fold String.replace help

        let internal showForCommand (output: Output) decorationLevel applicationOptions commandName (command: Command) =
            let options = command.Options @ applicationOptions

            output.SimpleOptions "Description:" [
                [ command.Description ]
            ]

            output.SimpleOptions "Usage:" [
                [ createUsage decorationLevel options commandName command ]
            ]

            match command.Arguments with
            | [] -> ()
            | arguments ->
                arguments
                |> List.map Argument.format
                |> output.SimpleOptions "Arguments:"

            match options with
            | [] -> ()
            | options ->
                options
                |> OptionsDefinitions.format
                |> output.SimpleOptions "Options:"

            command.Help
            |>! (fun help ->
                output.SimpleOptions "Help:" [ [ help |> replaceHelpPlaceholders commandName ] ]
            )

        let showSingleLine (output: Output) applicationOptions (commandName, command) =
            // list [--raw] [--format FORMAT] [--] [<namespace>]\n
            let options = command.Options @ applicationOptions

            createUsage Complete options commandName command
            |> sprintf "<c:green>%s</c>\n"
            |> output.Message

    [<RequireQualifiedAccess>]
    module Error =
        open Feather.ConsoleStyle
        open Feather.ErrorHandling.Option.Operators

        let showForCommand parts (error, currentCommand) =
            error
            |> ConsoleApplicationError.format (parts.Output.IsVerbose())
            |> List.iter parts.Output.Error

            currentCommand
            |>! Help.showSingleLine parts.Output parts.ApplicationOptions

        let show (ConsoleApplication application) currentCommand error =
            match application with
            | Ok parts ->
                (error, currentCommand)
                |> showForCommand parts
            | Error e ->
                e
                |> ConsoleApplicationError.format true
                |> List.iter Output.defaults.Error
