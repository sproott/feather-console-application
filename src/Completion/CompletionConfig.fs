namespace Feather.ConsoleApplication

type CompletionConfig = {
    CommandName: string
    /// Whether the completion command is shown by list/help.
    IsVisible: bool
    /// Command the generated script completes; defaults to the running executable name.
    ExecutableName: string option
}

[<RequireQualifiedAccess>]
module CompletionConfig =
    let defaults = {
        CommandName = "completion"
        IsVisible = true
        ExecutableName = None
    }
