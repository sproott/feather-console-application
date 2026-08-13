namespace Feather.ConsoleApplication.Completion

/// Generates the script a user sources to hook the application's resolver into their shell.
[<RequireQualifiedAccess>]
module internal ShellScript =
    open System

    type private ScriptParts = {
        ExecutableName: string
        ScriptInvocation: string
        ResolverInvocation: string
        FunctionName: string
    }

    /// Shell function names allow no dashes, dots or spaces, all of which an application name may contain.
    let private functionName (executableName: string) =
        let sanitized =
            executableName
            |> Seq.map (fun c -> if Char.IsLetterOrDigit c || c = '_' then c else '_')
            |> Seq.toArray
            |> String

        match sanitized with
        | "" -> "_completion"
        | name when name[0] |> Char.IsDigit -> sprintf "_%s_completion" name
        | name -> sprintf "%s_completion" name

    // The suggestions are shell-agnostic, so each script filters and escapes them the way its shell inserts them.

    let private bashScript (parts: ScriptParts) =
        $$$"""# Bash completion script for {{{parts.ExecutableName}}}
# Source this file in your .bashrc or .bash_profile:
# source <({{{parts.ScriptInvocation}}} bash)

_{{{parts.FunctionName}}}() {
    local index suggestion quoted
    local -a completionWords suggestions

    completionWords=("${COMP_WORDS[@]}")
    index=$COMP_CWORD

    # Readline inserts into the quotes it already knows about, so only an unquoted word is escaped.
    quoted=0
    case "${COMP_WORDS[COMP_CWORD]}" in
        \"*|\'*) quoted=1 ;;
    esac

    # COMP_WORDS keeps the word as typed, $2 is the same word with the quoting already removed.
    if [ "${BASH_VERSINFO[0]}" -ge 4 ]; then
        completionWords[index]="$2"
    fi

    # Readline shows no descriptions, so everything after the tab is dropped.
    suggestions=()
    while IFS= read -r suggestion; do
        suggestion="${suggestion%%$'\t'*}"
        [ -n "$suggestion" ] && suggestions+=("$suggestion")
    done < <({{{parts.ResolverInvocation}}} "$index" -- "${completionWords[@]}" 2>/dev/null)

    COMPREPLY=()
    while IFS= read -r suggestion; do
        if [ "$quoted" -eq 1 ]; then
            COMPREPLY+=("$suggestion")
        else
            COMPREPLY+=("$(printf '%q' "$suggestion")")
        fi
    done < <(IFS=$'\n'; compgen -W "${suggestions[*]}" -- "$2")
}
complete -F _{{{parts.FunctionName}}} {{{parts.ExecutableName}}}"""

    let private zshScript (parts: ScriptParts) =
        $$$"""#compdef {{{parts.ExecutableName}}}
# Zsh completion script for {{{parts.ExecutableName}}}
# Source this file in your .zshrc:
# source <({{{parts.ScriptInvocation}}} zsh)

_{{{parts.FunctionName}}}() {
    local -a completionWords suggestions described
    local suggestion value description

    # A quote left open by the cursor stays in `words`, while `PREFIX` is the word as typed without it.
    completionWords=("${(@Q)words[1,CURRENT-1]}" "$PREFIX")

    suggestions=(${(f)"$({{{parts.ResolverInvocation}}} $((CURRENT - 1)) -- "${completionWords[@]}" 2>/dev/null)"})
    suggestions=(${suggestions:#})

    # _describe separates the value from its description by a colon, so a colon in a value is escaped.
    described=()
    for suggestion in $suggestions; do
        value=${suggestion%%$'\t'*}
        description=${suggestion#*$'\t'}
        if [[ $description == $suggestion ]]; then
            described+=(${value//:/\\:})
        else
            described+=("${value//:/\\:}:$description")
        fi
    done

    # An assigned value is completed on its own, the `--option=` part is already on the line.
    compset -P '-*='

    _describe -t suggestions 'suggestion' described
}
compdef _{{{parts.FunctionName}}} {{{parts.ExecutableName}}}"""

    let private fishScript (parts: ScriptParts) =
        $$$"""# Fish completion script for {{{parts.ExecutableName}}}
# Source this file in your config.fish:
# source ({{{parts.ScriptInvocation}}} fish | psub)

function __{{{parts.FunctionName}}}
    set -l completionWords (commandline --current-process --tokenize --cut-at-cursor)
    set -l currentWord (commandline --current-token --cut-at-cursor --tokenize)

    # An assigned value is completed on its own, while fish replaces the whole token.
    set -l assignedPrefix ""
    if string match --quiet --regex -- '^-.*=' "$currentWord"
        set assignedPrefix (string replace --regex -- '^(-[^=]*=).*$' '$1' "$currentWord")
    end

    for suggestion in ({{{parts.ResolverInvocation}}} (count $completionWords) -- $completionWords $currentWord 2>/dev/null)
        echo $assignedPrefix$suggestion
    end
end
complete -c {{{parts.ExecutableName}}} -f -a '(__{{{parts.FunctionName}}})'"""

    let generate (shell: Shell) (executableName: string) (scriptCommandName: string) (resolverCommandName: string) =
        let parts = {
            ExecutableName = executableName
            ScriptInvocation = sprintf "%s %s" executableName scriptCommandName
            ResolverInvocation = sprintf "%s %s" executableName resolverCommandName
            FunctionName = functionName executableName
        }

        match shell with
        | Shell.Bash -> bashScript parts
        | Shell.Zsh -> zshScript parts
        | Shell.Fish -> fishScript parts
