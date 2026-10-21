#ifndef G27CFG_H
#define G27CFG_H

#include <windows.h>

/* Configuracao do driver em HKCU\Software\G27Hub\FFB (valores DWORD/REG_SZ). */
DWORD g27_cfg_get(const WCHAR *name, DWORD def);
BOOL g27_cfg_set(const WCHAR *name, DWORD v);
BOOL g27_cfg_get_str(const WCHAR *name, WCHAR *out, DWORD outChars);
BOOL g27_cfg_set_str(const WCHAR *name, const WCHAR *v);
BOOL g27_cfg_has(const WCHAR *name);
BOOL g27_cfg_delete(const WCHAR *name);

#endif /* G27CFG_H */
