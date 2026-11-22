#!/usr/bin/env bash
# Keep parent-shell exports in Bash or zsh; parse literal assignments with Python via uv.
# The Python parser tolerates the Windows-saved shapes of .env.local (BOM, CRLF, quotes).
# DXAUDIO_ENV_UV can pin uv when the caller uses a restricted PATH.

# Resolve the loader beside this file while it is being sourced: zsh has no BASH_SOURCE.
if [ -n "${ZSH_VERSION:-}" ]; then
    eval '_dxaudio_env_local_dir=${${(%):-%x}:h}'
else
    _dxaudio_env_local_dir="${BASH_SOURCE[0]%/*}"
fi

load_env_local() {
    # zsh: run under default options so a user's setopt (KSH_ARRAYS, NOUNSET...) cannot leak in.
    if [ -n "${ZSH_VERSION:-}" ]; then
        emulate -L zsh
    fi
    local env_file="${1:-${AI_BACKENDS_ENV_FILE:-}}"
    if [ -n "${env_file}" ]; then
        [ -f "${env_file}" ] || return 0
    elif [ -f "${WORKSPACE_ROOT:-}/.env.local" ]; then
        env_file="${WORKSPACE_ROOT}/.env.local"
    elif [ -f ".env.local" ]; then
        env_file=".env.local"
    else
        return 0
    fi

    local loader="${_dxaudio_env_local_dir}/env_local.py"
    local key value overwrite current complete=false
    local -a assignments=()
    while IFS= read -r -d '' key && IFS= read -r -d '' value && IFS= read -r -d '' overwrite; do
        if [ -z "${key}" ]; then
            complete=true
            break
        fi
        # The Python side validates names; re-check before the indirect read below.
        case "${key}" in
            [A-Za-z_]*) ;;
            *) continue ;;
        esac
        case "${key}" in
            *[!A-Za-z0-9_]*) continue ;;
        esac
        # Preserve non-exported shell values, which the child cannot inherit.
        eval "current=\"\${${key}:-}\""
        if [ -n "${current}" ] && [ "${overwrite}" != 1 ]; then
            continue
        fi
        assignments+=("${key}=${value}")
    done < <(DXAUDIO_ENV_LOCAL_PRECEDENCE="${DXAUDIO_ENV_LOCAL_PRECEDENCE:-}" "${DXAUDIO_ENV_UV:-uv}" run --no-project "${loader}" -- "${env_file}")
    if [ "${complete}" != true ]; then
        printf '%s\n' 'Cannot load environment assignments; no exports applied.' >&2
        return 1
    fi
    if [ "${#assignments[@]}" -ne 0 ]; then
        export "${assignments[@]}"
    fi
}
