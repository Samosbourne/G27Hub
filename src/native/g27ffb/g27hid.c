#include "g27hid.h"
#include "g27ids.h"

#include <setupapi.h>
#include <hidsdi.h>
#include <hidpi.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

int g27_find(G27Device *out, int max) {
    int count = 0;
    GUID hidGuid;
    HidD_GetHidGuid(&hidGuid);

    HDEVINFO devInfo = SetupDiGetClassDevsW(&hidGuid, NULL, NULL, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
    if (devInfo == INVALID_HANDLE_VALUE) {
        return 0;
    }

    SP_DEVICE_INTERFACE_DATA ifData;
    ifData.cbSize = sizeof(ifData);

    for (DWORD idx = 0; count < max && SetupDiEnumDeviceInterfaces(devInfo, NULL, &hidGuid, idx, &ifData); idx++) {
        DWORD needed = 0;
        SetupDiGetDeviceInterfaceDetailW(devInfo, &ifData, NULL, 0, &needed, NULL);
        if (needed == 0) {
            continue;
        }

        PSP_DEVICE_INTERFACE_DETAIL_DATA_W detail = (PSP_DEVICE_INTERFACE_DETAIL_DATA_W)malloc(needed);
        if (!detail) {
            continue;
        }
        detail->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);

        if (SetupDiGetDeviceInterfaceDetailW(devInfo, &ifData, detail, needed, NULL, NULL)) {
            HANDLE h = CreateFileW(detail->DevicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                    NULL, OPEN_EXISTING, 0, NULL);
            if (h != INVALID_HANDLE_VALUE) {
                HIDD_ATTRIBUTES attrs;
                attrs.Size = sizeof(attrs);
                if (HidD_GetAttributes(h, &attrs) && attrs.VendorID == G27_VID) {
                    BOOL isCompat = (attrs.ProductID == G27_PID_COMPAT) &&
                                     ((attrs.VersionNumber & G27_BCD_MASK) == G27_BCD_VALUE);
                    BOOL isNative = (attrs.ProductID == G27_PID_NATIVE);
                    if (isCompat || isNative) {
                        wcsncpy(out[count].path, detail->DevicePath, 511);
                        out[count].path[511] = 0;
                        out[count].pid = attrs.ProductID;
                        out[count].version = attrs.VersionNumber;
                        count++;
                    }
                }
                CloseHandle(h);
            }
        }
        free(detail);
    }

    SetupDiDestroyDeviceInfoList(devInfo);
    return count;
}

HANDLE g27_open(const WCHAR *path, BOOL overlapped) {
    DWORD flags = overlapped ? FILE_FLAG_OVERLAPPED : 0;
    return CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                        NULL, OPEN_EXISTING, flags, NULL);
}

BOOL g27_send(HANDLE h, const BYTE cmd7[7]) {
    BYTE buf[8];
    buf[0] = 0x00;
    memcpy(&buf[1], cmd7, 7);

    DWORD written = 0;
    if (WriteFile(h, buf, 8, &written, NULL) && written == 8) {
        return TRUE;
    }
    return HidD_SetOutputReport(h, buf, 8);
}

BOOL g27_caps(HANDLE h, USHORT *inLen, USHORT *outLen) {
    PHIDP_PREPARSED_DATA pp;
    if (!HidD_GetPreparsedData(h, &pp)) {
        return FALSE;
    }

    HIDP_CAPS caps;
    BOOL ok = (HidP_GetCaps(pp, &caps) == HIDP_STATUS_SUCCESS);
    if (ok) {
        if (inLen) *inLen = caps.InputReportByteLength;
        if (outLen) *outLen = caps.OutputReportByteLength;
    }
    HidD_FreePreparsedData(pp);
    return ok;
}

