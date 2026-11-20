#!/usr/bin/env bash
# Exports the connection strings of the integration-test databases. Without them, those tests are skipped with the
# reason. Usage:  source scripts/test-env.sh
#
# PostgreSQL: a local server with a dedicated database (default: wdc_cube_test, as the OS user, no password).
#   Override with WDC_TEST_POSTGRES.
# SQL Server: set WDC_TEST_SQLSERVER yourself, or WDC_TEST_SQLSERVER_HOST (plus _USER, default "sa", and _DATABASE,
#   default "wdc-cube") with the password in the macOS Keychain, stored once with:
#     security add-generic-password -s wdc-cube-test-sqlserver -a <user> -w
#   (it asks for the password; nothing goes to the shell history). The password is never printed.
#
# Machine-specific values go in scripts/test-env.local.sh (ignored by git), read first.

_wdc_local="$(dirname "${BASH_SOURCE[0]:-$0}")/test-env.local.sh"
# shellcheck source=/dev/null
[ -f "$_wdc_local" ] && . "$_wdc_local"
unset _wdc_local

export WDC_TEST_POSTGRES="${WDC_TEST_POSTGRES:-Host=localhost;Database=wdc_cube_test}"

if [ -z "${WDC_TEST_SQLSERVER:-}" ] && [ -n "${WDC_TEST_SQLSERVER_HOST:-}" ]; then
    _wdc_user="${WDC_TEST_SQLSERVER_USER:-sa}"
    _wdc_pwd="$(security find-generic-password -s wdc-cube-test-sqlserver -a "$_wdc_user" -w 2>/dev/null)"
    if [ -n "$_wdc_pwd" ]; then
        export WDC_TEST_SQLSERVER="Server=${WDC_TEST_SQLSERVER_HOST};Database=${WDC_TEST_SQLSERVER_DATABASE:-wdc-cube};User Id=${_wdc_user};Password=${_wdc_pwd};Encrypt=true;TrustServerCertificate=true"
    else
        echo "test-env: no Keychain item 'wdc-cube-test-sqlserver' for '$_wdc_user' — SQL Server tests will be skipped." >&2
    fi
    unset _wdc_pwd _wdc_user
fi

if [ -z "${WDC_TEST_SQLSERVER:-}" ]; then
    echo "test-env: no SQL Server configured — its tests will be skipped." >&2
fi
