#include "g27cfg.h"
#include "g27ids.h"

DWORD g27_cfg_get(const WCHAR *name, DWORD def) {
    DWORD value = def;
    DWORD size = sizeof(value);
    DWORD type = REG_DWORD;
    LONG rc = RegGetValueW(HKEY_CURRENT_USER, REG_TOOL, name, RRF_RT_REG_DWORD, &type,
                            (BYTE *)&value, &size);
    if (rc != ERROR_SUCCESS) {
        return def;
    }
    return value;
}

BOOL g27_cfg_set(const WCHAR *name, DWORD v) {
    HKEY hKey;
    LONG rc = RegCreateKeyExW(HKEY_CURRENT_USER, REG_TOOL, 0, NULL, 0, KEY_WRITE, NULL, &hKey, NULL);
    if (rc != ERROR_SUCCESS) {
        return FALSE;
    }
    rc = RegSetValueExW(hKey, name, 0, REG_DWORD, (const BYTE *)&v, sizeof(v));
    RegCloseKey(hKey);
    return rc == ERROR_SUCCESS;
}

BOOL g27_cfg_get_str(const WCHAR *name, WCHAR *out, DWORD outChars) {
    DWORD size = outChars * sizeof(WCHAR);
    DWORD type = REG_SZ;
    LONG rc = RegGetValueW(HKEY_CURRENT_USER, REG_TOOL, name, RRF_RT_REG_SZ, &type,
                            (BYTE *)out, &size);
    return rc == ERROR_SUCCESS;
}

BOOL g27_cfg_set_str(const WCHAR *name, const WCHAR *v) {
    HKEY hKey;
    LONG rc = RegCreateKeyExW(HKEY_CURRENT_USER, REG_TOOL, 0, NULL, 0, KEY_WRITE, NULL, &hKey, NULL);
    if (rc != ERROR_SUCCESS) {
        return FALSE;
    }
    DWORD size = (DWORD)((wcslen(v) + 1) * sizeof(WCHAR));
    rc = RegSetValueExW(hKey, name, 0, REG_SZ, (const BYTE *)v, size);
    RegCloseKey(hKey);
    return rc == ERROR_SUCCESS;
}

BOOL g27_cfg_has(const WCHAR *name) {
    WCHAR buf[8];
    DWORD size = sizeof(buf);
    DWORD type = 0;
    LONG rc = RegGetValueW(HKEY_CURRENT_USER, REG_TOOL, name, RRF_RT_ANY, &type, (BYTE *)buf, &size);
    return rc == ERROR_SUCCESS || rc == ERROR_MORE_DATA;
}

BOOL g27_cfg_delete(const WCHAR *name) {
    HKEY hKey;
    LONG rc = RegOpenKeyExW(HKEY_CURRENT_USER, REG_TOOL, 0, KEY_WRITE, &hKey);
    if (rc != ERROR_SUCCESS) {
        return FALSE;
    }
    rc = RegDeleteValueW(hKey, name);
    RegCloseKey(hKey);
    return rc == ERROR_SUCCESS;
}
