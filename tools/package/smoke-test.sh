#!/bin/sh
# Launches the app with --smoke-test several times and reports startup times (spike S6, macOS
# and Linux). Each run opens the fixture, waits until the first page is sharp, asks the worker for
# qpdf's version, and exits. A run passes only if the page rendered and qpdf loaded. On a fresh
# machine the first run is a cold start. Exits with 1 if any run fails. Windows uses smoke-test.ps1.
#
# POSIX sh and perl only, so it also runs in a bare Debian or Ubuntu container. Linux without a
# display runs the app under xvfb-run.
#
# Usage: smoke-test.sh <label> <fixture.pdf> <runs> <command> [arguments...]
set -eu

label=$1
fixture=$2
runs=$3
shift 3
case $fixture in /*) ;; *) fixture="$PWD/$fixture" ;; esac

outputs="${TMPDIR:-/tmp}/pdf-suite-smoke-$$"
mkdir -p "$outputs"

launcher=""
if [ "$(uname)" = Linux ] && [ -z "${DISPLAY:-}" ]; then
    launcher="xvfb-run -a"
fi

notice() {
    if [ "${GITHUB_ACTIONS:-}" = true ]; then echo "::notice title=$1::$2"; else echo "$1: $2"; fi
}

field() {
    sed -n "s/.*\"$1\": *\"\{0,1\}\([^\",]*\)\"\{0,1\},\{0,1\}\$/\1/p" "$2" | head -n 1
}

failed=0
run=1
while [ "$run" -le "$runs" ]; do
    out="$outputs/run-$run.json"
    # perl's alarm is a portable timeout: macOS has no timeout command.
    set +e
    perl -e 'alarm shift; exec @ARGV or die "exec failed: $!\n"' 120 $launcher "$@" "$fixture" --smoke-test --out "$out"
    code=$?
    set -e

    if [ ! -f "$out" ]; then
        notice "Smoke test failed ($label)" "Run $run exited with code $code and wrote no results."
        failed=$((failed + 1))
    else
        cat "$out"
        echo
        kind=warm
        [ "$run" -eq 1 ] && kind="first run"
        summary="run $run ($kind): exit $code; $(field codeGeneration "$out"); window $(field processStartToWindowMs "$out") ms; first page sharp $(field processStartToFirstPageSharpMs "$out") ms; qpdf $(field qpdfVersion "$out")"
        if [ "$code" -eq 0 ] && grep -q '"firstPageSharp": true' "$out"; then
            notice "Smoke test ($label)" "$summary"
        else
            notice "Smoke test failed ($label)" "$summary; $(field qpdfError "$out")"
            failed=$((failed + 1))
        fi
    fi
    run=$((run + 1))
done

[ "$failed" -eq 0 ]
