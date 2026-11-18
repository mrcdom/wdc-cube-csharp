#!/usr/bin/env bash
# Exports the connection strings of the integration-test databases (decision D-14).
# Usage:  source scripts/test-env.sh
# Secrets come from the macOS Keychain and are never printed. Store them once with:
#   security add-generic-password -s wdc-cube-test-sqlserver -a <user> -w
# (it asks for the password; nothing goes to the shell history)

export WDC_TEST_POSTGRES="${WDC_TEST_POSTGRES:-Host=localhost;Database=wdc_cube_test}"

if [ -z "${WDC_TEST_SQLSERVER:-}" ]; then
    _wdc_pwd="$(security find-generic-password -s wdc-cube-test-sqlserver -a <user> -w 2>/dev/null)"
    if [ -n "$_wdc_pwd" ]; then
        export WDC_TEST_SQLSERVER="Server=sqlserver.example.com,1433;Database=wdc-cube;User Id=<user>;Password=${_wdc_pwd};Encrypt=true;TrustServerCertificate=true"
    else
        echo "test-env: no Keychain item 'wdc-cube-test-sqlserver' — SQL Server tests will be skipped." >&2
    fi
    unset _wdc_pwd
fi
