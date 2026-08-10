# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

- Shell completion support (opt-in) for bash, zsh, and fish
  - `enableCompletion` CE operation in `consoleApplication { }` builder
  - Built-in `completion` script command (configurable name, visibility and completed executable)
  - Internal hidden `__completion` resolver command used by generated scripts, called as `<index> -- <words>...`
  - Quoted and backslash-escaped words are completed as single words, and values containing spaces are completed
  - Clustered short options (`-fv`, `-fo val`, `-oval`) are completed
  - Option names are not suggested after the `--` end-of-options separator, where every word is a positional value
  - Command and option name suggestions carry their description, shown by zsh and fish
  - `Suggest.describedValues` attaches a description to the values of a suggestion callback
  - Suggestions are shell-agnostic; each generated script filters, escapes and inserts them itself

## 2.0.0 - 2025-12-04
- [**BC**] Use net10

## 1.0.0 - 2025-11-26
- Initial implementation