/* Builds the two reports that configure and enable the wheel's hardware
   auto-centering spring.
 *
 * Report layout (protocol facts, see README "Acknowledgments"):
 *   0xFE 0x0D  kLeft  kRight  clip  00 00   set variable-resistance spring
 *   0x14       00     00      00    00 00 00  enable auto-center
 *
 * where kLeft/kRight are the spring's slope coefficients (valid range 0..7,
 * higher = stiffer) and clip is the ceiling on the force the spring may apply
 * (0..255, i.e. full 8-bit scale).
 *
 * The percentage -> (coefficient, clip) mapping is our own, and it is a table of
 * points measured by hand on a real G27 rather than a formula. Two closed-form
 * curves were tried first and both failed against the hardware: a plain linear
 * mapping was clearly too strong through the middle of the dial (50% felt close to
 * full strength), while a single power-law taper that fixed the middle made the
 * bottom of the dial too weak to overcome the wheel's own stiction - it would
 * accept the command and then simply not return the wheel to centre.
 *
 * No single exponent can satisfy both ends: the measured low point implies an
 * exponent below 1, the measured mid point implies about 1.4. The curve that
 * actually feels right is mildly S-shaped - usable as soon as you leave zero, then
 * finely progressive through the middle - so it is expressed as anchors with linear
 * interpolation between them. Integer arithmetic only, evaluated once per command.
 *
 * Which points are real measurements, so you know what you may move:
 *   25% -> 2/77   tuned by feel; at 2/64 it centred but was about 20% too weak
 *   50% -> 3/97   confirmed as the right mid-scale weight on real hardware
 *    1% -> 2/64   the weakest spring that still reliably centres the wheel
 *  100% -> 7/255  full scale of both protocol fields, by definition
 * Everything else is interpolation. If you are porting this to another wheel, take
 * your own measurements with 'g27ctl autocenter-raw COEFF CLIP' and replace the
 * table; that command bypasses this curve entirely.
 */
typedef struct {
    unsigned pct;
    BYTE coeff;
    BYTE clip;
} G27AcPoint;

static const G27AcPoint g27AcCurve[] = {
    {   1, 2,  64 },
    {  25, 2,  77 },
    {  50, 3,  97 },
    { 100, 7, 255 },
};

BOOL g27_autocenter_fields(unsigned pct, BYTE *coeff, BYTE *clip) {
    if (pct == 0) {
        return FALSE;
    }
    if (pct > 100) pct = 100;

    const unsigned n = (unsigned)(sizeof(g27AcCurve) / sizeof(g27AcCurve[0]));
    unsigned coeffv, clipv;

    if (pct <= g27AcCurve[0].pct) {
        /* Below the first anchor there is nothing to interpolate towards: anything
           weaker would not centre the wheel, and 0 was already handled as "off". */
        coeffv = g27AcCurve[0].coeff;
        clipv  = g27AcCurve[0].clip;
    } else {
        unsigned i = n - 1;
        for (unsigned j = 1; j < n; j++) {
            if (pct <= g27AcCurve[j].pct) {
                i = j;
                break;
            }
        }
        const G27AcPoint *lo = &g27AcCurve[i - 1];
        const G27AcPoint *hi = &g27AcCurve[i];
        unsigned span = hi->pct - lo->pct;
        unsigned off = pct - lo->pct;

        /* Linear interpolation, rounded to nearest. The table is monotonic, so the
           differences below are never negative. */
        coeffv = lo->coeff + (((unsigned)(hi->coeff - lo->coeff) * off + span / 2) / span);
        clipv  = lo->clip  + (((unsigned)(hi->clip  - lo->clip)  * off + span / 2) / span);
    }

    if (coeffv > G27_AC_COEFF_MAX) coeffv = G27_AC_COEFF_MAX;
    if (clipv > G27_AC_CLIP_MAX) clipv = G27_AC_CLIP_MAX;

    if (coeff) *coeff = (BYTE)coeffv;
    if (clip) *clip = (BYTE)clipv;
    return TRUE;
}

BOOL g27_autocenter_apply_raw(HANDLE h, BYTE coeff, BYTE clip) {
    /* Disabling first is not optional: the wheel latches the spring parameters while
       the spring is enabled, and silently ignores a new parameter report. Without this
       every change after the first one appears to do nothing at all. */
    if (!g27_send(h, CMD_AUTOCENTER_OFF)) {
        return FALSE;
    }

    BYTE cmd1[7] = {0xFE, 0x0D, coeff, coeff, clip, 0x00, 0x00};
    BYTE cmd2[7] = {0x14, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
    return g27_send(h, cmd1) && g27_send(h, cmd2);
}

BOOL g27_autocenter_apply(HANDLE h, unsigned pct) {
    BYTE coeff = 0, clip = 0;
    if (!g27_autocenter_fields(pct, &coeff, &clip)) {
        return g27_send(h, CMD_AUTOCENTER_OFF);
    }
    return g27_autocenter_apply_raw(h, coeff, clip);
}
