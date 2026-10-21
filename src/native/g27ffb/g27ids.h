/* Baseado em gcampa/g27-ffb-driverless (MIT, commit 415c871), ver LICENSE-gcampa.txt.
   G27 Hub: CLSID proprio e configuracao em HKCU\Software\G27Hub\FFB. */
#ifndef G27IDS_H
#define G27IDS_H

#include <windows.h>

#ifndef WINBOOL
#define WINBOOL BOOL /* tipo do mingw usado pelo código original; MSVC usa BOOL */
#endif

/* ---- VID/PID ---- */
#define G27_VID          0x046D
#define G27_PID_COMPAT   0xC294
#define G27_PID_NATIVE   0xC29B
#define G27_BCD_MASK     0xFFF0
#define G27_BCD_VALUE    0x1230

/* ---- Fixed HID commands (7 bytes, no report ID) ---- */
static const BYTE CMD_REVERT_ON_RESET[7] = {0xF8, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x00};
static const BYTE CMD_SWITCH_G27[7]      = {0xF8, 0x09, 0x04, 0x01, 0x00, 0x00, 0x00};
static const BYTE CMD_STOP[7]            = {0x13, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
static const BYTE CMD_AUTOCENTER_OFF[7]  = {0xF5, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};

/* ---- CLSID of the FFB driver (generated once, then fixed) ---- */
#define G27FFB_CLSID_STR L"{44CD99D0-68D8-40C3-917D-0F52047DFCEB}"
static const GUID CLSID_G27FFB =
    {0x44CD99D0, 0x68D8, 0x40C3, {0x91, 0x7D, 0x0F, 0x52, 0x04, 0x7D, 0xFC, 0xEB}};

/* ---- Direction of the force ----
   Calibrated empirically on real hardware: with LV=0xC0 (a positive force) the wheel
   pushed LEFT, and with LV=0x40 it pushed the opposite way (right). If force feedback
   works but pushes toward the lock stops instead of centering on your setup, don't edit
   this - use 'g27ctl invert on', which flips the sign at runtime. */
#define G27_LV_POSITIVE_IS_RIGHT 0

/* ---- Rotation range limits, in degrees ---- */
#define G27_RANGE_MIN 40u
#define G27_RANGE_MAX 900u

/* ---- Hardware auto-center spring field ranges (see g27_autocenter_fields) ---- */
#define G27_AC_COEFF_MAX 7u   /* spring slope coefficient, 0..7 */
#define G27_AC_CLIP_MAX  255u /* force clip, full 8-bit scale */

/* ---- Registry paths (relative to HKCU) ---- */
#define REG_OEM_C29B \
    L"System\\CurrentControlSet\\Control\\MediaProperties\\PrivateProperties\\Joystick\\OEM\\VID_046D&PID_C29B"
#define REG_OEM_C29B_FF \
    L"System\\CurrentControlSet\\Control\\MediaProperties\\PrivateProperties\\Joystick\\OEM\\VID_046D&PID_C29B\\OEMForceFeedback"
#define REG_TOOL        L"Software\\G27Hub\\FFB"
#define REG_CLSID_BASE  L"Software\\Classes\\CLSID\\"

/* ---- DirectInput/COM GUIDs, used by g27ffb.c and tests/fftest.c ---- */
static const GUID IID_IDirectInputEffectDriver_G27 =
    {0x02538130, 0x898F, 0x11D0, {0x9A, 0xD0, 0x00, 0xA0, 0xC9, 0xA0, 0x6E, 0x35}};
static const GUID IID_IUnknown_G27 =
    {0x00000000, 0x0000, 0x0000, {0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}};
static const GUID IID_IClassFactory_G27 =
    {0x00000001, 0x0000, 0x0000, {0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}};
static const GUID IID_IDirectInput8W_G27 =
    {0xBF798031, 0x483A, 0x4DA2, {0xAA, 0x99, 0x5D, 0x64, 0xED, 0x36, 0x97, 0x00}};
static const GUID GUID_ConstantForce_G27 =
    {0x13541C20, 0x8E33, 0x11D0, {0x9A, 0xD0, 0x00, 0xA0, 0xC9, 0xA0, 0x6E, 0x35}};
static const GUID GUID_Sine_G27 =
    {0x13541C23, 0x8E33, 0x11D0, {0x9A, 0xD0, 0x00, 0xA0, 0xC9, 0xA0, 0x6E, 0x35}};
static const GUID GUID_Spring_G27 =
    {0x13541C27, 0x8E33, 0x11D0, {0x9A, 0xD0, 0x00, 0xA0, 0xC9, 0xA0, 0x6E, 0x35}};
static const GUID GUID_Damper_G27 =
    {0x13541C28, 0x8E33, 0x11D0, {0x9A, 0xD0, 0x00, 0xA0, 0xC9, 0xA0, 0x6E, 0x35}};
static const GUID GUID_XAxis_G27 =
    {0xA36D02E0, 0xC9F3, 0x11CF, {0xBF, 0xC7, 0x44, 0x45, 0x53, 0x54, 0x00, 0x00}};

#endif /* G27IDS_H */
